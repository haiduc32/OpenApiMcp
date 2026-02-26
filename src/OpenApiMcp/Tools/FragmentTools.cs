using System.ComponentModel;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using ModelContextProtocol.Server;
using OpenApiMcp.Services;
using static OpenApiMcp.Tools.ToolHelper;

namespace OpenApiMcp.Tools;

[McpServerToolType]
public sealed class FragmentTools(ContractStore store)
{
    // ── get_fragment ──────────────────────────────────────────────────────────

    [McpServerTool, Description("Advanced: Read any subtree of the contract by JSON Pointer (RFC 6901). Prefer get_operation or get_schema for reading operations and schemas — use this only for nodes those tools do not cover.")]
    public string get_fragment(
        [Description("The contract ID.")]                         string  contract_id,
        [Description("JSON Pointer, e.g. /paths/~1users/get.")]  string  pointer,
        [Description("Inline immediate $ref targets.")]           bool    resolve_refs = true,
        [Description("Output format: json | yaml | native.")]    string  format       = "native") => Run(() =>
    {
        var doc  = store.Get(contract_id).Document;
        var node = pointer == "" ? doc : JsonPointerHelper.Navigate(doc, pointer);

        if (node is null)
            return Ok(new { pointer, exists = false, content = (string?)null, ref_targets = Array.Empty<string>() });

        List<string> unresolvedRefs = [];
        JsonNode content;
        if (resolve_refs)
            content = JsonPointerHelper.ResolveRefsShallow(doc, node, out unresolvedRefs);
        else
        {
            content = node.DeepClone();
            unresolvedRefs = JsonPointerHelper.CollectRefs(node).ToList();
        }

        var fmt = format == "native" ? "json" : format;
        return Ok(new
        {
            pointer,
            exists      = true,
            content     = ContentSerializer.Serialise(content, fmt),
            ref_targets = unresolvedRefs.Distinct().ToArray()
        });
    });

    // ── get_operation ─────────────────────────────────────────────────────────

    [McpServerTool, Description("Retrieve a single HTTP operation (e.g. GET /users, POST /orders) with all immediate $ref values resolved inline. Use this to inspect the full request parameters, request body, and response schemas for a specific endpoint.")]
    public string get_operation(
        [Description("The contract ID.")]          string contract_id,
        [Description("Path, e.g. /users/{id}.")]   string path,
        [Description("HTTP method, e.g. get.")]    string method) => Run(() =>
    {
        var doc = store.Get(contract_id).Document;
        var escapedPath = path.Replace("~", "~0").Replace("/", "~1");
        var pointer = $"/paths/{escapedPath}/{method.ToLowerInvariant()}";
        var node    = JsonPointerHelper.Navigate(doc, pointer)
                      ?? throw new McpException("INVALID_POINTER", $"Operation not found: {method.ToUpper()} {path}", pointer);

        var resolved = JsonPointerHelper.ResolveRefsShallow(doc, node, out var unresolved);
        return Ok(new
        {
            pointer,
            operation = ContentSerializer.Serialise(resolved, "json"),
            refs      = unresolved.Distinct().ToArray()
        });
    });

    // ── get_schema ────────────────────────────────────────────────────────────

    [McpServerTool, Description("Retrieve a named schema from components/schemas with optional $ref resolution. Use this to inspect a specific OpenAPI data model.")]
    public string get_schema(
        [Description("The contract ID.")]                               string  contract_id,
        [Description("Schema name.")]                                   string  name,
        [Description("How many $ref levels to inline (1–3).")]         int     resolve_depth = 1) => Run(() =>
    {
        var doc     = store.Get(contract_id).Document;
        var pointer = $"/components/schemas/{name}";
        var node    = JsonPointerHelper.Navigate(doc, pointer)
                      ?? throw new McpException("INVALID_POINTER", $"Schema '{name}' not found.", pointer);

        var depth   = Math.Clamp(resolve_depth, 0, 3);
        var resolved = JsonPointerHelper.ResolveRefsDepth(doc, node, depth, out var unresolved);

        return Ok(new
        {
            pointer,
            content         = ContentSerializer.Serialise(resolved, "json"),
            resolved_refs   = new Dictionary<string, object>(), // resolved inline
            unresolved_refs = unresolved.Distinct().ToArray()
        });
    });

    // ── search_contract ───────────────────────────────────────────────────────

    [McpServerTool, Description("Search across paths, operations, and schemas for a keyword or regex pattern. Use this to explore or locate relevant sections of a contract — particularly useful for large contracts where browsing the full index is impractical.")]
    public string search_contract(
        [Description("The contract ID.")]                                  string  contract_id,
        [Description("Free text or regex pattern.")]                       string  query,
        [Description("Scope: paths | schemas | all.")]                     string  scope       = "all",
        [Description("Maximum results to return.")]                        int     max_results  = 20) => Run(() =>
    {
        var doc     = store.Get(contract_id).Document;
        var results = new List<object>();
        Regex? regex = null;
        try { regex = new Regex(query, RegexOptions.IgnoreCase); }
        catch { regex = new Regex(Regex.Escape(query), RegexOptions.IgnoreCase); }

        int count = 0;

        if ((scope == "paths" || scope == "all") && count < max_results)
        {
            var paths = doc["paths"] as JsonObject;
            if (paths is not null)
            {
                foreach (var (path, pathItem) in paths)
                {
                    if (count >= max_results) break;
                    if (pathItem is not JsonObject pi) continue;

                    foreach (var method in new[] { "get","post","put","delete","patch","options","head","trace" })
                    {
                        if (count >= max_results) break;
                        var op = pi[method] as JsonObject;
                        if (op is null) continue;

                        var text = op.ToJsonString();
                        if (regex.IsMatch(text))
                        {
                            var escaped = path.Replace("~","~0").Replace("/","~1");
                            results.Add(new
                            {
                                pointer       = $"/paths/{escaped}/{method}",
                                kind          = "operation",
                                match_context = TrimContext(text, query, 120)
                            });
                            count++;
                        }
                    }

                    // also match the path string itself
                    if (count < max_results && regex.IsMatch(path))
                    {
                        var escaped = path.Replace("~","~0").Replace("/","~1");
                        results.Add(new
                        {
                            pointer       = $"/paths/{escaped}",
                            kind          = "path",
                            match_context = path
                        });
                        count++;
                    }
                }
            }
        }

        if ((scope == "schemas" || scope == "all") && count < max_results)
        {
            var schemas = doc["components"]?["schemas"] as JsonObject;
            if (schemas is not null)
            {
                foreach (var (name, schema) in schemas)
                {
                    if (count >= max_results) break;
                    var text = name + " " + (schema?.ToJsonString() ?? "");
                    if (regex.IsMatch(text))
                    {
                        results.Add(new
                        {
                            pointer       = $"/components/schemas/{name}",
                            kind          = "schema",
                            match_context = TrimContext(text, query, 120)
                        });
                        count++;
                    }
                }
            }
        }

        return Ok(new { results });
    });

    // ── get_tag_operations ────────────────────────────────────────────────────

    [McpServerTool, Description("Return all operations associated with a specific OpenAPI tag. Use this to see every endpoint in a logical group (e.g. all 'users' or 'orders' operations).")]
    public string get_tag_operations(
        [Description("The contract ID.")]   string contract_id,
        [Description("Tag name.")]          string tag) => Run(() =>
    {
        var doc  = store.Get(contract_id).Document;
        var ops  = new List<object>();
        var paths = doc["paths"] as JsonObject;

        if (paths is not null)
        {
            foreach (var (path, pathItem) in paths)
            {
                if (pathItem is not JsonObject pi) continue;
                foreach (var method in new[] { "get","post","put","delete","patch","options","head","trace" })
                {
                    var op = pi[method] as JsonObject;
                    if (op is null) continue;
                    var tags = (op["tags"] as JsonArray)?.Select(t => t?.GetValue<string>()).ToArray() ?? [];
                    if (!tags.Contains(tag, StringComparer.OrdinalIgnoreCase)) continue;

                    var escaped = path.Replace("~","~0").Replace("/","~1");
                    ops.Add(new
                    {
                        path,
                        method,
                        operationId = op["operationId"]?.GetValue<string>(),
                        summary     = op["summary"]?.GetValue<string>(),
                        pointer     = $"/paths/{escaped}/{method}"
                    });
                }
            }
        }

        return Ok(new { tag, operations = ops });
    });

    // ── Private helpers ───────────────────────────────────────────────────────

    private static string TrimContext(string text, string query, int maxLen)
    {
        var idx = text.IndexOf(query, StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return text.Length > maxLen ? text[..maxLen] + "…" : text;
        var start = Math.Max(0, idx - 30);
        var end   = Math.Min(text.Length, idx + query.Length + 60);
        var snippet = (start > 0 ? "…" : "") + text[start..end] + (end < text.Length ? "…" : "");
        return snippet;
    }
}

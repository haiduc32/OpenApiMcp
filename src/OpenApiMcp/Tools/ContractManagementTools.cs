using System.ComponentModel;
using System.Text.Json.Nodes;
using ModelContextProtocol.Server;
using OpenApiMcp.Services;
using static OpenApiMcp.Tools.ToolHelper;

namespace OpenApiMcp.Tools;

[McpServerToolType]
public sealed class ContractManagementTools(ContractStore store)
{
    // ── list_contracts ────────────────────────────────────────────────────────

    [McpServerTool, Description("List all OpenAPI contracts registered with this server. Use this to discover available contract IDs before calling any other tool.")]
    public string list_contracts() => Run(() =>
    {
        var contracts = store.GetAll().Select(c => new
        {
            id         = c.Id,
            title      = c.Title,
            version    = c.Version,
            openapi    = c.OpenApiVersion,
            source     = c.Source,
            size_lines = c.SizeLines
        });
        return Ok(new { contracts });
    });

    // ── get_contract_info ─────────────────────────────────────────────────────

    [McpServerTool, Description("Retrieve the top-level info, servers, security, and tags blocks of a contract. Always small and safe to load as context to understand the contract's purpose and available tags.")]
    public string get_contract_info(
        [Description("The contract ID.")] string contract_id) => Run(() =>
    {
        var entry = store.Get(contract_id);
        var doc   = entry.Document;

        var result = new JsonObject();
        foreach (var key in new[] { "info", "servers", "security", "tags" })
        {
            var val = doc[key];
            if (val is not null)
                result[key] = val.DeepClone();
        }
        return OkNode(result);
    });

    // ── get_contract_index ────────────────────────────────────────────────────

    [McpServerTool, Description("Retrieve a navigable index of all paths, HTTP methods, operation summaries, and component schema names in a contract. Use this to orient yourself before reading or editing — it is compact and fits in context even for large contracts.")]
    public string get_contract_index(
        [Description("The contract ID.")]     string  contract_id,
        [Description("Group paths by tag.")] bool    include_tags = false) => Run(() =>
    {
        var entry = store.Get(contract_id);
        var doc   = entry.Document;

        // Build paths index
        var pathsNode = doc["paths"] as JsonObject;
        var pathList  = new List<object>();

        if (pathsNode is not null)
        {
            foreach (var (path, pathItem) in pathsNode)
            {
                if (pathItem is not JsonObject pi) continue;
                var methods = new List<object>();
                foreach (var m in new[] { "get","post","put","delete","patch","options","head","trace" })
                {
                    var op = pi[m] as JsonObject;
                    if (op is null) continue;
                    methods.Add(new
                    {
                        method      = m,
                        operationId = op["operationId"]?.GetValue<string>(),
                        summary     = op["summary"]?.GetValue<string>(),
                        tags        = (op["tags"] as JsonArray)?.Select(t => t?.GetValue<string>()).ToArray(),
                        deprecated  = op["deprecated"]?.GetValue<bool>() ?? false
                    });
                }
                pathList.Add(new { path, methods });
            }
        }

        // Build components index
        var comps      = doc["components"] as JsonObject;
        var compSections = new[] { "schemas","parameters","responses","requestBodies","headers","securitySchemes","callbacks","pathItems" };
        var components = new Dictionary<string, string[]>();
        foreach (var section in compSections)
        {
            var sect = comps?[section] as JsonObject;
            if (sect is not null)
                components[section] = sect.Select(kv => kv.Key).ToArray();
        }

        object result;
        if (include_tags)
        {
            // group paths by tag
            var byTag = new Dictionary<string, List<object>>();
            foreach (var pathEntry in pathList.Cast<dynamic>())
            {
                var tagSet = new HashSet<string>();
                foreach (var m in pathEntry.methods)
                    foreach (var t in m.tags ?? Array.Empty<string>())
                        if (t is not null) tagSet.Add(t);
                if (tagSet.Count == 0) tagSet.Add("(untagged)");
                foreach (var tag in tagSet)
                {
                    if (!byTag.TryGetValue(tag, out var list)) byTag[tag] = list = [];
                    list.Add(pathEntry);
                }
            }
            result = new { paths_by_tag = byTag, components };
        }
        else
        {
            result = new { paths = pathList, components };
        }

        return Ok(result);
    });

    // ── register_contract (§8.3 optional) ────────────────────────────────────

    [McpServerTool, Description("Register an OpenAPI contract from a file path or inline content. Must be called before working with a contract. Returns the contract ID used by all other tools.")]
    public string register_contract(
        [Description("Absolute file path to a .json or .yaml OpenAPI file. Provide either this or 'content'.")] string? file_path = null,
        [Description("Inline OpenAPI content (JSON or YAML). Provide either this or 'file_path'.")]             string? content   = null,
        [Description("Explicit contract ID to assign. If omitted, derived from the title.")]                     string? id        = null,
        [Description("Content format hint when using 'content': json | yaml.")]                                  string  format    = "yaml") => Run(() =>
    {
        Models.ContractEntry entry;
        if (file_path is not null)
        {
            if (!File.Exists(file_path))
                throw new McpException("INVALID_CONTENT", $"File not found: {file_path}");
            entry = store.Register(file_path);
        }
        else if (content is not null)
        {
            var derivedId = id ?? $"contract-{Guid.NewGuid():N}"[..16];
            entry = store.RegisterFromContent(derivedId, content, format);
        }
        else
        {
            throw new McpException("INVALID_CONTENT", "Provide either 'file_path' or 'content'.");
        }

        return Ok(new
        {
            id         = entry.Id,
            title      = entry.Title,
            version    = entry.Version,
            openapi    = entry.OpenApiVersion,
            size_lines = entry.SizeLines
        });
    });

    // ── unregister_contract (§8.3 optional) ──────────────────────────────────

    [McpServerTool, Description("Unregister a contract from this server (in-memory only; the backing file is not deleted). Use this to remove a contract that is no longer needed.")]
    public string unregister_contract(
        [Description("The contract ID to remove.")] string contract_id) => Run(() =>
    {
        store.Get(contract_id); // throws CONTRACT_NOT_FOUND if missing
        store.Unregister(contract_id);
        return Ok(new { contract_id, removed = true });
    });
}

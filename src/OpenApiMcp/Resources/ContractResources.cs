using System.ComponentModel;
using System.Text;
using System.Text.Json.Nodes;
using ModelContextProtocol.Server;
using OpenApiMcp.Services;

namespace OpenApiMcp.Resources;

/// <summary>
/// Exposes the four read-only resources defined in §4 of the spec.
/// Resources let the AI host embed contract data directly into context via
/// the standard MCP resource protocol rather than tool calls.
/// </summary>
[McpServerResourceType]
public sealed class ContractResources(ContractStore store)
{
    // ── §4.1  openapi://contracts ─────────────────────────────────────────────

    [McpServerResource(UriTemplate = "openapi://contracts", Name = "openapi-contracts", MimeType = "text/plain"),
     Description("Lists all contracts registered with this server. Returns a newline-delimited list of contract IDs with their titles and OpenAPI versions.")]
    public string ListContracts()
    {
        var sb = new StringBuilder();
        foreach (var c in store.GetAll())
            sb.AppendLine($"{c.Id}\t{c.Title}\t{c.OpenApiVersion}");
        return sb.Length > 0 ? sb.ToString().TrimEnd() : "(no contracts registered)";
    }

    // ── §4.2  openapi://contracts/{contractId}/info ───────────────────────────

    [McpServerResource(UriTemplate = "openapi://contracts/{contractId}/info", Name = "contract-info", MimeType = "application/json"),
     Description("Returns the info, servers, security, and tags sections of the named contract.")]
    public string GetContractInfo(string contractId)
    {
        var doc    = store.Get(contractId).Document;
        var result = new JsonObject();
        foreach (var key in new[] { "info", "servers", "security", "tags" })
        {
            var val = doc[key];
            if (val is not null) result[key] = val.DeepClone();
        }
        return result.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
    }

    // ── §4.3  openapi://contracts/{contractId}/index ─────────────────────────

    [McpServerResource(UriTemplate = "openapi://contracts/{contractId}/index", Name = "contract-index", MimeType = "text/plain"),
     Description("Returns a compact flat index of all path strings with HTTP methods/summaries, and all top-level component names.")]
    public string GetContractIndex(string contractId)
    {
        var doc = store.Get(contractId).Document;
        var sb  = new StringBuilder();

        // Paths
        sb.AppendLine("=== PATHS ===");
        var paths = doc["paths"] as JsonObject;
        if (paths is not null)
        {
            foreach (var (path, pathItem) in paths)
            {
                if (pathItem is not JsonObject pi) continue;
                foreach (var m in new[] { "get","post","put","delete","patch","options","head","trace" })
                {
                    var op = pi[m] as JsonObject;
                    if (op is null) continue;
                    var summary     = op["summary"]?.GetValue<string>() ?? "";
                    var operationId = op["operationId"]?.GetValue<string>() ?? "";
                    var deprecated  = op["deprecated"]?.GetValue<bool>() == true ? " [DEPRECATED]" : "";
                    sb.AppendLine($"  {m.ToUpperInvariant(),-7} {path,-50} {operationId,-30} {summary}{deprecated}");
                }
            }
        }

        // Components
        sb.AppendLine();
        sb.AppendLine("=== COMPONENTS ===");
        var comps = doc["components"] as JsonObject;
        if (comps is not null)
        {
            foreach (var (section, sectionNode) in comps)
            {
                if (sectionNode is not JsonObject secObj) continue;
                var names = string.Join(", ", secObj.Select(k => k.Key));
                sb.AppendLine($"  {section}: {names}");
            }
        }

        return sb.ToString();
    }

    // ── §4.4  openapi://contracts/{contractId}/fragment ───────────────────────

    [McpServerResource(UriTemplate = "openapi://contracts/{contractId}/fragment{?pointer}", Name = "contract-fragment", MimeType = "application/json"),
     Description("Returns the JSON subtree at the given JSON Pointer. Immediate $ref targets are inlined one level deep.")]
    public string GetFragment(string contractId, string? pointer = null)
    {
        var doc       = store.Get(contractId).Document;
        var ptr       = pointer ?? "";
        var node      = ptr == "" ? doc : JsonPointerHelper.Navigate(doc, ptr);

        if (node is null)
            return System.Text.Json.JsonSerializer.Serialize(new { pointer = ptr, exists = false });

        var content   = JsonPointerHelper.ResolveRefsShallow(doc, node, out var unresolved);
        return content.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
    }
}

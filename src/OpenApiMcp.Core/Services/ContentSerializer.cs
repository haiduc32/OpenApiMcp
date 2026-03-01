using System.Text.Json;
using System.Text.Json.Nodes;

namespace OpenApiMcp.Services;

/// <summary>
/// Helpers for round-tripping content between JSON, YAML, and native formats.
/// </summary>
public static class ContentSerializer
{
    public static readonly JsonSerializerOptions IndentedOptions = new() { WriteIndented = true };

    /// <summary>
    /// Parse an arbitrary JSON or YAML string into a JsonNode.
    /// Tries JSON first, falls back to YAML.
    /// </summary>
    public static JsonNode Parse(string content, string? formatHint = null)
    {
        if (formatHint == "json" || (formatHint is null && content.TrimStart().StartsWith('{')))
        {
            try
            {
                return JsonNode.Parse(content) ?? throw new McpException("INVALID_CONTENT", "JSON parsed to null.");
            }
            catch (JsonException ex)
            {
                throw new McpException("INVALID_CONTENT", $"Invalid JSON: {ex.Message}");
            }
        }

        // Try YAML
        try
        {
            var json = ContractStore.YamlToJson(content);
            return JsonNode.Parse(json) ?? throw new McpException("INVALID_CONTENT", "YAML parsed to null.");
        }
        catch (McpException) { throw; }
        catch (Exception ex)
        {
            throw new McpException("INVALID_CONTENT", $"Invalid YAML/JSON: {ex.Message}");
        }
    }

    /// <summary>Serialise a JsonNode to the requested format.</summary>
    public static string Serialise(JsonNode node, string format)
        => format switch
        {
            "json"   => node.ToJsonString(IndentedOptions),
            "yaml"   => ContractStore.JsonToYaml(node.ToJsonString()),
            "native" => node.ToJsonString(IndentedOptions), // default native to JSON for in-memory nodes
            _        => node.ToJsonString(IndentedOptions)
        };

    // ── Diff generation ───────────────────────────────────────────────────────

    /// <summary>
    /// Very lightweight diff: collect changed top-level keys across two objects.
    /// Full diff mode produces a line-by-line comparison of the serialised forms.
    /// </summary>
    public static string Diff(JsonNode original, JsonNode staged, string format)
    {
        if (format == "full")
        {
            var origLines = Serialise(original, "json").Split('\n');
            var stagLines = Serialise(staged, "json").Split('\n');
            return UnifiedDiff(origLines, stagLines);
        }

        // Summary: which top-level keys changed
        var origObj = original as JsonObject;
        var stagObj = staged as JsonObject;
        if (origObj is null || stagObj is null)
            return original.ToJsonString() != staged.ToJsonString()
                ? "Document was replaced."
                : "No changes.";

        var sb = new System.Text.StringBuilder();
        var allKeys = origObj.Select(k => k.Key).Union(stagObj.Select(k => k.Key)).Distinct();
        foreach (var key in allKeys)
        {
            var hasOrig = origObj.TryGetPropertyValue(key, out var ov);
            var hasSta  = stagObj.TryGetPropertyValue(key, out var sv);
            if (!hasOrig) sb.AppendLine($"+ /{key}  (added)");
            else if (!hasSta) sb.AppendLine($"- /{key}  (removed)");
            else if (ov?.ToJsonString() != sv?.ToJsonString()) sb.AppendLine($"~ /{key}  (modified)");
        }
        return sb.Length == 0 ? "No changes." : sb.ToString();
    }

    private static string UnifiedDiff(string[] oldLines, string[] newLines)
    {
        // Minimal Myers-like diff — produces a readable +/- line diff
        var sb = new System.Text.StringBuilder();
        int i = 0, j = 0;
        while (i < oldLines.Length || j < newLines.Length)
        {
            if (i < oldLines.Length && j < newLines.Length && oldLines[i] == newLines[j])
            {
                sb.AppendLine($"  {oldLines[i]}");
                i++; j++;
            }
            else if (j < newLines.Length && (i >= oldLines.Length || oldLines[i] != newLines[j]))
            {
                sb.AppendLine($"+ {newLines[j]}");
                j++;
            }
            else
            {
                sb.AppendLine($"- {oldLines[i]}");
                i++;
            }
        }
        return sb.ToString();
    }
}

using System.Text.Json.Nodes;

namespace OpenApiMcp.Models;

/// <summary>Represents a contract registered with the server.</summary>
public sealed class ContractEntry
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string OpenApiVersion { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public int SizeLines { get; set; }
    public string Format { get; set; } = "yaml"; // "json" or "yaml"

    /// <summary>The parsed document as a JSON node tree (canonical in-memory form).</summary>
    public JsonNode Document { get; set; } = JsonNode.Parse("{}")!;

    /// <summary>Raw file bytes for round-trip fidelity (used for commit).</summary>
    public string RawContent { get; set; } = string.Empty;
}

using System.Text.Json.Nodes;

namespace OpenApiMcp.Models;

public sealed class Session
{
    public string SessionId { get; set; } = Guid.NewGuid().ToString("N");
    public string ContractId { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public DateTimeOffset OpenedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Deep-cloned working copy of the document that accumulates staged edits.</summary>
    public JsonNode StagedDocument { get; set; } = JsonNode.Parse("{}")!;

    /// <summary>The base content hash/snapshot for optimistic concurrency.</summary>
    public string BaseContentHash { get; set; } = string.Empty;

    public List<StagedPatch> Patches { get; set; } = [];
}

public sealed class StagedPatch
{
    public string Pointer { get; set; } = string.Empty;
    public string Operation { get; set; } = string.Empty; // "set" | "patch-merge" | "patch-json" | "delete"
    public string? Content { get; set; }
    public DateTimeOffset AppliedAt { get; set; } = DateTimeOffset.UtcNow;
}

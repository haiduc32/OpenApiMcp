using System.Text.Json.Nodes;
using OpenApiMcp.Models;

namespace OpenApiMcp.Services;

/// <summary>Thread-safe in-memory session manager.</summary>
public sealed class SessionManager
{
    private readonly Dictionary<string, Session> _sessions = new(StringComparer.Ordinal);
    private readonly Lock _lock = new();

    public Session Open(ContractEntry contract, string? description)
    {
        var session = new Session
        {
            ContractId = contract.Id,
            Description = description ?? string.Empty,
            StagedDocument = DeepClone(contract.Document),
            BaseContentHash = ContractStore.HashDocument(contract.Document)
        };

        lock (_lock)
            _sessions[session.SessionId] = session;

        return session;
    }

    public Session Get(string sessionId)
    {
        lock (_lock)
        {
            if (_sessions.TryGetValue(sessionId, out var s)) return s;
        }
        throw new McpException("SESSION_NOT_FOUND", $"Session '{sessionId}' not found or expired.");
    }

    public bool Close(string sessionId)
    {
        lock (_lock)
            return _sessions.Remove(sessionId);
    }

    public IReadOnlyList<Session> List(string? contractId)
    {
        lock (_lock)
        {
            var all = _sessions.Values.AsEnumerable();
            if (contractId is not null)
                all = all.Where(s => s.ContractId.Equals(contractId, StringComparison.OrdinalIgnoreCase));
            return all.ToList();
        }
    }

    /// <summary>Deep-clone a JsonNode tree via serialisation round-trip.</summary>
    public static JsonNode DeepClone(JsonNode node)
        => JsonNode.Parse(node.ToJsonString())!;
}

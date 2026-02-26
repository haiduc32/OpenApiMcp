using System.ComponentModel;
using ModelContextProtocol.Server;
using OpenApiMcp.Services;
using static OpenApiMcp.Tools.ToolHelper;

namespace OpenApiMcp.Tools;

[McpServerToolType]
public sealed class SessionTools(ContractStore store, SessionManager sessions)
{
    // ── open_session ──────────────────────────────────────────────────────────

    [McpServerTool, Description("Open an edit session against a contract. Required before making any changes — all edits are staged in the session until committed or discarded. Returns a session_id needed by all write tools.")]
    public string open_session(
        [Description("The contract ID to edit.")]               string  contract_id,
        [Description("Human-readable intent description.")]     string? description = null) => Run(() =>
    {
        var entry   = store.Get(contract_id);
        var session = sessions.Open(entry, description);
        return Ok(new
        {
            session_id  = session.SessionId,
            contract_id = session.ContractId,
            opened_at   = session.OpenedAt.ToString("O")
        });
    });

    // ── close_session ─────────────────────────────────────────────────────────

    [McpServerTool, Description("Discard a session and all staged changes without writing anything. Use this to cancel an in-progress edit.")]
    public string close_session(
        [Description("The session ID to close.")] string session_id) => Run(() =>
    {
        var discarded = sessions.Close(session_id);
        return Ok(new { session_id, discarded });
    });

    // ── list_sessions ─────────────────────────────────────────────────────────

    [McpServerTool, Description("List all currently open edit sessions, optionally filtered by contract. Use this to check for existing sessions before opening a new one.")]
    public string list_sessions(
        [Description("Filter by contract ID (optional).")] string? contract_id = null) => Run(() =>
    {
        var list = sessions.List(contract_id).Select(s => new
        {
            session_id  = s.SessionId,
            contract_id = s.ContractId,
            description = s.Description,
            opened_at   = s.OpenedAt.ToString("O"),
            patch_count = s.Patches.Count
        });
        return Ok(new { sessions = list });
    });
}

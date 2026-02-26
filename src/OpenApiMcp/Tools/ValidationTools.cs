using System.ComponentModel;
using ModelContextProtocol.Server;
using OpenApiMcp.Services;
using static OpenApiMcp.Tools.ToolHelper;

namespace OpenApiMcp.Tools;

[McpServerToolType]
public sealed class ValidationTools(ContractStore store, SessionManager sessions)
{
    // ── validate_session ──────────────────────────────────────────────────────

    [McpServerTool, Description("Validate the staged contract against the OpenAPI specification. Run this before committing to catch errors early.")]
    public string validate_session(
        [Description("The session ID.")]                                    string  session_id,
        [Description("Enforce additional best-practice rules.")] bool    strict = false) => Run(() =>
    {
        var session = sessions.Get(session_id);
        var result  = OpenApiValidator.Validate(session.StagedDocument, strict);
        return Ok(new
        {
            valid    = result.Valid,
            errors   = result.Errors.Select(e => new { pointer = e.Pointer, message = e.Message, severity = "error" }),
            warnings = result.Warnings.Select(w => new { pointer = w.Pointer, message = w.Message, severity = "warning" })
        });
    });

    // ── diff_session ──────────────────────────────────────────────────────────

    [McpServerTool, Description("Show a human-readable diff of all changes staged in the session compared to the committed contract. Review this before committing to confirm the scope of changes.")]
    public string diff_session(
        [Description("The session ID.")]                                       string  session_id,
        [Description("Output format: summary | full.")]                        string  format  = "summary",
        [Description("Scope diff to a subtree pointer (default: entire doc).")] string? pointer = null) => Run(() =>
    {
        var session  = sessions.Get(session_id);
        var original = store.Get(session.ContractId).Document;

        var origNode  = pointer is not null ? (JsonPointerHelper.Navigate(original, pointer) ?? throw new McpException("INVALID_POINTER", $"Pointer '{pointer}' not found in committed document.", pointer)) : original;
        var stagNode  = pointer is not null ? (JsonPointerHelper.Navigate(session.StagedDocument, pointer) ?? throw new McpException("INVALID_POINTER", $"Pointer '{pointer}' not found in staged document.", pointer)) : session.StagedDocument;

        var diff = ContentSerializer.Diff(origNode, stagNode, format);
        return Ok(new
        {
            session_id,
            patch_count = session.Patches.Count,
            diff
        });
    });

    // ── commit_session ────────────────────────────────────────────────────────

    [McpServerTool, Description("Persist all staged changes to the contract's backing store. Validates the contract against the OpenAPI specification first by default — fails and returns errors if the contract is invalid.")]
    public string commit_session(
        [Description("The session ID.")]                                string  session_id,
        [Description("Commit message for audit log.")]                  string? message                  = null,
        [Description("Proceed even if warnings are present.")]          bool    allow_warnings            = true,
        [Description("Validate before committing.")]                    bool    validate_before_commit    = true) => Run(() =>
    {
        var session = sessions.Get(session_id);

        if (validate_before_commit)
        {
            var validation = OpenApiValidator.Validate(session.StagedDocument);
            if (!validation.Valid)
            {
                return Ok(new
                {
                    session_id,
                    committed  = false,
                    contract_id = session.ContractId,
                    errors     = validation.Errors.Select(e => new { e.Pointer, e.Message }),
                    commit_ref = (string?)null
                });
            }
            if (!allow_warnings && validation.Warnings.Count > 0)
            {
                return Ok(new
                {
                    session_id,
                    committed  = false,
                    contract_id = session.ContractId,
                    errors     = validation.Warnings.Select(w => new { w.Pointer, w.Message }),
                    commit_ref = (string?)null
                });
            }
        }

        // Optimistic concurrency: check if base hash still matches
        var entry      = store.Get(session.ContractId);
        var currentHash = ContractStore.HashDocument(entry.Document);
        if (currentHash != session.BaseContentHash)
            throw new McpException("CONFLICT", "The contract has changed since this session was opened. Please refresh.");

        store.Commit(session.ContractId, session.StagedDocument);
        var commitRef = ContractStore.HashDocument(session.StagedDocument);
        sessions.Close(session_id);

        return Ok(new
        {
            session_id,
            committed   = true,
            contract_id = session.ContractId,
            errors      = Array.Empty<object>(),
            commit_ref  = commitRef
        });
    });

    // ── export_contract ───────────────────────────────────────────────────────

    [McpServerTool, Description("Export the full contract content as JSON or YAML. For large contracts prefer selective fragment tools (get_operation, get_schema, get_fragment) over this; use export_contract only when the complete text is genuinely needed (e.g. to write to a file or send to another tool).")]
    public string export_contract(
        [Description("The contract ID.")]                                           string  contract_id,
        [Description("If provided, export the staged (uncommitted) version.")]      string? session_id = null,
        [Description("Output format: json | yaml.")]                                string  format     = "yaml") => Run(() =>
    {
        var entry = store.Get(contract_id);
        var doc   = session_id is not null
            ? sessions.Get(session_id).StagedDocument
            : entry.Document;

        var content    = ContentSerializer.Serialise(doc, format);
        var lineCount  = content.Split('\n').Length;
        return Ok(new { content, format, line_count = lineCount });
    });
}

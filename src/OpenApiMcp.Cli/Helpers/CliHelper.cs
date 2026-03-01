using System.Text.Json;
using OpenApiMcp.Services;

namespace OpenApiMcp.Cli.Helpers;

internal static class CliHelper
{
    internal static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    // ── Content resolution ────────────────────────────────────────────────────

    /// <summary>
    /// Resolves a content argument:
    ///   "-"        → read all of stdin
    ///   "@path"    → read from the given file
    ///   otherwise  → use the value as-is (inline JSON/YAML)
    /// </summary>
    public static string ReadContent(string value)
    {
        if (value == "-")           return Console.In.ReadToEnd();
        if (value.StartsWith('@'))  return File.ReadAllText(value[1..]);
        return value;
    }

    // ── Output helpers ────────────────────────────────────────────────────────

    /// <summary>Serialise <paramref name="result"/> as indented JSON to stdout.</summary>
    public static void Ok(object result)
        => Console.WriteLine(JsonSerializer.Serialize(result, JsonOpts));

    /// <summary>Write a structured error to stderr; returns exit code 1.</summary>
    public static int Error(string code, string message, string? pointer = null)
    {
        var payload = new { error = new { code, message, pointer } };
        Console.Error.WriteLine(JsonSerializer.Serialize(payload, JsonOpts));
        return 1;
    }

    // ── Store factory ─────────────────────────────────────────────────────────

    /// <summary>
    /// Build a <see cref="ContractStore"/> pre-loaded with contracts.
    /// If <paramref name="contractsDir"/> is provided it is used exclusively;
    /// otherwise <see cref="ContractAutoLoader"/> is used for the standard search.
    /// </summary>
    public static ContractStore CreateStore(string? contractsDir)
    {
        var store = new ContractStore();
        ContractAutoLoader.LoadContracts(store, contractsDir);
        return store;
    }

    // ── Write-command helper ──────────────────────────────────────────────────

    /// <summary>
    /// Runs a write operation atomically:
    ///   open session → apply <paramref name="write"/> → validate → commit or error.
    /// Returns 0 on success, 1 on validation failure or domain error.
    /// </summary>
    public static int RunWrite(
        ContractStore  store,
        string         contractId,
        string         description,
        Action<OpenApiMcp.Models.Session> write)
    {
        OpenApiMcp.Models.Session? session = null;
        var sessions = new SessionManager();
        try
        {
            var entry = store.Get(contractId);
            session   = sessions.Open(entry, description);

            write(session);

            var validation = OpenApiValidator.Validate(session.StagedDocument);
            if (!validation.Valid)
            {
                sessions.Close(session.SessionId);
                Ok(new
                {
                    committed   = false,
                    contract_id = contractId,
                    errors      = validation.Errors.Select(e => new { e.Pointer, e.Message })
                });
                return 1;
            }

            store.Commit(contractId, session.StagedDocument);
            sessions.Close(session.SessionId);
            return 0;
        }
        catch (McpException ex)
        {
            if (session is not null) sessions.Close(session.SessionId);
            return Error(ex.Code, ex.Message, ex.Pointer);
        }
        catch (Exception ex)
        {
            if (session is not null) sessions.Close(session.SessionId);
            return Error("INTERNAL_ERROR", ex.Message);
        }
    }
}

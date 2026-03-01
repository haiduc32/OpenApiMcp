using System.CommandLine;
using OpenApiMcp.Cli.Helpers;
using OpenApiMcp.Services;

namespace OpenApiMcp.Cli.Commands;

/// <summary>
/// Contract management commands: register and validate.
/// </summary>
internal static class ContractCommands
{
    public static void Register(RootCommand root, ContractStore store)
    {
        root.AddCommand(RegisterCmd(store));
        root.AddCommand(ValidateCmd(store));
    }

    // ── register ──────────────────────────────────────────────────────────────

    private static Command RegisterCmd(ContractStore store)
    {
        var cmd        = new Command("register", "Register an OpenAPI contract from a file path or inline content.");
        var fileOpt    = new Option<string?>("--file", "Absolute or relative path to a .json/.yaml file.");
        var contentOpt = new Option<string?>("--content", "Inline OpenAPI content. Use @file or - for stdin.");
        var idOpt      = new Option<string?>("--id", "Explicit contract ID (derived from title if omitted).");
        var fmtOpt     = new Option<string>("--format", () => "yaml", "Content format: yaml | json.");
        cmd.AddOption(fileOpt); cmd.AddOption(contentOpt);
        cmd.AddOption(idOpt);   cmd.AddOption(fmtOpt);

        cmd.SetHandler((file, rawContent, id, fmt) =>
        {
            try
            {
                if (file is not null)
                {
                    var entry = store.Register(Path.GetFullPath(file));
                    CliHelper.Ok(new { id = entry.Id, title = entry.Title, version = entry.Version, source = entry.Source });
                    return;
                }

                if (rawContent is not null)
                {
                    var content = CliHelper.ReadContent(rawContent);
                    var usedId  = id ?? "inline-contract";
                    var entry   = store.RegisterFromContent(usedId, content, fmt);
                    CliHelper.Ok(new { id = entry.Id, title = entry.Title, version = entry.Version });
                    return;
                }

                Environment.ExitCode = CliHelper.Error("INVALID_ARGS", "Provide --file or --content.");
            }
            catch (McpException ex) { Environment.ExitCode = CliHelper.Error(ex.Code, ex.Message, ex.Pointer); }
            catch (Exception ex)    { Environment.ExitCode = CliHelper.Error("INTERNAL_ERROR", ex.Message); }
        }, fileOpt, contentOpt, idOpt, fmtOpt);
        return cmd;
    }

    // ── validate ──────────────────────────────────────────────────────────────

    private static Command ValidateCmd(ContractStore store)
    {
        var cmd       = new Command("validate", "Validate the committed contract document against the OpenAPI spec.");
        var idArg     = new Argument<string>("contract-id", "The contract ID.");
        var strictOpt = new Option<bool>("--strict", "Enforce additional best-practice rules.");
        cmd.AddArgument(idArg); cmd.AddOption(strictOpt);

        cmd.SetHandler((id, strict) =>
        {
            try
            {
                var entry  = store.Get(id);
                var result = OpenApiValidator.Validate(entry.Document, strict);
                CliHelper.Ok(new
                {
                    contract_id = id,
                    valid    = result.Valid,
                    errors   = result.Errors.Select(e => new { e.Pointer, e.Message }),
                    warnings = result.Warnings.Select(w => new { w.Pointer, w.Message })
                });
                if (!result.Valid) Environment.ExitCode = 1;
            }
            catch (McpException ex) { Environment.ExitCode = CliHelper.Error(ex.Code, ex.Message, ex.Pointer); }
            catch (Exception ex)    { Environment.ExitCode = CliHelper.Error("INTERNAL_ERROR", ex.Message); }
        }, idArg, strictOpt);
        return cmd;
    }
}

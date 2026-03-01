using System.CommandLine;
using OpenApiMcp.Cli.Commands;
using OpenApiMcp.Services;

namespace OpenApiMcp.Cli;

/// <summary>
/// Factory for the CLI command tree. Accepts a pre-built <see cref="ContractStore"/>
/// so the command tree can be constructed with an injected store for unit testing.
/// </summary>
public static class CliApp
{
    public static RootCommand Build(ContractStore store)
    {
        var root = new RootCommand("OpenAPI contract CLI — navigate and edit large OpenAPI contracts.");

        // Declare --contracts-dir as a global option so it appears in --help and
        // is silently consumed. The actual store is built in Program.cs before this
        // method is called, so handlers never need to read it.
        var dirOption = new Option<string?>(
            "--contracts-dir",
            "Directory to load contracts from (default: auto-detected 'contracts/' folder).");
        root.AddGlobalOption(dirOption);

        ReadCommands.Register(root, store);
        WriteCommands.Register(root, store);
        ContractCommands.Register(root, store);

        return root;
    }
}

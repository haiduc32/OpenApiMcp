using System.CommandLine;
using OpenApiMcp.Services;

namespace OpenApiMcp.Cli.Tests;

/// <summary>Result of a CLI command invocation.</summary>
public sealed record CliResult(int ExitCode, string Stdout, string Stderr);

/// <summary>
/// Runs CLI commands in-process by building the command tree with an injected store
/// and capturing stdout/stderr via Console redirection.
/// A semaphore ensures Console is only redirected by one test at a time.
/// </summary>
public static class CliRunner
{
    // Console.SetOut/SetError are process-global, so serialize all invocations.
    private static readonly SemaphoreSlim _gate = new(1, 1);

    public static async Task<CliResult> RunAsync(ContractStore store, params string[] args)
    {
        await _gate.WaitAsync();
        try
        {
            var stdout      = new StringWriter();
            var stderr      = new StringWriter();
            var prevOut     = Console.Out;
            var prevErr     = Console.Error;
            var prevExit    = Environment.ExitCode;
            Environment.ExitCode = 0;
            Console.SetOut(stdout);
            Console.SetError(stderr);
            try
            {
                int rc = await CliApp.Build(store).InvokeAsync(args);
                // System.CommandLine returns its own exit code; also honour Environment.ExitCode
                // set by handlers (e.g. validation failures).
                int exitCode = rc != 0 ? rc : Environment.ExitCode;
                return new CliResult(exitCode, stdout.ToString().Trim(), stderr.ToString().Trim());
            }
            finally
            {
                Console.SetOut(prevOut);
                Console.SetError(prevErr);
                Environment.ExitCode = prevExit;
            }
        }
        finally
        {
            _gate.Release();
        }
    }
}

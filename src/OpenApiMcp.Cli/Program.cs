using System.CommandLine;
using OpenApiMcp.Cli;
using OpenApiMcp.Cli.Helpers;

// Pre-resolve --contracts-dir before the command tree is built so the store
// can be injected rather than created lazily inside each command handler.
var idx = Array.IndexOf(args, "--contracts-dir");
var contractsDir = idx >= 0 && idx < args.Length - 1
    ? args[idx + 1]
    : args.Select(a => a.StartsWith("--contracts-dir=") ? a["--contracts-dir=".Length..] : null)
          .FirstOrDefault(v => v is not null);

var store = CliHelper.CreateStore(contractsDir);
return await CliApp.Build(store).InvokeAsync(args);

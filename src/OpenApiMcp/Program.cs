using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenApiMcp.Resources;
using OpenApiMcp.Services;
using OpenApiMcp.Tools;

// ── Build host ────────────────────────────────────────────────────────────────

var builder = Host.CreateApplicationBuilder(args);

// ── Logging configuration ──────────────────────────────────────────────────────
// Disable all logging providers (including console) since we use stdio for MCP
// and any console output would break the protocol communication
builder.Logging.ClearProviders();

// ── Core services ─────────────────────────────────────────────────────────────

builder.Services.AddSingleton<ContractStore>();
builder.Services.AddSingleton<SessionManager>();

// ── MCP server (stdio — local only) ──────────────────────────────────────────

builder.Services
    .AddMcpServer(options =>
    {
        options.ServerInfo = new ModelContextProtocol.Protocol.Implementation
        {
            Name    = "openapi-contracts",
            Version = "1.0.0"
        };
    })
    .WithStdioServerTransport()
    // Tools
    .WithTools<ContractManagementTools>()
    .WithTools<FragmentTools>()
    .WithTools<SessionTools>()
    .WithTools<WriteTools>()
    .WithTools<ValidationTools>()
    // Resources (§4)
    .WithResources<ContractResources>();

// ── Build and start ───────────────────────────────────────────────────────────

var app = builder.Build();

// Auto-load contracts from "contracts" directories in several common locations:
//   1. Next to the binary (e.g. after publish)
//   2. Relative to the current working directory (e.g. dotnet run from project dir)
//   3. Up to 3 parent directories (allows running from repo root or sub-dirs)
var store = app.Services.GetRequiredService<ContractStore>();
var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

void TryLoad(string dir)
{
    var full = Path.GetFullPath(dir);
    if (visited.Add(full))
        store.LoadDirectory(full);
}

TryLoad(Path.Combine(AppContext.BaseDirectory, "contracts"));
TryLoad(Path.Combine(Directory.GetCurrentDirectory(), "contracts"));

// Walk up parent directories to find a "contracts" folder
var search = Directory.GetCurrentDirectory();
for (int i = 0; i < 3; i++)
{
    var parent = Path.GetDirectoryName(search);
    if (parent is null) break;
    search = parent;
    TryLoad(Path.Combine(search, "contracts"));
}

await app.RunAsync();

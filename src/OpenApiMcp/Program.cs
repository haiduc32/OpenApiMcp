using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenApiMcp.Resources;
using OpenApiMcp.Services;
using OpenApiMcp.Tools;

// ── Build host ────────────────────────────────────────────────────────────────

var builder = Host.CreateApplicationBuilder(args);

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

ContractAutoLoader.LoadContracts(app.Services.GetRequiredService<ContractStore>());

await app.RunAsync();

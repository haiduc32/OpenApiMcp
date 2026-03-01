````instructions
# Copilot Instructions — OpenApiMcp

## Overview
Two complementary tools for working with large OpenAPI contracts, both built on a shared core library:

- **`src/OpenApiMcp`** — MCP (Model Context Protocol) server; stdio transport, local only. Fine-grained individual tools the model chains together.
- **`src/OpenApiMcp.Cli`** — CLI (`openapi` binary); compound commands that encapsulate full workflows (open-session → write → validate → commit internally). Described by `.github/skills/openapi/SKILL.md`.
- **`src/OpenApiMcp.Core`** — Shared class library; all domain logic with zero MCP/CLI dependency.

## Solution Layout
```
OpenApiMcp.slnx
src/
  OpenApiMcp.Core/         → class library  — Models, Services, ToolHelper
    Models/
      ContractEntry        → Immutable parsed contract (Document: JsonNode)
      Session              → Mutable working copy (StagedDocument) + patch log
    Services/
      ContractStore        → In-memory registry; parses YAML/JSON → JsonNode tree
      SessionManager       → Thread-safe staged-edit sessions (optimistic concurrency)
      JsonPointerHelper    → RFC 6901 navigate/set/delete; RFC 7396 + 6902 patch; $ref resolution
      ContentSerializer    → JSON ↔ YAML round-trip + text diff
      OpenApiValidator     → Validation via Microsoft.OpenApi.Readers
      ContractAutoLoader   → Scans contracts/ directories on startup (shared by MCP + CLI)
      McpException         → Domain exception → error envelope { code, message, pointer }
    Helpers/
      ToolHelper           → Run(), Ok(), OkNode() — public; used by MCP tool classes
  OpenApiMcp/              → exe — MCP server
    Tools/                 → Five MCP tool classes (see below)
    Resources/
      ContractResources    → Four read-only MCP resources at openapi:// URIs
    Program.cs             → DI host + MCP server wiring
  OpenApiMcp.Cli/          → exe — CLI (assembly name: openapi)
    Commands/
      ReadCommands         → contracts, info, index, get, operation, schema, search, tag-operations, export
      WriteCommands        → set, patch, delete, rename-schema, add-path, add-operation, add-schema
      ContractCommands     → register, validate
    Helpers/CliHelper      → ReadContent(), Ok(), Error(), CreateStore(), RunWrite()
    Program.cs             → System.CommandLine root command + --contracts-dir global option
tests/
  OpenApiMcp.IntegrationTests/   → xUnit tests against MCP tool classes (Core types via transitive ref)
  OpenApiMcp.Cli.Tests/          → xUnit tests for CLI commands via in-process CliApp.Build() injection
contracts/                       → Auto-loaded .yaml/.json sample contracts (OAS 3.0.3)
.github/skills/openapi/SKILL.md  → GitHub Copilot agent skill descriptor for the CLI
```

## Key Patterns

### Core: `JsonNode` is the canonical in-memory form
Contracts are always parsed YAML → JSON → `JsonNode`. Never re-parse a `Document` — use `JsonPointerHelper` for all navigation, mutation, and `$ref` resolution.

### Core: `McpException` for all domain errors
Throw `new McpException(code, message, pointer?)` for any domain violation. Both the MCP `ToolHelper.Run()` wrapper and the CLI `CliHelper.RunWrite()` wrapper catch this and emit a structured error envelope:
```json
{ "error": { "code": "CONTRACT_NOT_FOUND", "message": "...", "pointer": "..." } }
```

### MCP: tool return type is always `string` (JSON)
Every tool method returns a serialised JSON string via `ToolHelper.Run()`. Errors are returned as the envelope above, **never thrown** to the protocol layer. Use `ToolHelper.Ok(object)` for success.

### CLI: write commands own the session lifecycle
Every write command in `WriteCommands.cs` calls `CliHelper.RunWrite()`, which:
1. Opens a session
2. Invokes the caller's write lambda
3. Validates the staged document
4. Commits on success, or exits with code 1 on validation failure

### Contract auto-load on startup
`ContractAutoLoader.LoadContracts(store, explicitDir?)` scans:
1. Next to the binary (`AppContext.BaseDirectory/contracts`)
2. Current working directory (`contracts/`)
3. Up to 3 parent directories

Place `.yaml`/`.json` OpenAPI files (OAS 3.0.x) in `contracts/` at the repo root.

## Build & Test Commands
```bash
dotnet build OpenApiMcp.slnx                           # build all projects
dotnet test OpenApiMcp.slnx                            # run all 75 integration tests
dotnet run --project src/OpenApiMcp                   # start MCP server (stdio)
dotnet run --project src/OpenApiMcp.Cli -- --help     # CLI help
dotnet run --project src/OpenApiMcp.Cli -- contracts  # list loaded contracts
```

## MCP Tool Classes
Located in `src/OpenApiMcp/Tools/`. Each decorated `[McpServerToolType]`, methods with `[McpServerTool, Description(...)]`:
- `ContractManagementTools` — register, unregister, list, get_contract_info, get_contract_index
- `FragmentTools` — get_fragment, get_operation, get_schema, search_contract, get_tag_operations
- `SessionTools` — open_session, list_sessions, close_session
- `WriteTools` — add_operation, add_path, add_schema, set_fragment, patch_fragment, delete_fragment, rename_schema
- `ValidationTools` — validate_session, diff_session, commit_session, export_contract

## CLI Tests (`tests/OpenApiMcp.Cli.Tests/`)
- `CliApp.Build(ContractStore)` — public factory; injects a pre-populated store so commands never touch the filesystem
- `CliFixture` — `IClassFixture` with inline OAS 3.0.3 YAML and a `ContractStore` pre-loaded; shared by read tests
- `CliRunner.RunAsync(store, args)` — redirects `Console.Out/Error`, invokes `CliApp.Build(store).InvokeAsync(args)`, returns `CliResult(ExitCode, Stdout, Stderr)`; a `SemaphoreSlim` serialises invocations since `Console.SetOut` is process-global
- `[Collection("cli")]` — applied to all CLI test classes to prevent parallel Console redirection
- Write tests create a **fresh store per test** (`FreshStore()`) to prevent state leakage between write operations
- `ReadCommandTests` uses `IClassFixture<CliFixture>` (read-only store; safe to share)

## Integration Tests (MCP)
- Framework: **xunit** + **FluentAssertions** in `tests/OpenApiMcp.IntegrationTests/`
- Tests reference `src/OpenApiMcp` (tool classes); Core types are available transitively
- Shared state via `IClassFixture<PetstoreFixture>` — inline OAS 3.0.3 YAML, no file-path dependency
- `Json.cs` provides extension helpers (`json.Prop(...)`, `json.Str(...)`, `json.HasError()`)
- Test a new tool by creating `SomethingToolTests.cs` following `ContractManagementTests.cs`

## Adding a New MCP Tool
1. Add a method to the appropriate `src/OpenApiMcp/Tools/*.cs` class (or create a new `[McpServerToolType]` class)
2. Decorate with `[McpServerTool, Description("...")]` and parameter `[Description("...")]` attributes
3. Implement with `return Run(() => { ... Ok(...) ... })` — never return or throw outside `Run()`
4. Register the new class in `src/OpenApiMcp/Program.cs` with `.WithTools<NewTools>()`
5. Add integration tests in `tests/OpenApiMcp.IntegrationTests/`

## Adding a New CLI Command
1. Add a private static `Command` factory method to the appropriate `src/OpenApiMcp.Cli/Commands/*.cs` class
2. Call `root.AddCommand(...)` from the `Register(...)` method in that class
3. For write commands use `CliHelper.RunWrite(store, id, description, session => { ... })` — never manage sessions manually
4. Add tests in `tests/OpenApiMcp.Cli.Tests/` using `CliRunner.RunAsync(store, args)` (fresh store for writes, shared fixture for reads)

## Spec Reference
`openapi-mcp-spec.md` is the authoritative design document for the MCP tools. Section numbers are cited in code comments (e.g. `// §4.3`, `// §8.3 optional`). Cross-reference when adding features or resolving ambiguity about intended behaviour.
````
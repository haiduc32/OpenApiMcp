# Copilot Instructions — OpenApiMcp

## Overview
An MCP (Model Context Protocol) server that exposes structured, selective access to OpenAPI contracts. Uses `stdio` transport, making it runnable as a local tool. The core motivation: enterprise OpenAPI files exceed LLM context windows, so the server exposes fragment-level navigation instead of raw file access.

## Architecture
```
Program.cs              → DI host wiring, auto-loads contracts/ on startup
Services/
  ContractStore         → In-memory registry; parses YAML/JSON → JsonNode tree
  SessionManager        → Thread-safe staged-edit sessions (optimistic concurrency)
Models/
  ContractEntry         → Immutable parsed contract (Document: JsonNode)
  Session               → Mutable working copy (StagedDocument) + patch log
Tools/                  → Five MCP tool classes (see below)
Resources/
  ContractResources     → Four read-only MCP resources at openapi:// URIs
```

**Tool classes** (each decorated `[McpServerToolType]`, methods with `[McpServerTool, Description(...)]`):
- `ContractManagementTools` — register, list, get_contract_info, get_contract_index
- `FragmentTools` — get_fragment, get_operation, get_schema, search_contract, get_tag_operations
- `SessionTools` — open_session, list_sessions, close_session
- `WriteTools` — add_operation, add_path, add_schema, set_fragment, patch_fragment, delete_fragment, rename_schema
- `ValidationTools` — validate_session, diff_session, commit_session, export_contract

## Key Patterns

### Tool return type is always `string` (JSON)
Every tool method returns a serialised JSON string via `ToolHelper.Run()`. Errors are **never thrown to the protocol layer** — they are caught and returned as a structured error envelope:
```csharp
{ "error": { "code": "CONTRACT_NOT_FOUND", "message": "...", "pointer": "..." } }
```
Use `ToolHelper.Ok(object)` for success and throw `McpException(code, message, pointer?)` for domain errors.

### Canonical in-memory form is `JsonNode`
Contracts are always parsed YAML → JSON → `JsonNode`. The `Document` on `ContractEntry` and `StagedDocument` on `Session` are always `JsonNode` trees. Use `JsonPointerHelper` (in `Services/`) for all navigation, mutation, and `$ref` resolution — do not parse the document again.

### Session-based editing
Write tools require an open session. `SessionManager.Open()` deep-clones `contract.Document` into `session.StagedDocument`. All edits mutate `StagedDocument`; the committed `Document` is untouched until `commit_session`. Every write also appends a `StagedPatch` for the audit log.

### Contract auto-load on startup (`Program.cs`)
Contracts are loaded from a `contracts/` directory. The server searches:
1. Next to the binary (`AppContext.BaseDirectory/contracts`)
2. Current working directory (`contracts/`)
3. Up to 3 parent directories

Place `.yaml`/`.json` OpenAPI files in `contracts/` at the repo root; they are picked up automatically on `dotnet run`.

## Build & Test Commands
```bash
dotnet build                         # build everything
dotnet test                          # run all integration tests
dotnet run --project src/OpenApiMcp  # start the MCP server (stdio)
```

## Integration Tests
- Framework: **xunit** + **FluentAssertions**
- Tests instantiate tool classes directly (no HTTP, no MCP wire protocol)
- Shared state via `IClassFixture<PetstoreFixture>` — inline YAML keeps tests file-path independent
- `Json.cs` provides thin extension helpers (`json.Prop(...)`, `json.Str(...)`, `json.HasError()`) for asserting on tool return values
- Test a new tool by creating a `SomethingToolTests.cs` following the pattern in `ContractManagementTests.cs`

## Adding a New Tool
1. Add a method to the appropriate `Tools/*.cs` class (or create a new `[McpServerToolType]` class)
2. Decorate with `[McpServerTool, Description("...")]` and parameter `[Description("...")]` attributes
3. Implement with `return Run(() => { ... Ok(...) ... })` — never return or throw outside `Run()`
4. Register the new class in `Program.cs` with `.WithTools<NewTools>()`
5. Add integration tests in `tests/OpenApiMcp.IntegrationTests/`

## Spec Reference
`openapi-mcp-spec.md` is the authoritative design document. Section numbers are cited in code comments (e.g. `// §4.3`, `// §8.3 optional`). Cross-reference it when adding features or resolving ambiguity about intended behaviour.

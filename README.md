# OpenApiMcp

Structured, selective access to large OpenAPI contracts — for AI assistants, API tooling, and automated governance workflows.

Enterprise OpenAPI files routinely exceed LLM context windows. This project solves that with two complementary tools built on a shared core library:

| Tool | What it is | Best for |
|------|-----------|----------|
| **`src/OpenApiMcp`** | MCP server (stdio) | Copilot / agent workflows via Model Context Protocol |
| **`src/OpenApiMcp.Cli`** | CLI (`openapi` binary) | Shell scripts, CI, and Copilot skill invocations |

---

## Solution Layout

```
OpenApiMcp.slnx
src/
  OpenApiMcp.Core/          → Shared class library (Models, Services, no MCP dependency)
  OpenApiMcp/               → MCP server exe
  OpenApiMcp.Cli/           → CLI exe  (assembly name: openapi)
tests/
  OpenApiMcp.IntegrationTests/  → xUnit — MCP tool classes
  OpenApiMcp.Cli.Tests/         → xUnit — CLI commands (in-process, injected store)
contracts/                  → Auto-loaded sample contracts (.yaml / .json, OAS 3.0.x)
.github/skills/openapi/     → GitHub Copilot agent skill (SKILL.md)
openapi-mcp-spec.md         → Formal specification (authoritative)
```

---

## Quick Start

### Build

```bash
dotnet build
```

### Run tests

```bash
dotnet test
```

100 tests: 75 MCP integration + 25 CLI in-process.

### Start the MCP server

```bash
dotnet run --project src/OpenApiMcp
```

Starts in **stdio mode**, ready for any MCP client.

### Use the CLI

```bash
dotnet run --project src/OpenApiMcp.Cli -- --help

# List loaded contracts
dotnet run --project src/OpenApiMcp.Cli -- contracts

# Get a schema
dotnet run --project src/OpenApiMcp.Cli -- schema petstore-sample-api Pet

# Add a schema
dotnet run --project src/OpenApiMcp.Cli -- add-schema petstore-sample-api Widget \
  '{"type":"object","properties":{"id":{"type":"integer"}}}'
```

> See [.github/skills/openapi/SKILL.md](.github/skills/openapi/SKILL.md) for the full CLI command reference.

---

## Contracts

Place OpenAPI files (`.yaml` or `.json`, OAS 3.0.x) in the `contracts/` directory. They are auto-loaded on startup.

The auto-loader searches:
1. `<binary-dir>/contracts/`
2. Current working directory `contracts/`
3. Up to 3 parent directories

---

## MCP Server

The MCP server exposes **fine-grained tools** so an AI assistant can efficiently navigate and edit any contract without loading the full file.

### MCP Tools

| Class | Tools |
|-------|-------|
| `ContractManagementTools` | `register`, `unregister`, `list_contracts`, `get_contract_info`, `get_contract_index` |
| `FragmentTools` | `get_fragment`, `get_operation`, `get_schema`, `search_contract`, `get_tag_operations` |
| `SessionTools` | `open_session`, `list_sessions`, `close_session` |
| `WriteTools` | `set_fragment`, `patch_fragment`, `delete_fragment`, `add_path`, `add_operation`, `add_schema`, `rename_schema` |
| `ValidationTools` | `validate_session`, `diff_session`, `commit_session`, `export_contract` |

### MCP Resources

| URI | Description |
|-----|-------------|
| `openapi://contracts` | List all contracts |
| `openapi://contracts/{id}/info` | Info, servers, security, tags |
| `openapi://contracts/{id}/index` | Compact path + schema index |
| `openapi://contracts/{id}/fragment?pointer={p}` | Any subtree by JSON Pointer |

### VS Code / Claude Desktop config

```json
{
  "mcpServers": {
    "openapi-mcp": {
      "command": "dotnet",
      "args": ["run", "--project", "src/OpenApiMcp"]
    }
  }
}
```

---

## CLI

The CLI provides **compound commands** — each write command internally manages the full session lifecycle (open → write → validate → commit) so callers never deal with sessions.

### Read commands

```
contracts          List all loaded contracts
info <id>          Info, servers, security, tags
index <id>         All paths and schema names  [--tags to group by tag]
get <id> <ptr>     Fragment by JSON Pointer    [--no-resolve] [--format yaml|json]
operation <id> <path> <method>
schema <id> <name>                             [--depth 1-3]
search <id> <query>                            [--scope all|paths|schemas] [--max N]
tag-operations <id> <tag>
export <id>                                    [--format yaml|json]
```

### Write commands

```
set <id> <pointer> <content>
patch <id> <pointer> <patch>                   [--type merge|json-patch]
delete <id> <pointer>
rename-schema <id> <old> <new>
add-path <id> <path> <path-item>
add-operation <id> <path> <method> <operation>
add-schema <id> <name> <schema>
```

Content arguments accept inline JSON/YAML, `@file`, or `-` (stdin).

### Contract commands

```
register --file <path>
register --content <yaml> --id <id>
validate <id>                                  [--strict]
```

---

## Architecture

### Shared Core (`OpenApiMcp.Core`)

All domain logic lives here with no dependency on MCP or CLI:

- `ContractStore` — in-memory registry; parses YAML/JSON → `JsonNode`
- `SessionManager` — thread-safe staged-edit sessions (optimistic concurrency)
- `JsonPointerHelper` — RFC 6901 navigation/mutation, RFC 7396 merge patch, RFC 6902 JSON patch, `$ref` resolution
- `ContentSerializer` — JSON ↔ YAML round-trip, text diff
- `OpenApiValidator` — OpenAPI 3.0.x spec validation via `Microsoft.OpenApi.Readers`
- `ContractAutoLoader` — shared startup loader (used by both `Program.cs` files)

### Error envelope

All tools and CLI commands emit a consistent error structure — never throw to the caller:

```json
{
  "error": {
    "code": "CONTRACT_NOT_FOUND",
    "message": "No contract with ID 'foo' exists.",
    "pointer": null
  }
}
```

---

## Development

### Dependencies

| Package | Version | Used by |
|---------|---------|---------|
| `ModelContextProtocol` | 1.0.0 | MCP server |
| `Microsoft.Extensions.Hosting` | 10.0.3 | MCP server |
| `System.CommandLine` | 2.0.0-beta4 | CLI |
| `YamlDotNet` | 16.3.0 | Core |
| `Microsoft.OpenApi.Readers` | 1.6.28 | Core |

### Adding a new MCP tool

1. Add a method to `src/OpenApiMcp/Tools/*.cs` decorated `[McpServerTool, Description("...")]`
2. Implement with `return Run(() => { ... Ok(result) ... })`
3. Register new tool type in `src/OpenApiMcp/Program.cs` with `.WithTools<T>()`
4. Add tests in `tests/OpenApiMcp.IntegrationTests/`

### Adding a new CLI command

1. Add a factory method to `src/OpenApiMcp.Cli/Commands/*.cs`
2. Call `root.AddCommand(...)` from `Register(root, store)`
3. For writes: use `CliHelper.RunWrite(store, id, description, session => { ... })`
4. Add tests in `tests/OpenApiMcp.Cli.Tests/` (fresh store for writes, shared `CliFixture` for reads)

### Specification

[openapi-mcp-spec.md](openapi-mcp-spec.md) is the authoritative design document. Section numbers are cited in code comments (e.g. `// §4.3`).

---

## License

[Add your license here]

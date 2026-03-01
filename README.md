# OpenAPI Contracts MCP Server

An **MCP (Model Context Protocol) server** that exposes structured, selective access to large OpenAPI contracts. Instead of loading entire API specifications into an LLM context window, this server provides fragment-level navigation and editing capabilities through a well-defined set of tools and resources.

**Status:** Version 1.0.0 (Draft)

## Overview

Enterprise OpenAPI contracts frequently exceed the context window limits of language models. This MCP server solves that problem by:

- **Selective Access**: Navigate and query OpenAPI contracts through semantic operations (paths, schemas, operations) rather than raw file content
- **Fragment-Based Navigation**: Use JSON Pointers (RFC 6901) to address specific subtrees within contracts
- **Session-Based Editing**: Make incremental edits to contracts with staged changes until committed
- **Abstract Storage**: Contracts load from local YAML/JSON files with extensible backend support
- **Efficient Indexing**: Get compact summaries of vast contracts without loading the full document

Perfect for AI assistants, API tooling, contract validation, and automated API governance workflows.

## Architecture

```
Program.cs              → Dependency injection & auto-loads contracts from contracts/ directory
Services/
  ├─ ContractStore      → In-memory registry; parses YAML/JSON → JsonNode tree
  ├─ SessionManager     → Thread-safe staged-edit sessions with optimistic concurrency
  ├─ JsonPointerHelper  → RFC 6901 navigation and $ref resolution
  ├─ OpenApiValidator   → OpenAPI spec validation
  └─ ContentSerializer  → YAML/JSON serialization
Models/
  ├─ ContractEntry      → Immutable parsed contract with JsonNode document
  └─ Session            → Mutable working copy with patch audit log
Tools/ (Five MCP tool classes)
  ├─ ContractManagementTools  → register, list, get_contract_info, get_contract_index
  ├─ FragmentTools             → get_fragment, get_operation, get_schema, search_contract, get_tag_operations
  ├─ SessionTools              → open_session, list_sessions, close_session
  ├─ WriteTools                → add_operation, add_path, add_schema, set_fragment, patch_fragment, delete_fragment, rename_schema
  └─ ValidationTools           → validate_session, diff_session, commit_session, export_contract
Resources/
  └─ ContractResources    → Four read-only MCP resources at openapi:// URIs
```

## Features

### Core Capabilities

- **Contract Management**: Register, list, and inspect OpenAPI contracts
- **Semantic Fragment Queries**: Retrieve operations, schemas, paths, and tags directly
- **Session-Based Editing**: Stage changes, validate, and commit with full audit trail
- **JSON Pointer Navigation**: RFC 6901 compliant addressing for precise contract navigation
- **Reference Resolution**: Automatic `$ref` inlining for readable responses
- **Validation**: OpenAPI specification compliance checking before commit
- **Search**: Full-text and regex search across paths, schemas, and operations

### MCP Resources

- **`openapi://contracts`** – List all registered contracts
- **`openapi://contracts/{id}/info`** – Contract metadata (info, servers, security, tags)
- **`openapi://contracts/{id}/index`** – Compact navigable index (paths, schemas)
- **`openapi://contracts/{id}/fragment?pointer={pointer}`** – Any subtree by JSON Pointer

## Requirements

- **.NET 10.0** or **.NET 9.0**
- **dotnet CLI** (included with .NET SDK)

## Quick Start

### Build

```bash
dotnet build
```

### Run the MCP Server

```bash
dotnet run --project src/OpenApiMcp
```

The server starts in **stdio mode** (standard input/output transport), ready to communicate with MCP clients.

### Run Tests

```bash
dotnet test
```

Runs all integration tests using **xunit** and **FluentAssertions**.

## Packaging and Distribution

### Package as a .NET Tool

The project is configured to publish as a [.NET global tool](https://learn.microsoft.com/en-us/dotnet/core/tools/global-tools). Package it locally or publish to NuGet.

**Pack for local testing or publishing:**

```bash
dotnet pack src/OpenApiMcp/OpenApiMcp.csproj -c Release -o ./nupkgs
```

This produces `./nupkgs/OpenApiMcp.1.0.0.nupkg`.

### Install from Local File (Testing)

Test the tool locally before publishing:

```bash
# Install from local nupkg (global)
dotnet tool install --global --add-source ./nupkgs OpenApiMcp --version 1.0.0

# Or install as a repo-local tool (requires .config/dotnet-tools.json manifest)
dotnet new tool-manifest    # run once in repo root
dotnet tool install --local --add-source ./nupkgs OpenApiMcp --version 1.0.0

# Run the tool
openapimcp
```

**Update after repacking:**

```bash
dotnet tool update --global OpenApiMcp --add-source ./nupkgs --version 1.0.1
```

**Uninstall:**

```bash
dotnet tool uninstall --global OpenApiMcp
```

### Publish to NuGet.org

**Prerequisites:**
- NuGet.org account
- API key from https://www.nuget.org/account/ApiKeys

**Push to NuGet.org:**

```bash
dotnet nuget push ./nupkgs/OpenApiMcp.1.0.0.nupkg \
  -k <YOUR_NUGET_API_KEY> \
  -s https://api.nuget.org/v3/index.json
```

### Install Globally from NuGet.org

Once published, users can install globally:

```bash
dotnet tool install --global OpenApiMcp --version 1.0.0

# Run the tool
openapimcp
```

Or update to a new version:

```bash
dotnet tool update --global OpenApiMcp
```

**Note:** This tool is **framework-dependent** and requires .NET 10.0 or .NET 9.0 to be installed.

## Usage

### Adding Contracts

Place OpenAPI files (`.yaml` or `.json`) in the `contracts/` directory at the repository root. The server auto-discovers them on startup.

Example:
```bash
# Copy your OpenAPI spec to the contracts directory
cp my-api.yaml contracts/

# Start the server
dotnet run --project src/OpenApiMcp
```

The server searches for contracts in:
1. Next to the binary (`bin/Debug/net10.0/contracts/`)
2. The current working directory (`contracts/`)
3. Up to 3 parent directories

### Reading Contracts

**Get a contract's info:**
```json
{
  "name": "get_contract_info",
  "arguments": {
    "contract_id": "petstore"
  }
}
```

**Get an operation:**
```json
{
  "name": "get_operation",
  "arguments": {
    "contract_id": "petstore",
    "path": "/pets",
    "method": "get"
  }
}
```

**Get a schema:**
```json
{
  "name": "get_schema",
  "arguments": {
    "contract_id": "petstore",
    "name": "Pet"
  }
}
```

**Get a fragment by pointer:**
```json
{
  "name": "get_fragment",
  "arguments": {
    "contract_id": "petstore",
    "pointer": "/paths/~1pets/get"
  }
}
```

### Editing Contracts

**Open an edit session:**
```json
{
  "name": "open_session",
  "arguments": {
    "contract_id": "petstore",
    "description": "Add new /users endpoint"
  }
}
```

**Make changes, then validate:**
```json
{
  "name": "validate_session",
  "arguments": {
    "session_id": "session-123"
  }
}
```

**Commit changes:**
```json
{
  "name": "commit_session",
  "arguments": {
    "session_id": "session-123",
    "message": "Add new /users endpoint"
  }
}
```

## Development

### Project Structure

```
OpenApiMcp/
├── src/
│   └── OpenApiMcp/
│       ├── Program.cs                 # Entry point & DI setup
│       ├── Models/
│       ├── Services/
│       ├── Tools/
│       └── Resources/
├── tests/
│   └── OpenApiMcp.IntegrationTests/   # xunit test suite
├── contracts/                         # Sample OpenAPI files
├── openapi-mcp-spec.md               # Formal specification
└── README.md
```

### Adding a New Tool

1. Create or modify a tool class in `src/OpenApiMcp/Tools/` decorated with `[McpServerToolType]`
2. Add tool methods with `[McpServerTool, Description("...")]` attributes
3. Implement using `return Run(() => { ... Ok(result) ... })`
4. Register in `Program.cs` with `.WithTools<YourToolClass>()`
5. Add integration tests in `tests/OpenApiMcp.IntegrationTests/`

### Testing

Tests use:
- **xunit** for test framework
- **FluentAssertions** for readable assertions
- **Fixture pattern** for shared test data (see `PetstoreFixture.cs`)

Test files follow the naming convention: `*Tests.cs`

Example test structure:
```csharp
public class MyToolTests : IClassFixture<PetstoreFixture>
{
    private readonly MyTools _tools;
    private readonly ContractStore _store;

    public MyToolTests(PetstoreFixture fixture)
    {
        _store = fixture.Store;
        _tools = new MyTools(_store);
    }

    [Fact]
    public void MyTest_GivenInput_ReturnsExpected()
    {
        var result = _tools.MyMethod(...);
        result.Should().NotBeNull();
    }
}
```

## Dependencies

| Package | Version | Purpose |
|---------|---------|---------|
| `ModelContextProtocol` | 1.0.0 | MCP protocol implementation |
| `YamlDotNet` | 16.3.0 | YAML parsing & serialization |
| `Microsoft.OpenApi.Readers` | 1.6.28 | OpenAPI validation & parsing |
| `JsonPointer.Net` | 7.0.0 | RFC 6901 pointer addressing |
| `Microsoft.Extensions.Hosting` | 10.0.3 | Dependency injection & hosting |

## Specification

See [openapi-mcp-spec.md](openapi-mcp-spec.md) for the complete formal specification including:
- Tool definitions and signatures
- Resource descriptions
- Response envelopes
- Error handling
- Validation rules

## Key Design Patterns

### Error Handling

All tools return structured JSON responses. Errors are never thrown to the protocol layer:

```json
{
  "error": {
    "code": "CONTRACT_NOT_FOUND",
    "message": "Contract 'foo' not found",
    "pointer": null
  }
}
```

### In-Memory Form

Contracts are always represented internally as `JsonNode` trees. Navigation and mutation use `JsonPointerHelper` exclusively — never re-parse the document.

### Session-Based Editing

Write operations require an open session. Sessions deep-clone the contract document into a `StagedDocument`. All mutations happen in the session; the original committed document is untouched until `commit_session`.

## Contributing

Contributions are welcome! Please:

1. Follow the existing code structure and patterns
2. Add tests for new features
3. Update documentation
4. Ensure all tests pass: `dotnet test`
5. Build successfully: `dotnet build`

## License

[Add your license here — e.g., MIT, Apache 2.0, etc.]

## Support

For issues, questions, or discussions:
- Check [openapi-mcp-spec.md](openapi-mcp-spec.md) for specification details
- Review test cases in `tests/OpenApiMcp.IntegrationTests/` for usage examples
- Open an issue on GitHub

---

**Version:** 1.0.0 (Draft)  
**Last Updated:** February 2026

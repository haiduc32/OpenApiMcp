# OpenAPI Contracts MCP Server

A **Model Context Protocol (MCP) server** that provides intelligent access to large OpenAPI contracts for language models and AI tools.

## What is this?

This server exposes OpenAPI contracts (Swagger specifications) through the Model Context Protocol, enabling AI assistants and tools to:

- **Navigate large contracts efficiently** without loading entire files into context
- **Query specific operations, schemas, and paths** by name
- **Edit contracts safely** with staged changes and validation
- **Search contracts** for specific operations or schemas

Perfect for API documentation tooling, contract validation, API governance automation, and LLM-powered API exploration.

## Installation

### Installing the tool

Install as a .NET tool:

```bash
dotnet tool install -g OpenApiMcpServer
```

### Setting up the MCP

Setup the MCP server. This might be slightlly different depending on the IDE/tool you're using, but tipical setup would look like:

```json
{
  "servers": {
    "openapi-contracts": {
      "type": "stdio",
      "command": "openapimcpserver"
    }
  }
}
```

Alternatively, if `openapimcpserver` fails to register globally, you can try setting it up to run with `dotnet` command:

```json
{
  "servers": {
    "openapi-contracts": {
      "type": "stdio",
      "command": "dotnet",
      "args": [
        "run",
        "tool",
        "openapimcpserver"
      ]
    }
  }
}
```

### Setting up the Skill

Adding a dedicated skill for working with the OpenAPI MCP Server is not required, but it helps the AI understand how to work correctly with the available tools. 

Depending on your IDE/tool you're using the steps to adding the skill will be different. The skill can be found here: [openapi skill](https://github.com/haiduc32/OpenApiMcp/tree/main/.github/skills/openapi)

## Updating

Update to the latest version:

```bash
dotnet tool update -g OpenApiMcpServer
```

Or uninstall and reinstall:

```bash
dotnet tool uninstall -g OpenApiMcpServer
dotnet tool install -g OpenApiMcpServer
```

## Usage

### Supported Operations

Once connected via an MCP client (Claude Desktop, VS Code, or other MCP-compatible tools), you can:

- **List contracts** — discover available API specifications
- **Get contract information** — retrieve title, version, servers, and security schemes
- **Browse operations** — list all HTTP endpoints with method and path
- **Query schemas** — inspect data models and their properties
- **Search contracts** — find operations or schemas by keyword
- **Edit contracts** — add, modify, or delete paths, operations, and schemas
- **Validate changes** — ensure edits comply with OpenAPI spec
- **Export contracts** — save modified contracts back to disk

## Example

In your AI tools, try: 

- List all the operations from the openapi contract <file>.

Once the current context contains the file you're interested in working with you can continue your conversation to:

- Get index of all operations
- Retrieve a specific operation: GET /pets
- Get a schema: Pet
- Search for "user" operations
- Make staged edits and commit them

## Requirements

- .NET 10.0 or later

## Support

Issues and contributions are welcome on GitHub.

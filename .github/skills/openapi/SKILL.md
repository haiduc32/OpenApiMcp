---
name: openapi
description:  Instructions for reading, navigating, searching, and modifying large OpenAPI contracts using the openapi MCP server tools. Use this skill when asked about API schemas, paths, or operations, or when asked to add, update, delete, or rename any part of an OpenAPI  contract that is too large to load into context directly.
---

Use the MCP tools exposed by the `openapi-contracts` server to work with OpenAPI contracts without loading the full file into context. All tools return JSON. Errors are returned as `{"error":{"code":"...","message":"...","pointer":"..."}}` — never thrown.

## Step 0 — Register the contract

Before anything else, a contract must be registered with the server. Call `list_contracts()` first — if the contract you need is already listed, skip to Step 1.

If it is not registered yet, register it now:

```
# From a file path on disk
register_contract(file_path: "/absolute/path/to/api.yaml")

# From inline content
register_contract(content: "<yaml or json string>", format: "yaml")

# Optionally pin an explicit ID (otherwise derived from the title)
register_contract(file_path: "/path/to/api.yaml", id: "my-api")
```

The tool returns the assigned `id`. Use that id in every subsequent call.

To remove a contract that is no longer needed (in-memory only; the backing file is not deleted):

```
unregister_contract(contract_id)
```

## Step 1 — Find the contract ID

List all registered contracts to confirm the id:

```
list_contracts()
```

Use the returned `id` in every subsequent call.

## Step 2 — Orient yourself

Choose the right tool based on what you already know:

```
# Top-level metadata only (info, servers, security, tags) — always safe, always small

 get_contract_info(contract_id)

# Full navigable index (all paths + methods + summaries + schema names)
get_contract_index(contract_id)

# Group the index by tag to understand domain areas
get_contract_index(contract_id, include_tags: true)
```

Prefer `get_contract_index` over `export_contract` for orientation. Only call `export_contract`
when you genuinely need the entire document (e.g. to write it to a file).

## Read tools

```
# Single operation — resolves immediate $ref values inline
get_operation(contract_id, path, method)
# e.g. get_operation("my-api", "/users/{id}", "get")

# Named schema — resolves $ref to the given depth
get_schema(contract_id, name)
get_schema(contract_id, name, resolve_depth: 2)   # inline up to 2 levels of $ref

# Arbitrary node by JSON Pointer (RFC 6901) — use only when get_operation / get_schema don't apply
get_fragment(contract_id, pointer)
get_fragment(contract_id, "/info")
get_fragment(contract_id, "/paths/~1pets/get")     # / → ~1, ~ → ~0
get_fragment(contract_id, pointer, resolve_refs: false)  # skip $ref resolution
get_fragment(contract_id, pointer, format: "yaml")       # return as YAML text

# Keyword or regex search across paths, operations, and schemas
search_contract(contract_id, query)
search_contract(contract_id, "Order", scope: "schemas", max_results: 10)
# scope: "paths" | "schemas" | "all" (default)

# All operations tagged with a specific tag
get_tag_operations(contract_id, tag)

# Export the full contract document — prefer the selective tools above for large contracts
export_contract(contract_id)                              # committed version, YAML output
export_contract(contract_id, format: "json")              # JSON output
export_contract(contract_id, session_id: sid)             # export the staged (uncommitted) version
```

## Write workflow

All writes require an open session. Follow this sequence exactly:

### 1. Open a session

```
open_session(contract_id, description: "brief intent")
# Returns session_id — required by every write and validation tool
```

Check for an existing open session first with `list_sessions(contract_id)` to avoid duplicates.

```
list_sessions()                      # all open sessions
list_sessions(contract_id: "my-api") # filter by contract
```

### 2. Apply changes (pick the right tool)

**Add or replace a named schema:**
```
add_schema(session_id, name, schema)
add_schema(sid, "Widget", '{"type":"object","properties":{"id":{"type":"integer"}}}')
```

**Add or replace a full path item:**
```
add_path(session_id, path, path_item)
add_path(sid, "/orders/{orderId}", '{"get":{"operationId":"getOrder","responses":{"200":{"description":"OK"}}}}')
```

**Add or replace a single operation on an existing path:**
```
add_operation(session_id, path, method, operation)
add_operation(sid, "/pets", "post", '{"operationId":"createPet","responses":{"201":{"description":"Created"}}}')
```

**Set any node by JSON Pointer:**
```
set_fragment(session_id, pointer, content)
set_fragment(sid, "/info/x-owner", '{"team":"platform"}')
```

**Apply a JSON Merge Patch (partial update) or JSON Patch (precise ops):**
```
patch_fragment(session_id, pointer, patch, patch_type: "merge")
patch_fragment(session_id, pointer, patch, patch_type: "json-patch")
patch_fragment(sid, "/info", '{"x-env":"prod"}', "merge")
patch_fragment(sid, "", patch, "merge")   # pointer "" targets the document root
```

**Delete a node:**
```
delete_fragment(session_id, pointer)
delete_fragment(sid, "/components/schemas/DeprecatedDto")
```

**Rename a schema and update all `$ref` values in one step:**
```
rename_schema(session_id, old_name, new_name)
rename_schema(sid, "OrderDto", "Order")
```

### 3. Review changes before committing

```
diff_session(session_id)              # summary diff
diff_session(session_id, format: "full")
diff_session(session_id, pointer: "/components/schemas")  # scoped to a subtree
```

### 4. Validate

```
validate_session(session_id)               # standard validation
validate_session(session_id, strict: true) # also enforce best-practice rules
```

If `valid` is `false`, fix the errors and call `validate_session` again before committing.
Do **not** call `commit_session` when there are outstanding errors.

### 5a. Commit

```
commit_session(session_id)
commit_session(session_id, message: "Add /orders endpoint")
commit_session(session_id, validate_before_commit: false)  # skip auto-validation (not recommended)
commit_session(session_id, allow_warnings: false)          # treat warnings as errors
```

A successful commit returns `committed: true` and a `commit_ref` hash. If `validate_before_commit` is `true` (default) and the contract is invalid, the commit is rejected and returns `committed: false` with the validation errors.

### 5b. Discard (cancel)

```
close_session(session_id)
```

Always close a session when abandoning an edit, even after a successful commit
(commit closes automatically, but close_session is safe to call again).

## JSON Pointer encoding reminder

When building pointers manually:
- Path `/pets/{petId}` → segment `pets~1{petId}` (i.e. pointer `/paths/~1pets~1{petId}`)
- `~` → `~0`
- `/` → `~1`

Use `get_fragment(contract_id, pointer)` to verify a pointer exists before writing to it.

## Recommended workflows

### Understand an unfamiliar contract
1. `list_contracts()` → check if the contract is already registered
2. If not: `register_contract(file_path: "...")` → register and get the id
3. `get_contract_index(id)` → see everything at a glance
4. `get_contract_index(id, include_tags: true)` → understand domain groupings
5. `get_schema(id, "SomeModel", resolve_depth: 2)` → inspect key data types
6. `get_operation(id, "/path", "method")` → inspect a specific endpoint

### Add a new feature
1. `list_contracts()` → check if the contract is already registered
2. If not: `register_contract(file_path: "...")` → register and get the id
3. `get_contract_index(id)` → confirm there are no conflicts
4. `open_session(id, "Add /orders")`
5. `add_schema(sid, ...)` → add required types first
6. `add_path(sid, "/orders", ...)` → add the path
7. `validate_session(sid)` → fix any errors
8. `diff_session(sid)` → review scope of changes
9. `commit_session(sid, message: "Add /orders")`

### Refactor a schema name
1. `list_contracts()` → check if the contract is already registered
2. If not: `register_contract(file_path: "...")` → register and get the id
3. `search_contract(id, "OldName")` → preview all affected locations
4. `open_session(id, "Rename OldName to NewName")`
5. `rename_schema(sid, "OldName", "NewName")` → renames schema and updates all `$ref` in one step
6. `validate_session(sid)` → confirm contract is still valid
7. `commit_session(sid)`

# OpenAPI Contract MCP — Specification

**Version:** 1.0.0  
**Status:** Draft  
**Purpose:** Define the tools, resources, and behaviours an MCP server must expose to allow AI assistants to read, navigate, and write large OpenAPI contracts efficiently.

---

## 1. Motivation & Design Goals

OpenAPI contracts in enterprise systems frequently exceed the context window of any LLM. A naïve "read the whole file" approach is not viable. This MCP addresses that by:

- Exposing **structured, selective access** to contract fragments rather than raw file content.
- Maintaining a **working state** per session so the AI can make incremental edits without holding the full document in memory.
- Providing **semantic navigation** so the AI can find what it needs (paths, schemas, operations) without full-file traversal.
- Abstracting storage — the implementation may read from local files, git repositories, object storage, or any other backend.

---

## 2. Terminology

| Term | Meaning |
|---|---|
| **Contract** | A single OpenAPI document (v2 or v3.x), identified by a unique ID within this server. |
| **Fragment** | A subtree of a contract's JSON/YAML structure, addressed by a JSON Pointer (RFC 6901). |
| **Pointer** | A JSON Pointer string, e.g. `/paths/~1users/get` or `/components/schemas/User`. |
| **Session** | A stateful edit session opened against a contract. Edits are staged until committed. |
| **Tag** | An OpenAPI tag string used to group operations logically. |

---

## 3. MCP Server Metadata

```
name:        openapi-contracts
version:     1.0.0
description: Read, navigate, and write OpenAPI contracts with large-file support.
```

---

## 4. Resources

Resources expose read-only, URI-addressable content that the AI host can embed directly into context.

### 4.1 `openapi://contracts`
Lists all contracts registered with this server.  
**Returns:** A newline-delimited list of contract IDs with their titles and OpenAPI versions.

### 4.2 `openapi://contracts/{contractId}/info`
Returns the `info`, `servers`, `security`, and `tags` sections of the named contract. This is always small and safe to load as context.

### 4.3 `openapi://contracts/{contractId}/index`
Returns a structured index of the contract:
- All path strings with their supported HTTP methods and summary lines.
- All top-level schema names under `components/schemas`.
- All top-level parameter, response, header, and securityScheme names.

The index is intentionally flat and compact — designed to fit in context even for very large contracts.

### 4.4 `openapi://contracts/{contractId}/fragment?pointer={pointer}`
Returns the JSON/YAML subtree at the given JSON Pointer. Implementations must resolve `$ref` values one level deep (inline the immediate target) and annotate whether deeper `$ref` values exist.

---

## 5. Tools

Tools are callable by the AI to perform actions. All tools return structured responses (see §7).

---

### 5.1 Contract Management

#### `list_contracts`
List all available contracts.

**Input:** _(none)_

**Output:**
```
contracts[]:
  id          string   Unique identifier within this server.
  title       string   OpenAPI info.title value.
  version     string   OpenAPI info.version value.
  openapi     string   OpenAPI specification version (e.g. "3.1.0").
  source      string   Human-readable location hint (path, URL, etc.).
  size_lines  integer  Approximate line count.
```

---

#### `get_contract_info`
Retrieve the `info`, `servers`, `security`, and `tags` blocks.

**Input:**
```
contract_id  string  required
```

**Output:** The raw content of those top-level keys, serialised as the contract's native format (JSON or YAML).

---

#### `get_contract_index`
Retrieve a navigable index of all paths and component names.

**Input:**
```
contract_id   string   required
include_tags  boolean  optional  When true, group paths by tag in the output. Default: false.
```

**Output:**
```
paths[]:
  path        string    The path string, e.g. "/users/{id}".
  methods[]:
    method    string    HTTP method in lowercase.
    operationId string  If present.
    summary   string    If present.
    tags      string[]  If present.
    deprecated boolean

components:
  schemas          string[]   Names of all schemas.
  parameters       string[]
  responses        string[]
  requestBodies    string[]
  headers          string[]
  securitySchemes  string[]
  callbacks        string[]
  pathItems        string[]   (OpenAPI 3.1+)
```

---

### 5.2 Fragment Reading

#### `get_fragment`
Read a specific subtree of the contract by JSON Pointer.

**Input:**
```
contract_id     string   required
pointer         string   required   JSON Pointer (RFC 6901).
resolve_refs    boolean  optional   Inline immediate $ref targets. Default: true.
format          enum     optional   "json" | "yaml" | "native". Default: "native".
```

**Output:**
```
pointer         string   The pointer that was resolved.
content         string   Serialised fragment content.
ref_targets[]   string   Pointers of any un-resolved $refs found within.
exists          boolean  False when the pointer matched nothing.
```

---

#### `get_operation`
Retrieve a single operation (path + method) including its request body, parameters, and response schemas, with all immediate `$ref` values resolved.

**Input:**
```
contract_id  string  required
path         string  required   e.g. "/users/{id}"
method       string  required   e.g. "get"
```

**Output:**
```
operation    object  The resolved operation object.
refs[]       string  Pointers of any un-resolved deeper $refs.
```

---

#### `get_schema`
Retrieve a named schema from `components/schemas`, with optional shallow resolution of `$ref` and `allOf`/`oneOf`/`anyOf` members.

**Input:**
```
contract_id    string   required
name           string   required
resolve_depth  integer  optional  How many $ref levels to inline. Default: 1. Max: 3.
```

**Output:**
```
pointer        string   Canonical pointer for this schema.
content        string   Serialised schema.
resolved_refs  object   Map of pointer → inlined content for each resolved ref.
unresolved_refs string[] Pointers not inlined due to depth limit.
```

---

#### `search_contract`
Find paths, operations, or schemas matching a keyword or pattern.

**Input:**
```
contract_id  string  required
query        string  required   Free text or regex pattern.
scope        enum    optional   "paths" | "schemas" | "all". Default: "all".
max_results  integer optional   Default: 20.
```

**Output:**
```
results[]:
  pointer     string  JSON Pointer to the matching node.
  kind        enum    "path" | "operation" | "schema" | "other".
  match_context string  The matching text snippet with surrounding context.
```

---

#### `get_tag_operations`
Return all operations associated with a specific tag.

**Input:**
```
contract_id  string  required
tag          string  required
```

**Output:**
```
tag          string
operations[]:
  path        string
  method      string
  operationId string
  summary     string
  pointer     string
```

---

### 5.3 Session Management

Large edits are staged in a session. The AI opens a session, makes multiple patch calls, then commits or discards.

#### `open_session`
Open an edit session against a contract.

**Input:**
```
contract_id  string  required
description  string  optional  Human-readable intent, e.g. "Add payment endpoints".
```

**Output:**
```
session_id   string  Unique session identifier.
contract_id  string
opened_at    string  ISO 8601 timestamp.
```

---

#### `close_session`
Discard a session and all staged changes without writing anything.

**Input:**
```
session_id  string  required
```

**Output:**
```
session_id  string
discarded   boolean
```

---

#### `list_sessions`
List open sessions, optionally filtered by contract.

**Input:**
```
contract_id  string  optional
```

**Output:**
```
sessions[]:
  session_id   string
  contract_id  string
  description  string
  opened_at    string
  patch_count  integer  Number of staged patches.
```

---

### 5.4 Writing & Editing

All write operations require an open session. Changes are **staged**, not persisted, until `commit_session` is called.

#### `set_fragment`
Set (create or replace) the value at a JSON Pointer within the session.

**Input:**
```
session_id  string  required
pointer     string  required   Destination pointer.
content     string  required   JSON or YAML content to set at this pointer.
format      enum    optional   "json" | "yaml". Inferred from content if omitted.
```

**Output:**
```
pointer     string
action      enum    "created" | "replaced"
prev_existed boolean
```

---

#### `patch_fragment`
Apply a JSON Merge Patch (RFC 7396) or JSON Patch (RFC 6902) to the subtree at a pointer.

**Input:**
```
session_id   string  required
pointer      string  required   Root pointer to patch against. Use "" for document root.
patch        string  required   Serialised patch document.
patch_type   enum    required   "merge" | "json-patch"
```

**Output:**
```
pointer      string
patch_type   string
affected_keys string[]  Top-level keys changed within the target object.
```

---

#### `delete_fragment`
Remove a node at a given pointer.

**Input:**
```
session_id  string  required
pointer     string  required
```

**Output:**
```
pointer     string
deleted     boolean
```

---

#### `rename_schema`
Rename a component schema and update all `$ref` values that reference it throughout the staged document.

**Input:**
```
session_id  string  required
old_name    string  required
new_name    string  required
```

**Output:**
```
old_pointer  string
new_pointer  string
refs_updated integer  Count of $ref strings updated.
```

---

#### `add_path`
Convenience tool to add or replace a full path item.

**Input:**
```
session_id    string  required
path          string  required   e.g. "/orders/{orderId}"
path_item     string  required   Serialised PathItem object (JSON or YAML).
```

**Output:**
```
pointer       string
action        enum    "created" | "replaced"
operations[]  string  HTTP methods found in the provided path item.
```

---

#### `add_operation`
Add or replace a single operation on an existing or new path.

**Input:**
```
session_id   string  required
path         string  required
method       string  required
operation    string  required   Serialised Operation object.
```

**Output:**
```
pointer      string
action       enum    "created" | "replaced"
```

---

#### `add_schema`
Add or replace a schema in `components/schemas`.

**Input:**
```
session_id  string  required
name        string  required
schema      string  required   Serialised Schema object.
```

**Output:**
```
pointer     string
action      enum    "created" | "replaced"
```

---

### 5.5 Validation & Commit

#### `validate_session`
Validate the staged contract against the OpenAPI specification.

**Input:**
```
session_id    string   required
strict        boolean  optional  Enforce additional best-practice rules. Default: false.
```

**Output:**
```
valid         boolean
errors[]:
  pointer     string
  message     string
  severity    enum  "error" | "warning"
warnings[]:
  pointer     string
  message     string
```

---

#### `diff_session`
Show a human-readable diff of all changes staged in the session.

**Input:**
```
session_id  string   required
format      enum     optional  "summary" | "full". Default: "summary".
pointer     string   optional  Scope diff to a subtree. Default: entire document.
```

**Output:**
```
session_id  string
patch_count integer
diff        string   Human-readable diff text.
```

---

#### `commit_session`
Persist all staged changes to the contract's backing store. Fails if validation errors exist (warnings are allowed through by default).

**Input:**
```
session_id           string   required
message              string   optional  Commit message for audit log.
allow_warnings       boolean  optional  Proceed even if warnings present. Default: true.
validate_before_commit boolean optional Default: true.
```

**Output:**
```
session_id     string
committed      boolean
contract_id    string
errors[]       object   Populated when committed: false due to validation failure.
commit_ref     string   Implementation-defined reference (e.g. git SHA, version tag).
```

---

### 5.6 Export

#### `export_contract`
Export the current (committed) contract or a session's staged version.

**Input:**
```
contract_id  string   required
session_id   string   optional  If provided, export the staged (uncommitted) version.
format       enum     optional  "json" | "yaml". Default: "yaml".
```

**Output:**
```
content      string   Full serialised contract.
format       string
line_count   integer
```

> ⚠️ **Note:** For contracts exceeding the model's context window, prefer selective fragment tools over this export. Use `export_contract` only when the full text is genuinely needed (e.g. to write to a file or pipe to another tool).

---

## 6. Error Model

All tools return errors using a standard envelope:

```
error:
  code     string   Machine-readable error code (see §6.1).
  message  string   Human-readable description.
  pointer  string   (optional) JSON Pointer to the offending location.
  details  object   (optional) Additional structured context.
```

### 6.1 Error Codes

| Code | Meaning |
|---|---|
| `CONTRACT_NOT_FOUND` | No contract with the given ID exists. |
| `SESSION_NOT_FOUND` | Session ID is invalid or expired. |
| `INVALID_POINTER` | The JSON Pointer is malformed or resolves to nothing. |
| `INVALID_CONTENT` | The provided JSON/YAML content could not be parsed. |
| `VALIDATION_FAILED` | Commit rejected due to validation errors. |
| `CONFLICT` | A write conflicts with concurrent session changes. |
| `UNSUPPORTED_FORMAT` | The contract format or OpenAPI version is not supported. |
| `READ_ONLY` | The backing store does not permit writes. |
| `REF_CYCLE` | A circular `$ref` was detected during resolution. |

---

## 7. Recommended AI Usage Patterns

Implementors should document these patterns for consuming AI tools.

### Pattern A — Explore then Edit
1. `list_contracts` → pick a contract.
2. `get_contract_info` → understand purpose and servers.
3. `get_contract_index` → orient to available paths and schemas.
4. `search_contract` → locate relevant sections.
5. `get_operation` or `get_schema` → load specific fragments.
6. `open_session` → begin staging edits.
7. One or more write tools.
8. `diff_session` → review what will change.
9. `validate_session` → confirm correctness.
10. `commit_session` → persist.

### Pattern B — Add New Feature
1. `get_contract_index` → check existing schemas and paths to avoid duplication.
2. `get_schema` for related existing schemas.
3. `open_session`.
4. `add_schema` for any new types needed.
5. `add_path` or `add_operation` for new endpoints.
6. `validate_session`.
7. `commit_session`.

### Pattern C — Refactor / Rename
1. `search_contract` → identify all usages of an element.
2. `open_session`.
3. `rename_schema` (for schema renames) or `patch_fragment` for other structural changes.
4. `diff_session` → verify scope of changes.
5. `validate_session`.
6. `commit_session`.

---

## 8. Implementation Requirements

### 8.1 Mandatory
- MUST support OpenAPI 3.0.x and 3.1.x contracts.
- MUST implement all tools defined in §5.
- MUST implement all resources defined in §4.
- MUST enforce session isolation — changes in one session MUST NOT be visible in another session's reads until committed.
- MUST resolve `$ref` values within the same document for `get_operation` and `get_schema`.
- MUST validate against the OpenAPI specification on `validate_session` and (by default) before `commit_session`.
- MUST return errors using the structure defined in §6.

### 8.2 Recommended
- SHOULD support OpenAPI 2.0 (Swagger) contracts in read-only mode at minimum.
- SHOULD support external `$ref` resolution (HTTP and file path).
- SHOULD persist an audit log of commit messages and authoring metadata.
- SHOULD implement optimistic concurrency — reject a commit if the base contract changed since the session was opened.
- SHOULD stream large `export_contract` results rather than buffering entirely in memory.

### 8.3 Optional
- MAY support contract registration/deregistration via additional tools (`register_contract`, `unregister_contract`).
- MAY expose a `watch_contract` tool or resource subscription to notify of external changes.
- MAY provide a `generate_sdk_types` tool that extracts schemas as language-specific type stubs.
- MAY support multi-file contracts (contracts split across multiple files linked by external `$ref`).

---

## 9. Security Considerations

- The MCP server MUST NOT expose credentials, internal hostnames, or sensitive data embedded in contract descriptions.
- Write tools MUST be guarded; implementations should support a **read-only mode** for environments where AI tools should only inspect contracts.
- Commit operations SHOULD require explicit confirmation in interactive contexts.
- Implementations MUST validate that pointer values do not allow path traversal outside the contract document.
- Session TTL (time-to-live) SHOULD be configurable to prevent abandoned sessions accumulating.

---

## 10. Versioning

This specification follows Semantic Versioning. Backward-compatible additions (new optional tools or fields) increment the minor version. Breaking changes to existing tool signatures increment the major version. Implementations MUST advertise their supported spec version in the MCP server metadata.

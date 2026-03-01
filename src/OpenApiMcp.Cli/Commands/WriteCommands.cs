using System.CommandLine;
using OpenApiMcp.Cli.Helpers;
using OpenApiMcp.Services;

namespace OpenApiMcp.Cli.Commands;

/// <summary>
/// Write commands: set, patch, delete, rename-schema, add-path, add-operation, add-schema.
/// Each command manages the session lifecycle internally (open → write → validate → commit).
/// On validation failure the document is NOT committed and exit code 1 is returned.
/// </summary>
internal static class WriteCommands
{
    public static void Register(RootCommand root, ContractStore store)
    {
        root.AddCommand(SetCmd(store));
        root.AddCommand(PatchCmd(store));
        root.AddCommand(DeleteCmd(store));
        root.AddCommand(RenameSchemaCmd(store));
        root.AddCommand(AddPathCmd(store));
        root.AddCommand(AddOperationCmd(store));
        root.AddCommand(AddSchemaCmd(store));
    }

    // ── Shared argument/option factories ──────────────────────────────────────

    private static Argument<string> ContractIdArg() =>
        new("contract-id", "The contract ID.");

    private static Argument<string> ContentArg(string name, string desc) =>
        new(name, $"{desc}. Use @file to read from a file, or - for stdin.");

    // ── set ───────────────────────────────────────────────────────────────────

    private static Command SetCmd(ContractStore store)
    {
        var cmd        = new Command("set", "Create or replace the value at a JSON Pointer.");
        var idArg      = ContractIdArg();
        var ptrArg     = new Argument<string>("pointer", "Destination JSON Pointer.");
        var contentArg = ContentArg("content", "JSON or YAML content to place at this pointer");
        var fmtOpt     = new Option<string?>("--format", "Content format hint: json | yaml.");
        cmd.AddArgument(idArg); cmd.AddArgument(ptrArg);
        cmd.AddArgument(contentArg); cmd.AddOption(fmtOpt);

        cmd.SetHandler((id, ptr, raw, fmt) =>
        {
            var content = CliHelper.ReadContent(raw);
            int rc = CliHelper.RunWrite(store, id, $"set {ptr}", session =>
            {
                var value   = ContentSerializer.Parse(content, fmt);
                var existed = JsonPointerHelper.Set(session.StagedDocument, ptr, value);
                CliHelper.Ok(new { pointer = ptr, action = existed ? "replaced" : "created", prev_existed = existed });
            });
            Environment.ExitCode = rc;
        }, idArg, ptrArg, contentArg, fmtOpt);
        return cmd;
    }

    // ── patch ─────────────────────────────────────────────────────────────────

    private static Command PatchCmd(ContractStore store)
    {
        var cmd      = new Command("patch", "Apply a JSON Merge Patch or JSON Patch to the document.");
        var idArg    = ContractIdArg();
        var ptrArg   = new Argument<string>("pointer", "Root pointer. Use empty string for the whole document.");
        var patchArg = ContentArg("patch", "Serialised patch document");
        var typeOpt  = new Option<string>("--type", () => "merge", "Patch type: merge | json-patch.");
        cmd.AddArgument(idArg); cmd.AddArgument(ptrArg);
        cmd.AddArgument(patchArg); cmd.AddOption(typeOpt);

        cmd.SetHandler((id, ptr, raw, patchType) =>
        {
            var content = CliHelper.ReadContent(raw);
            int rc = CliHelper.RunWrite(store, id, $"patch {ptr} ({patchType})", session =>
            {
                var patchNode = ContentSerializer.Parse(content);
                System.Text.Json.Nodes.JsonNode target = ptr == ""
                    ? session.StagedDocument
                    : JsonPointerHelper.Navigate(session.StagedDocument, ptr)
                      ?? throw new McpException("INVALID_POINTER", $"Pointer '{ptr}' not found.", ptr);

                System.Text.Json.Nodes.JsonNode patched;
                if (patchType == "merge")
                    patched = JsonPointerHelper.ApplyMergePatch(target, patchNode);
                else if (patchType == "json-patch")
                    patched = ptr == ""
                        ? JsonPointerHelper.ApplyJsonPatch(session.StagedDocument, patchNode)
                        : JsonPointerHelper.ApplyJsonPatch(target, patchNode);
                else
                    throw new McpException("INVALID_CONTENT", $"Unknown patch type '{patchType}'. Use 'merge' or 'json-patch'.");

                if (ptr == "")
                {
                    if (patched is System.Text.Json.Nodes.JsonObject patchedObj && session.StagedDocument is System.Text.Json.Nodes.JsonObject stageObj)
                    {
                        foreach (var key in stageObj.Select(k => k.Key).ToList())
                            stageObj.Remove(key);
                        foreach (var (k, v) in patchedObj.ToList())
                        {
                            patchedObj.Remove(k);
                            stageObj[k] = v;
                        }
                    }
                }
                else
                    JsonPointerHelper.Set(session.StagedDocument, ptr, patched);

                CliHelper.Ok(new { pointer = ptr, patch_type = patchType });
            });
            Environment.ExitCode = rc;
        }, idArg, ptrArg, patchArg, typeOpt);
        return cmd;
    }

    // ── delete ────────────────────────────────────────────────────────────────

    private static Command DeleteCmd(ContractStore store)
    {
        var cmd    = new Command("delete", "Remove the node at a JSON Pointer.");
        var idArg  = ContractIdArg();
        var ptrArg = new Argument<string>("pointer", "RFC 6901 JSON Pointer to the node to remove.");
        cmd.AddArgument(idArg); cmd.AddArgument(ptrArg);

        cmd.SetHandler((id, ptr) =>
        {
            int rc = CliHelper.RunWrite(store, id, $"delete {ptr}", session =>
            {
                var deleted = JsonPointerHelper.Delete(session.StagedDocument, ptr);
                CliHelper.Ok(new { pointer = ptr, deleted });
            });
            Environment.ExitCode = rc;
        }, idArg, ptrArg);
        return cmd;
    }

    // ── rename-schema ─────────────────────────────────────────────────────────

    private static Command RenameSchemaCmd(ContractStore store)
    {
        var cmd    = new Command("rename-schema", "Rename a component schema and update all $ref values.");
        var idArg  = ContractIdArg();
        var oldArg = new Argument<string>("old-name", "Current schema name.");
        var newArg = new Argument<string>("new-name", "New schema name.");
        cmd.AddArgument(idArg); cmd.AddArgument(oldArg); cmd.AddArgument(newArg);

        cmd.SetHandler((id, oldName, newName) =>
        {
            int rc = CliHelper.RunWrite(store, id, $"rename-schema {oldName}→{newName}", session =>
            {
                var oldPtr = $"/components/schemas/{oldName}";
                var newPtr = $"/components/schemas/{newName}";
                var schema = JsonPointerHelper.Navigate(session.StagedDocument, oldPtr)
                             ?? throw new McpException("INVALID_POINTER", $"Schema '{oldName}' not found.", oldPtr);

                JsonPointerHelper.Delete(session.StagedDocument, oldPtr);
                JsonPointerHelper.Set(session.StagedDocument, newPtr, schema.DeepClone());

                int refsUpdated = UpdateRefs(session.StagedDocument,
                    $"#/components/schemas/{oldName}",
                    $"#/components/schemas/{newName}");

                CliHelper.Ok(new { old_pointer = oldPtr, new_pointer = newPtr, refs_updated = refsUpdated });
            });
            Environment.ExitCode = rc;
        }, idArg, oldArg, newArg);
        return cmd;
    }

    private static int UpdateRefs(System.Text.Json.Nodes.JsonNode node, string oldRef, string newRef)
    {
        int count = 0;
        if (node is System.Text.Json.Nodes.JsonObject obj)
        {
            if (obj.TryGetPropertyValue("$ref", out var refVal) &&
                refVal?.GetValue<string>() == oldRef)
            {
                obj["$ref"] = newRef;
                return 1;
            }
            foreach (var key in obj.Select(k => k.Key).ToList())
                if (obj[key] is { } child) count += UpdateRefs(child, oldRef, newRef);
        }
        else if (node is System.Text.Json.Nodes.JsonArray arr)
            foreach (var item in arr) if (item is not null) count += UpdateRefs(item, oldRef, newRef);
        return count;
    }

    // ── add-path ──────────────────────────────────────────────────────────────

    private static Command AddPathCmd(ContractStore store)
    {
        var cmd     = new Command("add-path", "Add or replace a full path item.");
        var idArg   = ContractIdArg();
        var pathArg = new Argument<string>("path", "API path, e.g. /users.");
        var itemArg = ContentArg("path-item", "PathItem object (JSON or YAML)");
        cmd.AddArgument(idArg); cmd.AddArgument(pathArg); cmd.AddArgument(itemArg);

        cmd.SetHandler((id, path, raw) =>
        {
            var content = CliHelper.ReadContent(raw);
            int rc = CliHelper.RunWrite(store, id, $"add-path {path}", session =>
            {
                var escaped = path.TrimStart('/').Replace("~", "~0").Replace("/", "~1");
                var ptr     = $"/paths/~1{escaped}";
                var value   = ContentSerializer.Parse(content);
                var existed = JsonPointerHelper.Set(session.StagedDocument, ptr, value);
                var ops     = (value as System.Text.Json.Nodes.JsonObject)?
                              .Select(k => k.Key)
                              .Where(m => new[] { "get","post","put","delete","patch","options","head","trace" }.Contains(m))
                              .ToList() ?? [];
                CliHelper.Ok(new { pointer = ptr, action = existed ? "replaced" : "created", operations = ops });
            });
            Environment.ExitCode = rc;
        }, idArg, pathArg, itemArg);
        return cmd;
    }

    // ── add-operation ─────────────────────────────────────────────────────────

    private static Command AddOperationCmd(ContractStore store)
    {
        var cmd     = new Command("add-operation", "Add or replace a single operation on a path.");
        var idArg   = ContractIdArg();
        var pathArg = new Argument<string>("path", "API path, e.g. /pets.");
        var methArg = new Argument<string>("method", "HTTP method (lowercase), e.g. post.");
        var opArg   = ContentArg("operation", "Operation object (JSON or YAML)");
        cmd.AddArgument(idArg); cmd.AddArgument(pathArg);
        cmd.AddArgument(methArg); cmd.AddArgument(opArg);

        cmd.SetHandler((id, path, method, raw) =>
        {
            var content = CliHelper.ReadContent(raw);
            int rc = CliHelper.RunWrite(store, id, $"add-operation {method} {path}", session =>
            {
                var escaped = path.TrimStart('/').Replace("~", "~0").Replace("/", "~1");
                var ptr     = $"/paths/~1{escaped}/{method.ToLowerInvariant()}";
                var value   = ContentSerializer.Parse(content);
                var existed = JsonPointerHelper.Set(session.StagedDocument, ptr, value);
                CliHelper.Ok(new { pointer = ptr, action = existed ? "replaced" : "created" });
            });
            Environment.ExitCode = rc;
        }, idArg, pathArg, methArg, opArg);
        return cmd;
    }

    // ── add-schema ────────────────────────────────────────────────────────────

    private static Command AddSchemaCmd(ContractStore store)
    {
        var cmd     = new Command("add-schema", "Add or replace a named schema in components/schemas.");
        var idArg   = ContractIdArg();
        var nameArg = new Argument<string>("name", "Schema name.");
        var schArg  = ContentArg("schema", "Schema object (JSON or YAML)");
        cmd.AddArgument(idArg); cmd.AddArgument(nameArg); cmd.AddArgument(schArg);

        cmd.SetHandler((id, name, raw) =>
        {
            var content = CliHelper.ReadContent(raw);
            int rc = CliHelper.RunWrite(store, id, $"add-schema {name}", session =>
            {
                var ptr     = $"/components/schemas/{name}";
                var value   = ContentSerializer.Parse(content);
                var existed = JsonPointerHelper.Set(session.StagedDocument, ptr, value);
                CliHelper.Ok(new { pointer = ptr, action = existed ? "replaced" : "created" });
            });
            Environment.ExitCode = rc;
        }, idArg, nameArg, schArg);
        return cmd;
    }
}

using System.ComponentModel;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using ModelContextProtocol.Server;
using OpenApiMcp.Models;
using OpenApiMcp.Services;
using static OpenApiMcp.Tools.ToolHelper;

namespace OpenApiMcp.Tools;

[McpServerToolType]
public sealed class WriteTools(SessionManager sessions)
{
    // ── set_fragment ──────────────────────────────────────────────────────────

    [McpServerTool, Description("Advanced (session required): Set any value in the contract by JSON Pointer. Prefer add_operation, add_path, or add_schema for common edit cases — use this only for nodes those tools do not cover.")]
    public string set_fragment(
        [Description("The session ID.")]                              string  session_id,
        [Description("Destination JSON Pointer.")]                    string  pointer,
        [Description("JSON or YAML content to place at this pointer.")] string content,
        [Description("Content format hint: json | yaml.")]            string? format = null) => Run(() =>
    {
        var session = sessions.Get(session_id);
        var value   = ContentSerializer.Parse(content, format);
        var existed = JsonPointerHelper.Set(session.StagedDocument, pointer, value);
        RecordPatch(session, pointer, "set", content);
        return Ok(new { pointer, action = existed ? "replaced" : "created", prev_existed = existed });
    });

    // ── patch_fragment ────────────────────────────────────────────────────────

    [McpServerTool, Description("Advanced (session required): Apply a JSON Merge Patch (RFC 7396) or JSON Patch (RFC 6902) to the staged contract at a given JSON Pointer. Use 'merge' for partial object updates, 'json-patch' for precise operations (add/remove/replace/move).")]
    public string patch_fragment(
        [Description("The session ID.")]                  string session_id,
        [Description("Root pointer. Use \"\" for root.")] string pointer,
        [Description("Serialised patch document.")]       string patch,
        [Description("Patch type: merge | json-patch.")]  string patch_type) => Run(() =>
    {
        var session = sessions.Get(session_id);
        var patchNode = ContentSerializer.Parse(patch);

        JsonNode target = pointer == ""
            ? session.StagedDocument
            : JsonPointerHelper.Navigate(session.StagedDocument, pointer)
              ?? throw new McpException("INVALID_POINTER", $"Pointer '{pointer}' not found.", pointer);

        JsonNode patched;
        if (patch_type == "merge")
            patched = JsonPointerHelper.ApplyMergePatch(target, patchNode);
        else if (patch_type == "json-patch")
            patched = pointer == ""
                ? JsonPointerHelper.ApplyJsonPatch(session.StagedDocument, patchNode)
                : JsonPointerHelper.ApplyJsonPatch(target, patchNode);
        else
            throw new McpException("INVALID_CONTENT", $"Unknown patch_type '{patch_type}'. Use 'merge' or 'json-patch'.");

        // Write result back
        if (pointer == "")
        {
            // Replace the whole staged document — copy all keys
            var stagObj = session.StagedDocument as JsonObject
                ?? throw new McpException("INVALID_CONTENT", "Document root is not an object.");
            var newObj  = patched as JsonObject
                ?? throw new McpException("INVALID_CONTENT", "Patched result is not an object.");
            // Clear and re-populate (can't replace the reference, mutate in place)
            var keys = stagObj.Select(k => k.Key).ToList();
            foreach (var k in keys) stagObj.Remove(k);
            foreach (var (k, v) in newObj) stagObj[k] = v?.DeepClone();
        }
        else
        {
            JsonPointerHelper.Set(session.StagedDocument, pointer, patched);
        }

        RecordPatch(session, pointer, $"patch-{patch_type}", patch);

        var affectedKeys = (patchNode as JsonObject)?.Select(k => k.Key).ToArray()
                         ?? (patched as JsonObject)?.Select(k => k.Key).ToArray()
                         ?? [];
        return Ok(new { pointer, patch_type, affected_keys = affectedKeys });
    });

    // ── delete_fragment ───────────────────────────────────────────────────────

    [McpServerTool, Description("Session required: Remove a node at a given JSON Pointer from the staged document.")]
    public string delete_fragment(
        [Description("The session ID.")] string session_id,
        [Description("JSON Pointer.")]   string pointer) => Run(() =>
    {
        var session = sessions.Get(session_id);
        var deleted = JsonPointerHelper.Delete(session.StagedDocument, pointer);
        RecordPatch(session, pointer, "delete", null);
        return Ok(new { pointer, deleted });
    });

    // ── rename_schema ─────────────────────────────────────────────────────────

    [McpServerTool, Description("Session required: Rename a component schema and automatically update all $ref values that reference it throughout the staged document.")]
    public string rename_schema(
        [Description("The session ID.")]        string session_id,
        [Description("Current schema name.")]   string old_name,
        [Description("New schema name.")]        string new_name) => Run(() =>
    {
        var session    = sessions.Get(session_id);
        var oldPointer = $"/components/schemas/{old_name}";
        var newPointer = $"/components/schemas/{new_name}";

        var existing = JsonPointerHelper.Navigate(session.StagedDocument, oldPointer)
                       ?? throw new McpException("INVALID_POINTER", $"Schema '{old_name}' not found.", oldPointer);

        // Move the schema
        JsonPointerHelper.Set(session.StagedDocument, newPointer, existing.DeepClone());
        JsonPointerHelper.Delete(session.StagedDocument, oldPointer);

        // Update all $ref strings
        var oldRef = $"#/components/schemas/{old_name}";
        var newRef = $"#/components/schemas/{new_name}";
        var updated = ReplaceRefs(session.StagedDocument, oldRef, newRef);

        RecordPatch(session, oldPointer, $"rename→{newPointer}", null);
        return Ok(new { old_pointer = oldPointer, new_pointer = newPointer, refs_updated = updated });
    });

    // ── add_path ──────────────────────────────────────────────────────────────

    [McpServerTool, Description("Session required: Add or replace a complete path item (all operations for a given path) in the staged contract.")]
    public string add_path(
        [Description("The session ID.")]                           string session_id,
        [Description("Path string, e.g. /orders/{orderId}.")]      string path,
        [Description("Serialised PathItem object (JSON or YAML).")] string path_item) => Run(() =>
    {
        var session   = sessions.Get(session_id);
        var escaped   = path.Replace("~","~0").Replace("/","~1");
        var pointer   = $"/paths/{escaped}";
        var itemNode  = ContentSerializer.Parse(path_item);
        var existed   = JsonPointerHelper.Set(session.StagedDocument, pointer, itemNode);
        var piObj     = itemNode as JsonObject;
        var operations = new[] { "get","post","put","delete","patch","options","head","trace" }
            .Where(m => piObj?[m] != null).ToArray();
        RecordPatch(session, pointer, "set", path_item);
        return Ok(new { pointer, action = existed ? "replaced" : "created", operations });
    });

    // ── add_operation ─────────────────────────────────────────────────────────

    [McpServerTool, Description("Session required: Add or replace a single HTTP operation (e.g. GET /users, POST /orders) on an existing or new path in the staged contract.")]
    public string add_operation(
        [Description("The session ID.")]                         string session_id,
        [Description("Path string.")]                            string path,
        [Description("HTTP method (lowercase).")]                string method,
        [Description("Serialised Operation object (JSON/YAML).")] string operation) => Run(() =>
    {
        var session  = sessions.Get(session_id);
        var escaped  = path.Replace("~","~0").Replace("/","~1");
        var pointer  = $"/paths/{escaped}/{method.ToLowerInvariant()}";
        var opNode   = ContentSerializer.Parse(operation);
        var existed  = JsonPointerHelper.Set(session.StagedDocument, pointer, opNode);
        RecordPatch(session, pointer, "set", operation);
        return Ok(new { pointer, action = existed ? "replaced" : "created" });
    });

    // ── add_schema ────────────────────────────────────────────────────────────

    [McpServerTool, Description("Session required: Add or replace a named schema in components/schemas of the staged contract.")]
    public string add_schema(
        [Description("The session ID.")]                          string session_id,
        [Description("Schema name.")]                             string name,
        [Description("Serialised Schema object (JSON or YAML).")] string schema) => Run(() =>
    {
        var session = sessions.Get(session_id);
        var pointer = $"/components/schemas/{name}";
        var node    = ContentSerializer.Parse(schema);
        var existed = JsonPointerHelper.Set(session.StagedDocument, pointer, node);
        RecordPatch(session, pointer, "set", schema);
        return Ok(new { pointer, action = existed ? "replaced" : "created" });
    });

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static void RecordPatch(Models.Session session, string pointer, string op, string? content)
    {
        session.Patches.Add(new StagedPatch { Pointer = pointer, Operation = op, Content = content });
    }

    /// <summary>Walk the document and replace all occurrences of oldRef string with newRef.</summary>
    private static int ReplaceRefs(JsonNode node, string oldRef, string newRef)
    {
        int count = 0;
        if (node is JsonObject obj)
        {
            if (obj.TryGetPropertyValue("$ref", out var rv) && rv is JsonValue jv && jv.GetValue<string>() == oldRef)
            {
                obj["$ref"] = JsonValue.Create(newRef);
                count++;
            }
            foreach (var (_, child) in obj.ToList())
                if (child is not null) count += ReplaceRefs(child, oldRef, newRef);
        }
        else if (node is JsonArray arr)
        {
            foreach (var item in arr)
                if (item is not null) count += ReplaceRefs(item, oldRef, newRef);
        }
        return count;
    }
}

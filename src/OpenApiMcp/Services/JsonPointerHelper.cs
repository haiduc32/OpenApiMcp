using System.Text.Json;
using System.Text.Json.Nodes;

namespace OpenApiMcp.Services;

/// <summary>
/// Utilities for navigating and mutating a JsonNode tree using RFC 6901 JSON Pointer strings.
/// This implementation does not depend on external JSON-Pointer packages so it stays
/// compatible with System.Text.Json.Nodes across all target frameworks.
/// </summary>
public static class JsonPointerHelper
{
    // ── Segment parsing ───────────────────────────────────────────────────────

    /// <summary>
    /// Parse a JSON Pointer into its decoded segments.
    /// An empty pointer ("") refers to the root document.
    /// </summary>
    public static string[] ParseSegments(string pointer)
    {
        if (string.IsNullOrEmpty(pointer)) return [];
        if (!pointer.StartsWith('/'))
            throw new McpException("INVALID_POINTER", $"JSON Pointer must start with '/' (got '{pointer}').");

        return pointer[1..].Split('/').Select(Unescape).ToArray();
    }

    private static string Unescape(string token) => token.Replace("~1", "/").Replace("~0", "~");
    private static string Escape(string token)   => token.Replace("~", "~0").Replace("/", "~1");

    // ── Navigation ────────────────────────────────────────────────────────────

    /// <summary>Returns the node at the given pointer, or null if not found.</summary>
    public static JsonNode? Navigate(JsonNode root, string pointer)
    {
        var segments = ParseSegments(pointer);
        JsonNode? current = root;

        foreach (var seg in segments)
        {
            if (current is JsonObject obj)
            {
                if (!obj.TryGetPropertyValue(seg, out current) || current is null)
                    return null;
            }
            else if (current is JsonArray arr)
            {
                if (!int.TryParse(seg, out var idx) || idx < 0 || idx >= arr.Count)
                    return null;
                current = arr[idx];
            }
            else
            {
                return null;
            }
        }

        return current;
    }

    /// <summary>Returns true when the pointer addresses an existing node.</summary>
    public static bool Exists(JsonNode root, string pointer)
        => Navigate(root, pointer) is not null || pointer == "";

    // ── Mutation ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Set the value at the given pointer.  Creates intermediate objects if missing.
    /// Returns true when the pointer previously existed (replace), false when created.
    /// </summary>
    public static bool Set(JsonNode root, string pointer, JsonNode? value)
    {
        var segments = ParseSegments(pointer);
        if (segments.Length == 0)
            throw new McpException("INVALID_POINTER", "Cannot replace the document root with set_fragment; use patch_fragment instead.");

        var parent = NavigateToParent(root, segments, create: true)!;
        var key = segments[^1];

        if (parent is JsonObject obj)
        {
            var existed = obj.ContainsKey(key);
            obj.Remove(key);
            obj[key] = value;
            return existed;
        }
        if (parent is JsonArray arr)
        {
            if (!int.TryParse(key, out var idx))
                throw new McpException("INVALID_POINTER", $"Array index '{key}' is not a number.");
            if (idx < arr.Count)
            {
                arr[idx] = value;
                return true;
            }
            arr.Add(value);
            return false;
        }
        throw new McpException("INVALID_POINTER", "Parent node is not an object or array.");
    }

    /// <summary>Delete the node at the given pointer.  Returns true when something was removed.</summary>
    public static bool Delete(JsonNode root, string pointer)
    {
        var segments = ParseSegments(pointer);
        if (segments.Length == 0)
            throw new McpException("INVALID_POINTER", "Cannot delete the document root.");

        var parent = NavigateToParent(root, segments, create: false);
        if (parent is null) return false;

        var key = segments[^1];
        if (parent is JsonObject obj) return obj.Remove(key);
        if (parent is JsonArray arr)
        {
            if (!int.TryParse(key, out var idx) || idx < 0 || idx >= arr.Count) return false;
            arr.RemoveAt(idx);
            return true;
        }
        return false;
    }

    // ── JSON Merge Patch (RFC 7396) ───────────────────────────────────────────

    public static JsonNode ApplyMergePatch(JsonNode target, JsonNode patch)
    {
        if (patch is not JsonObject patchObj) return patch.DeepClone();

        var result = target is JsonObject ? (JsonObject)target.DeepClone() : new JsonObject();
        foreach (var (key, patchVal) in patchObj)
        {
            if (patchVal is null)
                result.Remove(key);
            else if (result.TryGetPropertyValue(key, out var tgt) && tgt is JsonObject && patchVal is JsonObject)
                result[key] = ApplyMergePatch(tgt, patchVal);
            else
                result[key] = patchVal.DeepClone();
        }
        return result;
    }

    // ── JSON Patch (RFC 6902) ─────────────────────────────────────────────────

    public static JsonNode ApplyJsonPatch(JsonNode document, JsonNode patch)
    {
        if (patch is not JsonArray ops)
            throw new McpException("INVALID_CONTENT", "JSON Patch must be a JSON array of operation objects.");

        var doc = document.DeepClone();
        foreach (var opNode in ops)
        {
            if (opNode is not JsonObject op)
                throw new McpException("INVALID_CONTENT", "Each JSON Patch operation must be an object.");

            var opType = op["op"]?.GetValue<string>() ?? throw new McpException("INVALID_CONTENT", "Missing 'op' field.");
            var path   = op["path"]?.GetValue<string>() ?? throw new McpException("INVALID_CONTENT", "Missing 'path' field.");

            switch (opType)
            {
                case "add":
                    var addVal = (op["value"] ?? throw new McpException("INVALID_CONTENT", "'add' requires 'value'.")).DeepClone();
                    Set(doc, path, addVal);
                    break;
                case "remove":
                    if (!Delete(doc, path))
                        throw new McpException("INVALID_POINTER", $"'remove' target '{path}' not found.");
                    break;
                case "replace":
                    var repVal = (op["value"] ?? throw new McpException("INVALID_CONTENT", "'replace' requires 'value'.")).DeepClone();
                    if (!Set(doc, path, repVal))
                        throw new McpException("INVALID_POINTER", $"'replace' target '{path}' not found.");
                    break;
                case "copy":
                    var from = op["from"]?.GetValue<string>() ?? throw new McpException("INVALID_CONTENT", "'copy' requires 'from'.");
                    var copyVal = Navigate(doc, from)?.DeepClone() ?? throw new McpException("INVALID_POINTER", $"'copy' source '{from}' not found.");
                    Set(doc, path, copyVal);
                    break;
                case "move":
                    var mFrom = op["from"]?.GetValue<string>() ?? throw new McpException("INVALID_CONTENT", "'move' requires 'from'.");
                    var moveVal = Navigate(doc, mFrom)?.DeepClone() ?? throw new McpException("INVALID_POINTER", $"'move' source '{mFrom}' not found.");
                    Delete(doc, mFrom);
                    Set(doc, path, moveVal);
                    break;
                case "test":
                    var testVal = Navigate(doc, path);
                    var expected = op["value"];
                    if (testVal?.ToJsonString() != expected?.ToJsonString())
                        throw new McpException("CONFLICT", $"JSON Patch 'test' failed at '{path}'.");
                    break;
                default:
                    throw new McpException("INVALID_CONTENT", $"Unknown patch op '{opType}'.");
            }
        }
        return doc;
    }

    // ── $ref collection ───────────────────────────────────────────────────────

    /// <summary>Collect all $ref string values within a subtree.</summary>
    public static IReadOnlyList<string> CollectRefs(JsonNode? node)
    {
        var refs = new List<string>();
        Collect(node, refs);
        return refs;
    }

    private static void Collect(JsonNode? node, List<string> refs)
    {
        if (node is null) return;
        if (node is JsonObject obj)
        {
            if (obj.TryGetPropertyValue("$ref", out var refVal) && refVal is JsonValue rv)
                refs.Add(rv.GetValue<string>());
            foreach (var (_, child) in obj)
                Collect(child, refs);
        }
        else if (node is JsonArray arr)
        {
            foreach (var item in arr)
                Collect(item, refs);
        }
    }

    // ── $ref resolution (one level deep) ─────────────────────────────────────

    /// <summary>
    /// Inline local $ref targets one level deep.
    /// Returns a cloned copy with refs replaced.
    /// </summary>
    public static JsonNode ResolveRefsShallow(JsonNode root, JsonNode fragment, out List<string> unresolvedRefs)
    {
        var unresolved = new List<string>();
        var result = ResolveNode(root, fragment.DeepClone(), depth: 0, maxDepth: 1, unresolved);
        unresolvedRefs = unresolved;
        return result;
    }

    public static JsonNode ResolveRefsDepth(JsonNode root, JsonNode fragment, int maxDepth, out List<string> unresolvedRefs)
    {
        var unresolved = new List<string>();
        var result = ResolveNode(root, fragment.DeepClone(), depth: 0, maxDepth: maxDepth, unresolved);
        unresolvedRefs = unresolved;
        return result;
    }

    private static JsonNode ResolveNode(JsonNode root, JsonNode node, int depth, int maxDepth, List<string> unresolved)
    {
        if (node is JsonObject obj)
        {
            if (obj.TryGetPropertyValue("$ref", out var refVal) && refVal is JsonValue rv)
            {
                var refStr = rv.GetValue<string>();
                if (refStr.StartsWith('#') || !refStr.Contains("://"))
                {
                    // local ref
                    var ptr = refStr.StartsWith('#') ? refStr[1..] : refStr;
                    var target = Navigate(root, ptr);
                    if (target is not null && depth < maxDepth)
                    {
                        var cloned = target.DeepClone();
                        return ResolveNode(root, cloned, depth + 1, maxDepth, unresolved);
                    }
                    else
                    {
                        unresolved.Add(refStr);
                        // Must clone: node already has a parent; returning it directly would cause
                        // InvalidOperationException in the caller when assigning to a new parent.
                        return node.DeepClone();
                    }
                }
                else
                {
                    unresolved.Add(refStr);
                    return node.DeepClone();
                }
            }

            var newObj = new JsonObject();
            // Snapshot keys before iterating to avoid mutation during enumeration
            foreach (var key in obj.Select(kv => kv.Key).ToList())
            {
                obj.TryGetPropertyValue(key, out var child);
                newObj[key] = child is null ? null : ResolveNode(root, child, depth, maxDepth, unresolved);
            }
            return newObj;
        }
        if (node is JsonArray arr)
        {
            var newArr = new JsonArray();
            // Snapshot items before iterating
            foreach (var item in arr.ToList())
                newArr.Add(item is null ? null : ResolveNode(root, item, depth, maxDepth, unresolved));
            return newArr;
        }
        // JsonValue — must clone to avoid "node already has a parent" when the caller
        // assigns this value into a new JsonObject / JsonArray.
        return node.DeepClone();
    }

    // ── Pointer builder ───────────────────────────────────────────────────────

    public static string BuildPointer(params string[] segments)
        => "/" + string.Join("/", segments.Select(Escape));

    // ── Private helpers ───────────────────────────────────────────────────────

    private static JsonNode? NavigateToParent(JsonNode root, string[] segments, bool create)
    {
        JsonNode current = root;
        for (int i = 0; i < segments.Length - 1; i++)
        {
            var seg = segments[i];
            if (current is JsonObject obj)
            {
                if (!obj.TryGetPropertyValue(seg, out var next) || next is null)
                {
                    if (!create) return null;
                    var child = new JsonObject();
                    obj[seg] = child;
                    current = child;
                }
                else current = next;
            }
            else if (current is JsonArray arr)
            {
                if (!int.TryParse(seg, out var idx) || idx < 0 || idx >= arr.Count)
                    return null;
                current = arr[idx] ?? throw new McpException("INVALID_POINTER", $"Null node at segment '{seg}'.");
            }
            else return null;
        }
        return current;
    }
}

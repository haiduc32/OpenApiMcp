using System.Text.Json;

namespace OpenApiMcp.IntegrationTests;

/// <summary>Thin wrapper around JsonDocument for fluent result assertions.</summary>
internal static class Json
{
    private static readonly JsonDocumentOptions Opts = new() { CommentHandling = JsonCommentHandling.Skip };

    public static JsonElement Parse(string json) =>
        JsonDocument.Parse(json, Opts).RootElement;

    public static string? Str(this JsonElement el, string property)
    {
        if (el.TryGetProperty(property, out var p))
            return p.ValueKind == JsonValueKind.Null ? null : p.GetString();
        return null;
    }

    public static bool Bool(this JsonElement el, string property, bool defaultValue = false)
    {
        if (el.TryGetProperty(property, out var p))
            return p.GetBoolean();
        return defaultValue;
    }

    public static int Int(this JsonElement el, string property, int defaultValue = 0)
    {
        if (el.TryGetProperty(property, out var p))
            return p.GetInt32();
        return defaultValue;
    }

    public static JsonElement Prop(this JsonElement el, string property)
    {
        if (el.TryGetProperty(property, out var p)) return p;
        throw new InvalidOperationException($"Property '{property}' not found in JSON element.");
    }

    public static bool Has(this JsonElement el, string property) =>
        el.TryGetProperty(property, out _);

    public static bool HasError(this JsonElement el) => el.Has("error");

    public static string ErrorCode(this JsonElement el) =>
        el.Prop("error").Str("code") ?? "";
}

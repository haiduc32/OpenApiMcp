using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.OpenApi.Readers;

namespace OpenApiMcp.Services;

public sealed class ValidationResult
{
    public bool Valid { get; set; }
    public List<ValidationIssue> Errors { get; set; } = [];
    public List<ValidationIssue> Warnings { get; set; } = [];
}

public sealed class ValidationIssue
{
    public string Pointer { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string Severity { get; set; } = "error";
}

/// <summary>Validates an OpenAPI document using Microsoft.OpenApi.</summary>
public static class OpenApiValidator
{
    public static ValidationResult Validate(JsonNode document, bool strict = false)
    {
        var result = new ValidationResult();

        try
        {
            var json = document.ToJsonString(new JsonSerializerOptions { WriteIndented = false });
            using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json));

            var reader = new OpenApiStreamReader();
            var doc = reader.Read(stream, out var diagnostic);

            foreach (var err in diagnostic.Errors)
                result.Errors.Add(new ValidationIssue { Pointer = err.Pointer, Message = err.Message, Severity = "error" });

            foreach (var warn in diagnostic.Warnings)
                result.Warnings.Add(new ValidationIssue { Pointer = warn.Pointer, Message = warn.Message, Severity = "warning" });

            if (strict)
                AddStrictChecks(document, result);
        }
        catch (Exception ex)
        {
            result.Errors.Add(new ValidationIssue { Pointer = "", Message = $"Parser error: {ex.Message}", Severity = "error" });
        }

        result.Valid = result.Errors.Count == 0;
        return result;
    }

    private static void AddStrictChecks(JsonNode doc, ValidationResult result)
    {
        // Warn if paths have operations without operationId
        var paths = doc["paths"] as JsonObject;
        if (paths is null) return;
        foreach (var (path, pathItem) in paths)
        {
            if (pathItem is not JsonObject pi) continue;
            foreach (var method in new[] { "get", "post", "put", "delete", "patch", "options", "head", "trace" })
            {
                var op = pi[method];
                if (op is null) continue;
                if (op["operationId"] is null)
                    result.Warnings.Add(new ValidationIssue
                    {
                        Pointer = $"/paths/{path.Replace("/", "~1")}/{method}",
                        Message = "Operation is missing an operationId.",
                        Severity = "warning"
                    });
                if (op["summary"] is null && op["description"] is null)
                    result.Warnings.Add(new ValidationIssue
                    {
                        Pointer = $"/paths/{path.Replace("/", "~1")}/{method}",
                        Message = "Operation has no summary or description.",
                        Severity = "warning"
                    });
            }
        }
    }
}

using System.Text.Json;
using System.Text.Json.Nodes;

namespace OpenApiMcp.Tools;

/// <summary>Shared serialisation settings and error wrapping helpers for all tool methods.</summary>
public static class ToolHelper
{
    public static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    /// <summary>
    /// Wrap a tool action so that any McpException is serialised as the standard
    /// error envelope defined in §6 of the spec, rather than surfacing as an MCP
    /// protocol error. The AI model can then read the structured error.
    /// </summary>
    public static string Run(Func<string> action)
    {
        try
        {
            return action();
        }
        catch (Services.McpException ex)
        {
            return JsonSerializer.Serialize(new
            {
                error = new
                {
                    code    = ex.Code,
                    message = ex.Message,
                    pointer = ex.Pointer
                }
            }, JsonOpts);
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new
            {
                error = new
                {
                    code    = "INTERNAL_ERROR",
                    message = ex.Message
                }
            }, JsonOpts);
        }
    }

    public static string Ok(object result) => JsonSerializer.Serialize(result, JsonOpts);

    /// <summary>Serialise a JsonNode without double-serialising.</summary>
    public static string OkNode(JsonNode node) => node.ToJsonString(JsonOpts);
}

namespace OpenApiMcp.Services;

/// <summary>Domain exception that maps to the MCP error envelope defined in §6.</summary>
public sealed class McpException(string code, string message, string? pointer = null) : Exception(message)
{
    public string Code { get; } = code;
    public string? Pointer { get; } = pointer;
}

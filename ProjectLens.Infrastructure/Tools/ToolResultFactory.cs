using System.Text.Json;
using ProjectLens.Domain;

namespace ProjectLens.Infrastructure.Tools;

internal static class ToolResultFactory
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true
    };

    public static ToolExecutionResult Success(string toolName, object payload)
    {
        return new ToolExecutionResult(
            toolName,
            true,
            JsonSerializer.Serialize(payload, SerializerOptions));
    }

    public static ToolExecutionResult Failure(string toolName, string errorMessage)
    {
        return new ToolExecutionResult(toolName, false, null, errorMessage);
    }
}

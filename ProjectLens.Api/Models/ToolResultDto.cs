namespace ProjectLens.Api.Models;

public sealed record ToolResultDto(string ToolName, bool Success, string? ErrorMessage);

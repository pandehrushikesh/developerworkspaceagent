namespace ProjectLens.Domain;

public interface ITool
{
    ToolDefinition Definition { get; }

    Task<ToolExecutionResult> ExecuteAsync(
        IReadOnlyDictionary<string, string> arguments,
        CancellationToken cancellationToken = default);
}

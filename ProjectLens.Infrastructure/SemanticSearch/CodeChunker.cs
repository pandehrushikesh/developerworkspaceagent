using System.Text.RegularExpressions;
using ProjectLens.Application.Abstractions;

namespace ProjectLens.Infrastructure.SemanticSearch;

internal static class CodeChunker
{
    private const int MaxChunkLines = 40;
    private static readonly Regex ClassPattern = new(
        @"^\s*(?:public|private|internal|protected|export|sealed|abstract|static|partial|\s)*(?:class|record|interface|struct|enum)\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)",
        RegexOptions.Compiled);
    private static readonly Regex MethodPattern = new(
        @"^\s*(?:public|private|internal|protected|static|virtual|override|async|sealed|partial|export|\s)*(?:[\w<>\[\],?.]+\s+)+(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*\(",
        RegexOptions.Compiled);
    private static readonly Regex FunctionPattern = new(
        @"^\s*(?:async\s+)?(?:function|def)\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*\(",
        RegexOptions.Compiled);
    private static readonly Regex ArrowFunctionPattern = new(
        @"^\s*(?:const|let|var)\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*=\s*(?:async\s*)?\(",
        RegexOptions.Compiled);

    public static IReadOnlyCollection<SemanticCodeChunk> Chunk(string path, string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return Array.Empty<SemanticCodeChunk>();
        }

        var lines = content.Replace("\r\n", "\n").Split('\n');
        var declarations = FindDeclarations(lines);
        if (declarations.Count == 0)
        {
            return ChunkByLineRange(path, lines);
        }

        var chunks = new List<SemanticCodeChunk>();
        for (var index = 0; index < declarations.Count; index++)
        {
            var declaration = declarations[index];
            var nextStart = index < declarations.Count - 1
                ? declarations[index + 1].StartLine - 1
                : lines.Length;
            var endLine = Math.Min(nextStart, declaration.StartLine + MaxChunkLines - 1);
            var text = JoinLines(lines, declaration.StartLine, endLine);
            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            chunks.Add(new SemanticCodeChunk(
                path,
                text,
                declaration.StartLine,
                endLine,
                declaration.ClassName,
                declaration.MethodName));
        }

        return chunks.Count > 0
            ? chunks
            : ChunkByLineRange(path, lines);
    }

    private static IReadOnlyCollection<SemanticCodeChunk> ChunkByLineRange(string path, string[] lines)
    {
        var chunks = new List<SemanticCodeChunk>();
        for (var lineIndex = 0; lineIndex < lines.Length; lineIndex += MaxChunkLines)
        {
            var startLine = lineIndex + 1;
            var endLine = Math.Min(lines.Length, lineIndex + MaxChunkLines);
            var text = JoinLines(lines, startLine, endLine);
            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            chunks.Add(new SemanticCodeChunk(path, text, startLine, endLine));
        }

        return chunks;
    }

    private static List<Declaration> FindDeclarations(string[] lines)
    {
        var declarations = new List<Declaration>();
        string? currentClassName = null;

        for (var lineIndex = 0; lineIndex < lines.Length; lineIndex++)
        {
            var line = lines[lineIndex];

            var classMatch = ClassPattern.Match(line);
            if (classMatch.Success)
            {
                currentClassName = classMatch.Groups["name"].Value;
                declarations.Add(new Declaration(lineIndex + 1, currentClassName, null));
                continue;
            }

            var methodName = TryGetMethodName(line);
            if (!string.IsNullOrWhiteSpace(methodName))
            {
                declarations.Add(new Declaration(lineIndex + 1, currentClassName, methodName));
            }
        }

        return declarations;
    }

    private static string? TryGetMethodName(string line)
    {
        foreach (var pattern in new[] { MethodPattern, FunctionPattern, ArrowFunctionPattern })
        {
            var match = pattern.Match(line);
            if (match.Success)
            {
                return match.Groups["name"].Value;
            }
        }

        return null;
    }

    private static string JoinLines(string[] lines, int startLine, int endLine)
    {
        return string.Join(
            Environment.NewLine,
            lines.Skip(startLine - 1).Take(endLine - startLine + 1)).Trim();
    }

    private sealed record Declaration(
        int StartLine,
        string? ClassName,
        string? MethodName);
}

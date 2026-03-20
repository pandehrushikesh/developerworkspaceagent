using System.Text;

namespace ProjectLens.Infrastructure.Tools;

internal static class TextFileDetector
{
    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".cs",
        ".csproj",
        ".config",
        ".csv",
        ".fs",
        ".go",
        ".h",
        ".hpp",
        ".java",
        ".js",
        ".editorconfig",
        ".gitignore",
        ".json",
        ".jsx",
        ".kt",
        ".log",
        ".md",
        ".py",
        ".props",
        ".rs",
        ".sln",
        ".sql",
        ".ts",
        ".tsx",
        ".targets",
        ".txt",
        ".vb",
        ".xml",
        ".yaml",
        ".yml"
    };

    private static readonly HashSet<string> AllowedFileNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ".dockerignore",
        ".editorconfig",
        ".gitattributes",
        ".gitignore",
        "dockerfile",
        "license",
        "readme"
    };

    public static bool IsTextFile(string filePath)
    {
        var extension = Path.GetExtension(filePath);
        if (AllowedExtensions.Contains(extension))
        {
            return true;
        }

        var fileName = Path.GetFileName(filePath);
        if (AllowedFileNames.Contains(fileName))
        {
            return true;
        }

        Span<byte> buffer = stackalloc byte[512];
        using var stream = File.OpenRead(filePath);
        var bytesRead = stream.Read(buffer);
        var slice = buffer[..bytesRead];

        if (slice.IndexOf((byte)0) >= 0)
        {
            return false;
        }

        try
        {
            _ = new UTF8Encoding(false, true).GetString(slice);
            return true;
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
    }
}

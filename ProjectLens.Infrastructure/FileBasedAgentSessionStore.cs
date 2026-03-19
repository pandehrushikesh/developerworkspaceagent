using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ProjectLens.Domain;

namespace ProjectLens.Infrastructure;

public sealed class FileBasedAgentSessionStore : IAgentSessionStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly string _sessionsDirectoryPath;

    public FileBasedAgentSessionStore(string rootDirectoryPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectoryPath);

        _sessionsDirectoryPath = Path.Combine(rootDirectoryPath, ".sessions");
        Directory.CreateDirectory(_sessionsDirectoryPath);
    }

    public async Task<AgentSessionState?> GetAsync(
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        cancellationToken.ThrowIfCancellationRequested();

        var sessionFilePath = GetSessionFilePath(sessionId);
        if (!File.Exists(sessionFilePath))
        {
            return null;
        }

        await using var stream = new FileStream(
            sessionFilePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 4096,
            useAsync: true);

        var sessionState = await JsonSerializer.DeserializeAsync<AgentSessionState>(
            stream,
            SerializerOptions,
            cancellationToken);

        return sessionState is null
            ? null
            : Normalize(sessionState, sessionState.CreatedAtUtc, sessionState.UpdatedAtUtc);
    }

    public async Task SaveAsync(
        AgentSessionState sessionState,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sessionState);
        cancellationToken.ThrowIfCancellationRequested();

        Directory.CreateDirectory(_sessionsDirectoryPath);

        var sessionFilePath = GetSessionFilePath(sessionState.SessionId);
        var tempFilePath = $"{sessionFilePath}.{Guid.NewGuid():N}.tmp";
        var now = DateTimeOffset.UtcNow;
        var existingState = await GetAsync(sessionState.SessionId, cancellationToken);
        var createdAtUtc = existingState?.CreatedAtUtc
            ?? (sessionState.CreatedAtUtc == default ? now : sessionState.CreatedAtUtc);
        var normalizedState = Normalize(sessionState, createdAtUtc, now);

        try
        {
            await using (var stream = new FileStream(
                tempFilePath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4096,
                useAsync: true))
            {
                await JsonSerializer.SerializeAsync(stream, normalizedState, SerializerOptions, cancellationToken);
            }

            ReplaceFile(tempFilePath, sessionFilePath);
        }
        finally
        {
            if (File.Exists(tempFilePath))
            {
                File.Delete(tempFilePath);
            }
        }
    }

    private string GetSessionFilePath(string sessionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        var safeFileName = BuildSafeFileName(sessionId);
        return Path.Combine(_sessionsDirectoryPath, safeFileName);
    }

    private static string BuildSafeFileName(string sessionId)
    {
        var normalizedId = sessionId.Trim();
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalizedId)));

        var slugBuilder = new StringBuilder(normalizedId.Length);
        foreach (var character in normalizedId)
        {
            if (char.IsLetterOrDigit(character) || character is '-' or '_')
            {
                slugBuilder.Append(char.ToLowerInvariant(character));
            }
            else
            {
                slugBuilder.Append('_');
            }
        }

        var slug = slugBuilder.ToString().Trim('_');
        if (string.IsNullOrWhiteSpace(slug))
        {
            slug = "session";
        }

        const int maxSlugLength = 80;
        if (slug.Length > maxSlugLength)
        {
            slug = slug[..maxSlugLength];
        }

        return $"{slug}-{hash[..12]}.json";
    }

    private static AgentSessionState Normalize(
        AgentSessionState sessionState,
        DateTimeOffset createdAtUtc,
        DateTimeOffset updatedAtUtc)
    {
        return sessionState with
        {
            CreatedAtUtc = createdAtUtc,
            UpdatedAtUtc = updatedAtUtc,
            VisitedFiles = sessionState.VisitedFiles.ToArray(),
            RecentToolHistory = sessionState.RecentToolHistory.ToArray()
        };
    }

    private static void ReplaceFile(string tempFilePath, string destinationFilePath)
    {
        if (OperatingSystem.IsWindows() && File.Exists(destinationFilePath))
        {
            File.Replace(tempFilePath, destinationFilePath, destinationBackupFileName: null, ignoreMetadataErrors: true);
            return;
        }

        File.Move(tempFilePath, destinationFilePath, overwrite: true);
    }
}

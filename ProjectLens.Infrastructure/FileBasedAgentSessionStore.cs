using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Collections.Concurrent;
using ProjectLens.Domain;

namespace ProjectLens.Infrastructure;

public sealed class FileBasedAgentSessionStore : IAgentSessionStore
{
    private const int ReplaceRetryCount = 3;
    private static readonly TimeSpan ReplaceRetryDelay = TimeSpan.FromMilliseconds(40);
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> SessionWriteLocks =
        new(StringComparer.OrdinalIgnoreCase);

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
            FileShare.ReadWrite | FileShare.Delete,
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
        var sessionWriteLock = SessionWriteLocks.GetOrAdd(
            sessionFilePath,
            static _ => new SemaphoreSlim(1, 1));

        await sessionWriteLock.WaitAsync(cancellationToken);
        try
        {
            await SaveCoreAsync(sessionState, sessionFilePath, cancellationToken);
        }
        finally
        {
            sessionWriteLock.Release();
        }
    }

    private async Task SaveCoreAsync(
        AgentSessionState sessionState,
        string sessionFilePath,
        CancellationToken cancellationToken)
    {
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

            await ReplaceFileWithRetryAsync(tempFilePath, sessionFilePath, cancellationToken);
        }
        finally
        {
            await DeleteFileIfExistsWithRetryAsync(tempFilePath, cancellationToken);
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

    private static async Task ReplaceFileWithRetryAsync(
        string tempFilePath,
        string destinationFilePath,
        CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= ReplaceRetryCount; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                if (OperatingSystem.IsWindows() && File.Exists(destinationFilePath))
                {
                    File.Replace(
                        tempFilePath,
                        destinationFilePath,
                        destinationBackupFileName: null,
                        ignoreMetadataErrors: true);
                }
                else
                {
                    File.Move(tempFilePath, destinationFilePath, overwrite: true);
                }

                return;
            }
            catch (Exception exception) when (
                attempt < ReplaceRetryCount &&
                exception is IOException or UnauthorizedAccessException)
            {
                await Task.Delay(ReplaceRetryDelay, cancellationToken);
            }
        }
    }

    private static async Task DeleteFileIfExistsWithRetryAsync(
        string filePath,
        CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= ReplaceRetryCount; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                }

                return;
            }
            catch (Exception exception) when (
                attempt < ReplaceRetryCount &&
                exception is IOException or UnauthorizedAccessException)
            {
                await Task.Delay(ReplaceRetryDelay, cancellationToken);
            }
        }
    }
}

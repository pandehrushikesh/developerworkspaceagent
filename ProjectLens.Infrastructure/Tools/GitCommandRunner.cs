using System.Diagnostics;
using System.Text;

namespace ProjectLens.Infrastructure.Tools;

internal sealed class GitCommandRunner
{
    private readonly string _workspaceRoot;

    public GitCommandRunner(string workspaceRoot)
    {
        _workspaceRoot = workspaceRoot;
    }

    public bool IsGitRepository()
    {
        var gitDir = Path.Combine(_workspaceRoot, ".git");
        if (Directory.Exists(gitDir) || File.Exists(gitDir))
        {
            return true;
        }

        // Walk up to check if we're inside a git repo
        var current = Directory.GetParent(_workspaceRoot);
        while (current is not null)
        {
            if (Directory.Exists(Path.Combine(current.FullName, ".git")))
            {
                return true;
            }

            current = current.Parent;
        }

        return false;
    }

    public async Task<GitCommandResult> RunAsync(
        IReadOnlyList<string> args,
        CancellationToken cancellationToken = default)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = _workspaceRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        foreach (var arg in args)
        {
            startInfo.ArgumentList.Add(arg);
        }

        using var process = new Process { StartInfo = startInfo };

        var stdoutBuilder = new StringBuilder();
        var stderrBuilder = new StringBuilder();

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                stdoutBuilder.AppendLine(e.Data);
            }
        };

        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                stderrBuilder.AppendLine(e.Data);
            }
        };

        try
        {
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            await process.WaitForExitAsync(cancellationToken);

            var stdout = stdoutBuilder.ToString();
            var stderr = stderrBuilder.ToString();
            var exitCode = process.ExitCode;

            return exitCode == 0
                ? new GitCommandResult(true, stdout, null)
                : new GitCommandResult(false, stdout, stderr.Trim());
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            throw;
        }
        catch (Exception ex)
        {
            return new GitCommandResult(false, string.Empty, $"Failed to run git: {ex.Message}");
        }
    }
}

internal sealed record GitCommandResult(bool Success, string Output, string? Error);

using System.Diagnostics;
using System.Text;

namespace Akses.Core;

/// <summary>
/// The command-line tools Akses drives (ffmpeg, ffprobe, pandoc). The installer ships them in
/// <c>tools\</c> next to the exe (installer/fetch-tools.ps1 downloads pinned builds); a developer
/// build also finds them in the repo's <c>tools\</c> folder or on PATH.
/// </summary>
public static class Tools
{
    public static string Ffmpeg => Find("ffmpeg.exe");
    public static string Ffprobe => Find("ffprobe.exe");
    public static string Pandoc => Find("pandoc.exe");

    private static readonly Dictionary<string, string> Cache = new(StringComparer.OrdinalIgnoreCase);

    private static string Find(string exe)
    {
        lock (Cache)
        {
            if (Cache.TryGetValue(exe, out string? cached)) return cached;
            string found = Locate(exe) ?? throw new ToolMissingException(exe);
            Cache[exe] = found;
            return found;
        }
    }

    private static string? Locate(string exe)
    {
        // Installed: <app>\tools\ffmpeg\ffmpeg.exe. Developer builds: walk up from bin\...\ to the
        // repo root's tools\ (same layout, filled by fetch-tools.ps1).
        string sub = exe.StartsWith("pandoc", StringComparison.OrdinalIgnoreCase) ? "pandoc" : "ffmpeg";
        var candidates = new List<string>();
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        for (int i = 0; i < 8 && dir is not null; i++, dir = dir.Parent)
            candidates.Add(Path.Combine(dir.FullName, "tools", sub, exe));

        foreach (string folder in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries))
            candidates.Add(Path.Combine(folder.Trim(), exe));

        return candidates.FirstOrDefault(File.Exists);
    }

    /// <summary>
    /// Runs a tool to completion. <paramref name="onStdoutLine"/> sees stdout line by line (ffmpeg's
    /// <c>-progress pipe:1</c>); stderr is kept and the last lines become the error message on failure.
    /// Cancelling kills the whole process tree.
    /// </summary>
    public static async Task<string> RunAsync(string exe, IEnumerable<string> args, CancellationToken ct,
        Action<string>? onStdoutLine = null, string? workingDirectory = null)
    {
        var psi = new ProcessStartInfo(exe)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            WorkingDirectory = workingDirectory ?? Path.GetDirectoryName(exe) ?? "",
        };
        foreach (string a in args) psi.ArgumentList.Add(a);

        using var process = new Process { StartInfo = psi };
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        var stdoutDone = new TaskCompletionSource();
        var stderrDone = new TaskCompletionSource();
        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is null) { stdoutDone.TrySetResult(); return; }
            if (onStdoutLine is null) stdout.AppendLine(e.Data);
            else onStdoutLine(e.Data);
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null) { stderrDone.TrySetResult(); return; }
            lock (stderr) stderr.AppendLine(e.Data);
        };

        process.Start();
        process.StandardInput.Close();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using (ct.Register(() => { try { process.Kill(entireProcessTree: true); } catch { } }))
        {
            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            await Task.WhenAll(stdoutDone.Task, stderrDone.Task).ConfigureAwait(false);
        }
        ct.ThrowIfCancellationRequested();

        if (process.ExitCode != 0)
            throw new ToolFailedException(Path.GetFileNameWithoutExtension(exe), process.ExitCode, LastLines(stderr.ToString(), 4));
        return stdout.ToString();
    }

    private static string LastLines(string text, int count) =>
        string.Join(Environment.NewLine, text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).TakeLast(count));
}

public sealed class ToolMissingException(string exe)
    : Exception($"{exe} not found: reinstall Akses.")
{
    public string Exe { get; } = exe;
}

public sealed class ToolFailedException(string tool, int exitCode, string details)
    : Exception(string.IsNullOrWhiteSpace(details) ? $"{tool} exited with code {exitCode}" : details)
{
    public string Tool { get; } = tool;
}

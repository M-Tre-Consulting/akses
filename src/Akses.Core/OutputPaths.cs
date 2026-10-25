namespace Akses.Core;

/// <summary>Where results go: never over an existing file, always a fresh name like "foto (1).png".</summary>
public static class OutputPaths
{
    /// <summary>
    /// A path in <paramref name="folder"/> for <paramref name="baseName"/> + <paramref name="suffix"/> with
    /// extension <paramref name="extension"/> that doesn't exist yet. <paramref name="suffix"/> is e.g.
    /// " (tagliato)", already localized by the caller; empty for plain conversions.
    /// </summary>
    public static string Unique(string folder, string baseName, string extension, string suffix = "")
    {
        extension = extension.TrimStart('.');
        lock (Handed)
        {
            string candidate = Path.Combine(folder, $"{baseName}{suffix}.{extension}");
            for (int i = 1; Taken(candidate); i++)
                candidate = Path.Combine(folder, $"{baseName}{suffix} ({i}).{extension}");
            Handed.Add(candidate);
            return candidate;
        }
    }

    /// <summary>
    /// Every name handed out in this session. Jobs run side by side and only write their file at the
    /// end: without this, "lettera.docx" and "lettera.html" → PDF would both get "lettera.pdf" and the
    /// second would overwrite the first.
    /// </summary>
    private static readonly HashSet<string> Handed = new(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

    private static bool Taken(string path) => Handed.Contains(path) || File.Exists(path) || Directory.Exists(path);

    /// <summary>A folder name in <paramref name="parent"/> that doesn't exist yet (for multi-file results such as PDF pages).</summary>
    public static string UniqueFolder(string parent, string name)
    {
        lock (Handed)
        {
            string candidate = Path.Combine(parent, name);
            for (int i = 1; Taken(candidate); i++)
                candidate = Path.Combine(parent, $"{name} ({i})");
            Handed.Add(candidate);
            return candidate;
        }
    }

    /// <summary>
    /// The folder results of <paramref name="input"/> go to: next to the original, or the fixed folder the
    /// user chose. Falls back to Documents\Akses when the original's folder is read-only (a CD, a
    /// protected system folder, a phone mounted read-only).
    /// </summary>
    public static string FolderFor(string input, string? fixedFolder)
    {
        if (!string.IsNullOrWhiteSpace(fixedFolder))
        {
            Directory.CreateDirectory(fixedFolder);
            return fixedFolder;
        }
        string folder = Path.GetDirectoryName(Path.GetFullPath(input)) ?? Environment.CurrentDirectory;
        if (IsWritable(folder)) return folder;
        string fallback = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Akses");
        Directory.CreateDirectory(fallback);
        return fallback;
    }

    private static bool IsWritable(string folder)
    {
        try
        {
            string probe = Path.Combine(folder, $".akses-{Guid.NewGuid():N}.tmp");
            using (File.Create(probe, 1, FileOptions.DeleteOnClose)) { }
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Best-effort delete of a half-written result after a failure or a cancel.</summary>
    public static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
            else if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        }
        catch
        {
            // A leftover partial file is better than a crash.
        }
    }
}

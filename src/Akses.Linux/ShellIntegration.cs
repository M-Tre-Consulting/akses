using System.Text;
using Akses.Core;

namespace Akses.App;

/// <summary>
/// "Apri con Akses" in the file manager's right-click menu, on every file type Akses reads: one
/// entry, which opens the file's page (same idea as Explorer's menu in the Windows app).
///
/// Linux Mint's file manager, Nemo, takes it from a ".nemo_action" file: one per user, in
/// ~/.local/share/nemo/actions, so no administrator rights. Written the first time Akses starts
/// (the .deb can't write in the user's home), removed by the switch in Impostazioni. Every other
/// file manager lists Akses under "Apri con", from the .desktop file the .deb installs.
/// </summary>
internal static class ShellIntegration
{
    private static string ActionFile => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.Create), "nemo", "actions", "akses.nemo_action");

    /// <summary>The installed launcher when there is one, otherwise this very executable (developer builds).</summary>
    private static string ExePath => File.Exists("/usr/bin/akses") ? "/usr/bin/akses" : Environment.ProcessPath ?? "akses";

    public static bool IsRegistered() => File.Exists(ActionFile);

    /// <summary>At every start: put the entry in place unless it was turned off (and refresh it after an update).</summary>
    public static void RegisterOnFirstRun()
    {
        if (!Settings.Current.ShellMenuOff) Register();
    }

    public static void Register()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ActionFile)!);
            var sb = new StringBuilder();
            sb.Append("[Nemo Action]\n");
            sb.Append("Name=").Append(Strings.ShellOpenWith).Append('\n');
            sb.Append("Comment=").Append(Strings.HomeSubtitle).Append('\n');
            sb.Append("Exec=\"").Append(ExePath).Append("\" %F\n");
            sb.Append("Icon-Name=akses\n");
            sb.Append("Selection=notnone\n");
            sb.Append("Extensions=").Append(string.Join(';', FormatCatalog.InputExtensions)).Append(";\n");
            sb.Append("Quote=double\n");
            string text = sb.ToString();
            if (File.Exists(ActionFile) && File.ReadAllText(ActionFile) == text) return;
            File.WriteAllText(ActionFile, text);
        }
        catch (Exception ex)
        {
            AppLog.Write($"Registrazione del menu del gestore file fallita: {ex}");
        }
    }

    public static void Unregister()
    {
        try { File.Delete(ActionFile); }
        catch (Exception ex) { AppLog.Write($"Rimozione del menu del gestore file fallita: {ex}"); }
    }
}

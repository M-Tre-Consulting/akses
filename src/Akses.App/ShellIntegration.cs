using System.Runtime.InteropServices;
using Akses.Core;
using Microsoft.Win32;

namespace Akses.App;

/// <summary>
/// "Apri con Akses" in Explorer's right-click menu, on every file type Akses reads: one entry, which
/// opens the file's page with everything that can be done with it (same idea as the window).
///
/// Per user, no administrator rights: HKCU\Software\Classes\SystemFileAssociations\.ext\shell\Akses.
/// On Windows 11 it sits under "Mostra altre opzioni" (the modern menu only takes packaged apps).
/// Written by the installer (<c>Akses.exe --register-shell</c>), removed by the uninstaller, and
/// toggled in Impostazioni. Selecting several files starts Akses once per file: <see cref="Program"/>
/// gathers them all in the one window.
/// </summary>
internal static class ShellIntegration
{
    private const string AssocRoot = @"Software\Classes\SystemFileAssociations";
    private const string VerbName = "Akses";

    private static string ExePath => Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "Akses.exe");

    public static bool IsRegistered()
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey($@"{AssocRoot}\.png\shell\{VerbName}");
        return key is not null;
    }

    public static void Register()
    {
        try
        {
            string exe = ExePath;
            foreach (string ext in FormatCatalog.InputExtensions)
            {
                string verbPath = $@"{AssocRoot}\.{ext}\shell\{VerbName}";
                Registry.CurrentUser.DeleteSubKeyTree(verbPath, throwOnMissingSubKey: false);
                using RegistryKey verb = Registry.CurrentUser.CreateSubKey(verbPath);
                verb.SetValue("MUIVerb", Strings.ShellOpenWith);
                verb.SetValue("Icon", $"\"{exe}\",0");
                using RegistryKey cmd = verb.CreateSubKey("command");
                cmd.SetValue("", $"\"{exe}\" \"%1\"");
            }
            NotifyShell();
        }
        catch (Exception ex)
        {
            AppLog.Write($"Registrazione del menu di Esplora file fallita: {ex}");
        }
    }

    public static void Unregister()
    {
        try
        {
            using RegistryKey? root = Registry.CurrentUser.OpenSubKey(AssocRoot, writable: true);
            if (root is null) return;
            foreach (string ext in root.GetSubKeyNames())
            {
                using RegistryKey? shell = root.OpenSubKey($@"{ext}\shell", writable: true);
                if (shell?.GetSubKeyNames().Contains(VerbName) == true)
                    shell.DeleteSubKeyTree(VerbName, throwOnMissingSubKey: false);
            }
            NotifyShell();
        }
        catch (Exception ex)
        {
            AppLog.Write($"Rimozione del menu di Esplora file fallita: {ex}");
        }
    }

    private static void NotifyShell() => SHChangeNotify(0x08000000 /* SHCNE_ASSOCCHANGED */, 0, IntPtr.Zero, IntPtr.Zero);

    [DllImport("shell32.dll")]
    private static extern void SHChangeNotify(int eventId, uint flags, IntPtr item1, IntPtr item2);
}

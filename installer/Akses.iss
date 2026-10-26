; Inno Setup script for "Akses".
;
; Wraps the unpackaged `dotnet publish` output (+ tools\ffmpeg and tools\pandoc) into a single
; Setup.exe with a Start Menu entry, the "Apri con Akses" Explorer menu and a proper uninstaller.
; Same approach as WinPlay Mahogany: no MSIX.
;
; How to (re)build the installer, end to end:
;   powershell -File installer\build-installer.ps1
; That runs fetch-tools.ps1, `dotnet publish`, copies tools\ into the publish folder and runs
; this script through ISCC.exe, leaving Akses-Setup-<version>.exe in installer\output\.
;
; The version comes from <Version> in src\Akses.App\Akses.App.csproj: build-installer.ps1 passes
; it as /DMyAppVersion=..., so the installer, the exe's version resource and the app's
; "Privacy, licenza e contatti" screen always agree.

#define MyAppName "Akses"
#ifndef MyAppVersion
  #define MyAppVersion "0.0.0"
#endif
#define MyAppPublisher "M-Tre Consulting"
#define MyAppURL "https://mtre-consulting.it/progetti/akses"
#define MyAppExeName "Akses.exe"
#define MyPublishDir "..\src\Akses.App\bin\Release\net9.0-windows10.0.19041.0\win-x64\publish"

[Setup]
AppId={{C6CF6D18-2D16-4F60-AEFA-91014D3C2077}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL=https://mtre-consulting.it
AppSupportURL={#MyAppURL}
AppCopyright=© {#GetDateTimeString('yyyy', '', '')} {#MyAppPublisher}. GNU GPL v3.
VersionInfoVersion={#MyAppVersion}
VersionInfoCompany={#MyAppPublisher}
VersionInfoCopyright=© {#GetDateTimeString('yyyy', '', '')} {#MyAppPublisher}. GNU GPL v3.
DefaultDirName={localappdata}\Programs\Akses
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
; Per-user install, no administrator/UAC prompt: the Explorer menu is per user too (HKCU).
PrivilegesRequired=lowest
OutputDir=output
OutputBaseFilename=Akses-Setup-{#MyAppVersion}
SetupIconFile=..\src\Akses.App\Assets\AppIcon.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
LicenseFile=..\LICENSE
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
; Akses may be open while updating: close it, then reopen it after.
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "italian"; MessagesFile: "compiler:Languages\Italian.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"
Name: "french"; MessagesFile: "compiler:Languages\French.isl"
Name: "german"; MessagesFile: "compiler:Languages\German.isl"

[CustomMessages]
italian.ExplorerMenu=Aggiungi «Apri con Akses» al menu del tasto destro
english.ExplorerMenu=Add "Open with Akses" to the right-click menu
spanish.ExplorerMenu=Añadir «Abrir con Akses» al menú del clic derecho
french.ExplorerMenu=Ajouter « Ouvrir avec Akses » au menu du clic droit
german.ExplorerMenu=„Mit Akses öffnen“ zum Kontextmenü hinzufügen

[Tasks]
Name: "explorermenu"; Description: "{cm:ExplorerMenu}"
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[InstallDelete]
; An update replaces the program folder wholesale: a trimmed build or a newer ffmpeg drops files,
; and leftovers of the previous version would just take space. Only program files live in {app}
; (settings, log and the downloaded pandoc are in {localappdata}\Akses).
Type: filesandordirs; Name: "{app}\*"

[Files]
Source: "{#MyPublishDir}\*"; DestDir: "{app}"; Flags: recursesubdirs createallsubdirs ignoreversion

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
; "Apri con Akses" in Explorer (HKCU, see ShellIntegration.cs): written by the app itself so the
; menu text is in the user's language and the path is the installed exe.
Filename: "{app}\{#MyAppExeName}"; Parameters: "--register-shell"; Flags: runhidden waituntilterminated; Tasks: explorermenu
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent
; "Aggiorna e riavvia" in the app (AutoUpdate.cs) runs this installer silently with /relaunch=1: reopen Akses.
Filename: "{app}\{#MyAppExeName}"; Flags: nowait; Check: RelaunchAfterUpdate

[UninstallRun]
Filename: "{app}\{#MyAppExeName}"; Parameters: "--unregister-shell"; Flags: runhidden waituntilterminated; RunOnceId: "UnregisterShell"

[UninstallDelete]
; Settings, error log and the WebView2 profile (Settings.cs, AppLog.cs): the privacy notice
; promises that uninstalling removes everything the app stored. Converted files are the user's.
Type: filesandordirs; Name: "{localappdata}\Akses"
Type: filesandordirs; Name: "{%TEMP}\Akses"

[Code]
function RelaunchAfterUpdate: Boolean;
begin
  Result := ExpandConstant('{param:relaunch|0}') = '1';
end;

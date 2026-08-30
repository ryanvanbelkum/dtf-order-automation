; ─────────────────────────────────────────────────────────────────────────────
; Inno Setup script for DTF Order Automation
;
; Produces a self-contained, per-user installer (DTF.Setup.exe) that installs
; without admin rights — which is what lets the in-app auto-updater run it
; SILENTLY (a machine-wide install would trigger a UAC prompt and break the
; seamless update).
;
; Build steps (run on Windows):
;   1. Publish the app to .\publish (relative to this .iss):
;        dotnet publish "DTF Win\DtfOrderAutomation\DtfOrderAutomation.csproj" ^
;          -c Release -r win-x64 --self-contained true ^
;          -p:WindowsAppSDKSelfContained=true ^
;          -o installer\publish
;   2. Compile this script, passing the version:
;        ISCC.exe installer\installer.iss /DMyAppVersion=1.0.1
;   3. The installer is written to installer\Output\DTF.Setup.exe
;
; The auto-updater invokes the result as:  DTF.Setup.exe /VERYSILENT /SUPPRESSMSGBOXES /NORESTART
; ─────────────────────────────────────────────────────────────────────────────

#define MyAppName      "DTF Order Automation"
#define MyAppExeName   "DtfOrderAutomation.exe"
#define MyAppPublisher "Ryan Van Belkum"

; Version can be overridden at compile time: ISCC ... /DMyAppVersion=1.0.1
#ifndef MyAppVersion
  #define MyAppVersion "1.0.0"
#endif

[Setup]
; Keep this AppId STABLE across releases so updates replace the same install.
AppId={{7C9B2E14-3F5A-4D8B-9E21-6A0C5D3F1B88}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
VersionInfoVersion={#MyAppVersion}

; Per-user install — no admin rights, no UAC prompt (required for silent updates).
PrivilegesRequired=lowest
DefaultDirName={localappdata}\Programs\DtfOrderAutomation
DisableProgramGroupPage=yes
DisableDirPage=yes

; Overwrite the running app cleanly during an update.
CloseApplications=yes
RestartApplications=no

OutputDir=Output
OutputBaseFilename=DTF.Setup
UninstallDisplayIcon={app}\{#MyAppExeName}
WizardStyle=modern
Compression=lzma2
SolidCompression=yes
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64

[Files]
; Everything from the publish output goes into the install dir.
Source: "publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autodesktop}\{#MyAppName}";  Filename: "{app}\{#MyAppExeName}"
Name: "{userprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"

[Run]
; Offer to launch after an interactive install. Skipped during silent updates —
; the auto-updater relaunches the app itself in that case.
Filename: "{app}\{#MyAppExeName}"; Description: "Launch {#MyAppName}"; Flags: nowait postinstall skipifsilent

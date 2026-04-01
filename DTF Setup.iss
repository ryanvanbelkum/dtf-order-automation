[Setup]
AppName=DTF Order Automation
AppVersion=1.0.0
AppPublisher=Ryan Van Belkum
DefaultDirName={autopf}\DTF Order Automation
DefaultGroupName=DTF Order Automation
OutputDir=output
OutputBaseFilename=DTF.Setup
Compression=lzma
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
UninstallDisplayIcon={app}\DTF Order Automation.exe

[Files]
Source: "dist\DTF Order Automation.exe"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
; Start Menu
Name: "{group}\DTF Order Automation"; Filename: "{app}\DTF Order Automation.exe"
Name: "{group}\Uninstall DTF Order Automation"; Filename: "{uninstallexe}"
; Desktop
Name: "{autodesktop}\DTF Order Automation"; Filename: "{app}\DTF Order Automation.exe"

[Run]
; Offer to launch the app after install
Filename: "{app}\DTF Order Automation.exe"; Description: "Launch DTF Order Automation"; Flags: nowait postinstall skipifsilent

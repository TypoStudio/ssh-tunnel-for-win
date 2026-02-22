[Setup]
AppId={{B9A2C3D4-E5F6-4890-ABCD-EF1234567890}
AppName=SSH Tunnel Manager
AppVersion={#MyAppVersion}
AppPublisher=TypoStudio
AppPublisherURL=https://github.com/TypoStudio/ssh-tunnel-for-win
DefaultDirName={autopf}\SSHTunnel4Win
DefaultGroupName=SSH Tunnel Manager
UninstallDisplayIcon={app}\SSHTunnel4Win.exe
OutputDir=.
OutputBaseFilename=SSHTunnel4Win-{#MyAppVersion}-setup
Compression=lzma2
SolidCompression=yes
ArchitecturesInstallIn64BitMode=x64compatible
ArchitecturesAllowed=x64compatible
PrivilegesRequired=lowest
SetupIconFile=..\SSHTunnel4Win\Assets\app-icon.ico
WizardStyle=modern

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional options:"
Name: "startup"; Description: "Run at Windows startup"; GroupDescription: "Additional options:"

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\SSH Tunnel Manager"; Filename: "{app}\SSHTunnel4Win.exe"
Name: "{group}\Uninstall SSH Tunnel Manager"; Filename: "{uninstallexe}"
Name: "{commondesktop}\SSH Tunnel Manager"; Filename: "{app}\SSHTunnel4Win.exe"; Tasks: desktopicon

[Registry]
; sshtunnel:// URL scheme
Root: HKA; Subkey: "Software\Classes\sshtunnel"; ValueType: string; ValueName: ""; ValueData: "URL:SSH Tunnel Protocol"; Flags: uninsdeletekey
Root: HKA; Subkey: "Software\Classes\sshtunnel"; ValueType: string; ValueName: "URL Protocol"; ValueData: ""; Flags: uninsdeletekey
Root: HKA; Subkey: "Software\Classes\sshtunnel\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: """{app}\SSHTunnel4Win.exe"",0"; Flags: uninsdeletekey
Root: HKA; Subkey: "Software\Classes\sshtunnel\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\SSHTunnel4Win.exe"" ""%1"""; Flags: uninsdeletekey
; Startup (optional)
Root: HKA; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "SSHTunnel4Win"; ValueData: """{app}\SSHTunnel4Win.exe"""; Tasks: startup; Flags: uninsdeletevalue

[Run]
Filename: "{app}\SSHTunnel4Win.exe"; Description: "Launch SSH Tunnel Manager"; Flags: nowait postinstall skipifsilent

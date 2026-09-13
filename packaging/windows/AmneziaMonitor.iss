#ifndef MyAppVersion
  #define MyAppVersion "1.4.0"
#endif
#ifndef SourceDir
  #define SourceDir "..\..\artifacts\publish\compact\win-x64"
#endif
#ifndef OutputDir
  #define OutputDir "..\..\dist"
#endif

#define MyAppName "Amnezia Monitor"
#define MyAppPublisher "Amnezia Monitor"
#define MyAppURL "https://github.com/RandomUser111/AmneziaMonitor"
#define MyAppExeName "AmneziaMonitor.exe"

[Setup]
AppId={{F15AC76C-8C33-46DD-9078-8F5E3E1ED630}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}/issues
AppUpdatesURL={#MyAppURL}/releases
DefaultDirName={localappdata}\Programs\Amnezia Monitor
DefaultGroupName=Amnezia Monitor
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
OutputDir={#OutputDir}
OutputBaseFilename=AmneziaMonitor-v{#MyAppVersion}-win-x64-setup
SetupIconFile=..\..\src\AmneziaDashboard.App\Assets\amnezia-monitor-icon.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
CloseApplications=yes
RestartApplications=no
VersionInfoVersion={#MyAppVersion}.0
VersionInfoCompany={#MyAppPublisher}
VersionInfoDescription=Amnezia VPN server monitor and management utility
VersionInfoProductName={#MyAppName}
VersionInfoProductVersion={#MyAppVersion}

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\Amnezia Monitor"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\Amnezia Monitor"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,Amnezia Monitor}"; Flags: nowait postinstall skipifsilent

#define AppName "WinPods"
#ifndef AppVersion
#define AppVersion "0.0.0"
#endif
#ifndef SourceDir
#define SourceDir "..\publish\win-x64"
#endif
#ifndef OutputDir
#define OutputDir "..\dist"
#endif
#ifndef OutputBaseName
#define OutputBaseName "WinPodsSetup"
#endif

[Setup]
AppId={{2B8B86E5-A503-4CE9-9C6A-74D27F3C2A8D}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=WinPods
AppPublisherURL=https://github.com/ValentinSK0/WinPods
AppSupportURL=https://github.com/ValentinSK0/WinPods/issues
AppUpdatesURL=https://github.com/ValentinSK0/WinPods/releases
DefaultDirName={localappdata}\Programs\WinPods
DefaultGroupName=WinPods
DisableProgramGroupPage=yes
OutputDir={#OutputDir}
OutputBaseFilename={#OutputBaseName}
SetupIconFile=..\Assets\WinPods.ico
UninstallDisplayIcon={app}\WinPods.exe
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Shortcuts:"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Excludes: "*.pdb"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\WinPods"; Filename: "{app}\WinPods.exe"; WorkingDir: "{app}"; IconFilename: "{app}\WinPods.exe"
Name: "{autodesktop}\WinPods"; Filename: "{app}\WinPods.exe"; WorkingDir: "{app}"; IconFilename: "{app}\WinPods.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\WinPods.exe"; Description: "Launch WinPods"; Flags: nowait postinstall skipifsilent

#ifndef ClientDirectory
  #error ClientDirectory must point to the verified package contents.
#endif
#ifndef AppVersion
  #error AppVersion is required.
#endif
#ifndef OutputDirectory
  #error OutputDirectory is required.
#endif

[Setup]
AppId={{8A3E2147-29FA-4506-8F7D-6995D652DE31}
AppName=RMUC 2026 Simulator
AppVersion={#AppVersion}
AppPublisher=CityWithoutMe
AppPublisherURL=https://github.com/CityWithoutMe/rmuc-2026-simulator
AppSupportURL=https://github.com/CityWithoutMe/rmuc-2026-simulator/issues
DefaultDirName={localappdata}\Programs\RMUC2026Simulator
DefaultGroupName=RMUC 2026 Simulator
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
UninstallDisplayIcon={app}\RMNetwork.exe
OutputDir={#OutputDirectory}
OutputBaseFilename=RMUC2026-Simulator-{#AppVersion}-Windows-Setup
Compression=lzma2/fast
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no
InfoAfterFile={#ClientDirectory}\README.txt

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Shortcuts:"; Flags: unchecked

[Files]
Source: "{#ClientDirectory}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\RMUC 2026 Simulator\RMUC 2026 Simulator"; Filename: "{app}\RMNetwork.exe"; WorkingDir: "{app}"
Name: "{autodesktop}\RMUC 2026 Simulator"; Filename: "{app}\RMNetwork.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\RMNetwork.exe"; Description: "Launch RMUC 2026 Simulator"; Flags: nowait postinstall skipifsilent

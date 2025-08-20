[Setup]
AppName=SPT Launcher
AppVersion=2.0.0
AppPublisher=SPT Launcher Team
DefaultDirName={pf}\SPT Launcher
DefaultGroupName=SPT Launcher
AllowNoIcons=yes
OutputDir=release
OutputBaseFilename=SPT-Launcher-Setup-2.0.0
Compression=lzma
SolidCompression=yes
WizardStyle=modern

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "release\win-unpacked\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\SPT Launcher"; Filename: "{app}\SPT Launcher.exe"
Name: "{group}\{cm:UninstallProgram,SPT Launcher}"; Filename: "{uninstallexe}"
Name: "{commondesktop}\SPT Launcher"; Filename: "{app}\SPT Launcher.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\SPT Launcher.exe"; Description: "{cm:LaunchProgram,SPT Launcher}"; Flags: nowait postinstall skipifsilent

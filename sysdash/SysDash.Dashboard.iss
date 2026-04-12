[Setup]
AppName=SysDash Dashboard
AppVersion=1.0.0
AppPublisher=SysDash
DefaultDirName={autopf}\SysDash Dashboard
DefaultGroupName=SysDash
OutputBaseFilename=SysDash-Dashboard-Setup
OutputDir=.\Installer
Compression=lzma2
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayIcon={app}\SysDash.NetCore.exe

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
Source: "SysDash.NetCore\bin\Release\net10.0\win-x64\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "hosts.db"; DestDir: "{app}"; Flags: ignoreversion onlyifdoesntexist

[Icons]
Name: "{group}\SysDash Dashboard"; Filename: "{app}\SysDash.NetCore.exe"; Parameters: "--urls http://0.0.0.0:5059"
Name: "{group}\{cm:UninstallProgram,SysDash Dashboard}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\SysDash Dashboard"; Filename: "{app}\SysDash.NetCore.exe"; Parameters: "--urls http://0.0.0.0:5059"; Tasks: desktopicon

[Run]
Filename: "{app}\SysDash.NetCore.exe"; Parameters: "--urls http://0.0.0.0:5059"; Description: "Launch SysDash Dashboard"; Flags: nowait postinstall skipifsilent

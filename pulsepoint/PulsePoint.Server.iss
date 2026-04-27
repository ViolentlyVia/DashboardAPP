#define AppName "PulsePoint Server"
#define AppVersion "1.0.0"
#define AppPublisher "PulsePoint"
#define AppExe "PulsePoint.Server.exe"
#define BuildDir "PulsePoint.Server\bin\Release\net10.0\win-x64\publish"

[Setup]
AppId={{C5A4B3D2-E9F8-5067-AB12-34CD5678EF01}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
DefaultDirName={autopf}\PulsePoint Server
DefaultGroupName={#AppName}
OutputDir=Installer
OutputBaseFilename=PulsePoint-Server-Setup
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
DisableProgramGroupPage=yes
PrivilegesRequired=admin
UninstallDisplayName={#AppName}
UninstallDisplayIcon={app}\{#AppExe}
MinVersion=10.0

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Files]
Source: "{#BuildDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs
; Config file — only install if not already present (don't overwrite user config)
Source: "PulsePoint.Server\appsettings.json"; DestDir: "{app}"; Flags: onlyifdoesntexist

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"

[Tasks]
Name: "installservice"; Description: "Install and start as a Windows Service (recommended)"

[Run]
; Install as Windows Service
Filename: "sc.exe"; \
  Parameters: "create PulsePointServer binPath= ""{app}\{#AppExe}"" DisplayName= ""PulsePoint Server"" start= auto"; \
  Flags: runhidden waituntilterminated; Tasks: installservice
Filename: "sc.exe"; \
  Parameters: "start PulsePointServer"; \
  Flags: runhidden waituntilterminated; Tasks: installservice
; Or just run directly if not installing as service
Filename: "{app}\{#AppExe}"; \
  Flags: nowait postinstall skipifsilent; \
  Description: "Launch PulsePoint Server"; Tasks: not installservice

[UninstallRun]
Filename: "sc.exe"; Parameters: "stop PulsePointServer";   Flags: runhidden
Filename: "sc.exe"; Parameters: "delete PulsePointServer"; Flags: runhidden
Filename: "taskkill"; Parameters: "/f /im {#AppExe}";       Flags: runhidden

[Code]
var
  ApiKeyPage: TInputQueryWizardPage;
  PortPage:   TInputQueryWizardPage;

procedure InitializeWizard();
begin
  ApiKeyPage := CreateInputQueryPage(wpSelectTasks,
    'API Key', 'Set a secret key for dashboard access',
    'Agents do not need this key. You will use it to access the web dashboard.');
  ApiKeyPage.Add('API Key (leave blank to disable auth):', False);
  ApiKeyPage.Values[0] := '';

  PortPage := CreateInputQueryPage(ApiKeyPage.ID,
    'Listen Port', 'Which port should the server listen on?',
    'The dashboard will be accessible at http://<server-ip>:<port>');
  PortPage.Add('Port:', False);
  PortPage.Values[0] := '5000';
end;

function NextButtonClick(CurPageID: Integer): Boolean;
var
  P: Integer;
begin
  Result := True;
  if CurPageID = PortPage.ID then
  begin
    P := StrToIntDef(PortPage.Values[0], 0);
    if (P < 1) or (P > 65535) then
    begin
      MsgBox('Please enter a valid port number (1-65535).', mbError, MB_OK);
      PortPage.Values[0] := '5000';
      Result := False;
    end;
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  ConfigPath, Content, Key, Port: String;
begin
  if CurStep = ssPostInstall then
  begin
    ConfigPath := ExpandConstant('{app}\appsettings.json');
    Key  := Trim(ApiKeyPage.Values[0]);
    Port := Trim(PortPage.Values[0]);
    if Port = '' then Port := '5000';

    Content :=
      '{' + #13#10 +
      '  "Logging": { "LogLevel": { "Default": "Information", "Microsoft.AspNetCore": "Warning" } },' + #13#10 +
      '  "AllowedHosts": "*",' + #13#10 +
      '  "ApiKey": "' + Key + '",' + #13#10 +
      '  "DbPath": "pulsepoint.db",' + #13#10 +
      '  "Urls": "http://0.0.0.0:' + Port + '",' + #13#10 +
      '  "Services": []' + #13#10 +
      '}';

    SaveStringToFile(ConfigPath, Content, False);
  end;
end;

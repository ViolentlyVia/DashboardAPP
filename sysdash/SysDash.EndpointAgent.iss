[Setup]
AppName=SysDash Endpoint Agent
AppVersion=1.0.0
AppPublisher=SysDash
DefaultDirName={pf}\SysDash Endpoint Agent
DefaultGroupName=SysDash
OutputBaseFilename=SysDash-EndpointAgent-Setup
OutputDir=.\Installer
Compression=lzma2
SolidCompression=yes
UninstallDisplayIcon={app}\SysDash.EndpointAgent.exe
ArchitecturesInstallIn64BitMode=x64

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
Source: "SysDash.EndpointAgent\bin\Release\net10.0-windows\SysDash.EndpointAgent.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "SysDash.EndpointAgent\bin\Release\net10.0-windows\*.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "SysDash.EndpointAgent\bin\Release\net10.0-windows\SysDash.EndpointAgent.runtimeconfig.json"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\SysDash Endpoint Agent"; Filename: "{app}\SysDash.EndpointAgent.exe"
Name: "{group}\{cm:UninstallProgram,SysDash Endpoint Agent}"; Filename: "{uninstallexe}"
Name: "{commondesktop}\SysDash Endpoint Agent"; Filename: "{app}\SysDash.EndpointAgent.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\SysDash.EndpointAgent.exe"; Description: "{cm:LaunchProgram,SysDash Endpoint Agent}"; Flags: nowait postinstall skipifsilent

[Code]
var
  ServerUrlPage: TInputQueryWizardPage;
  IntervalPage: TInputQueryWizardPage;
  ReportIpPage: TInputQueryWizardPage;

procedure InitializeWizard;
begin
  ServerUrlPage := CreateInputQueryPage(wpWelcome,
    'Server Configuration', 'Enter SysDash Server Details',
    'Please enter the SysDash server URL and port:');
  ServerUrlPage.Add('Server URL:', False);
  ServerUrlPage.Values[0] := 'http://127.0.0.1:5059';

  IntervalPage := CreateInputQueryPage(ServerUrlPage.ID,
    'Report Interval', 'How often should the agent report?',
    'Report interval in seconds (minimum 5):');
  IntervalPage.Add('Interval (seconds):', False);
  IntervalPage.Values[0] := '30';

  ReportIpPage := CreateInputQueryPage(IntervalPage.ID,
    'Reported IP Address', 'Which IP to report to server?',
    'Enter "auto" for primary NIC, or a specific IPv4 address:');
  ReportIpPage.Add('Report IP:', False);
  ReportIpPage.Values[0] := 'auto';
end;

function NextButtonClick(CurPageID: Integer): Boolean;
var
  Url: string;
  Interval: string;
  IpStr: string;
begin
  Result := True;

  if CurPageID = ServerUrlPage.ID then
  begin
    Url := ServerUrlPage.Values[0];
    if (Pos('http://', Url) = 0) and (Pos('https://', Url) = 0) then
    begin
      MsgBox('Invalid URL. Use http or https with port.' + #13 + 'Example: http://192.168.1.10:5059', mbError, MB_OK);
      Result := False;
    end;
  end
  else if CurPageID = IntervalPage.ID then
  begin
    Interval := IntervalPage.Values[0];
    if StrToIntDef(Interval, 0) < 5 then
    begin
      MsgBox('Interval must be >= 5 seconds.', mbError, MB_OK);
      Result := False;
    end;
  end
  else if CurPageID = ReportIpPage.ID then
  begin
    IpStr := ReportIpPage.Values[0];
    if (IpStr <> 'auto') and (Pos('.', IpStr) = 0) then
    begin
      MsgBox('Enter "auto" or a valid IPv4 address (e.g., 192.168.1.100)', mbError, MB_OK);
      Result := False;
    end;
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  ServerUrl: string;
  Interval: string;
  ReportIp: string;
  RunKey: string;
  AgentExe: string;
  CmdLine: string;
begin
  if CurStep = ssPostInstall then
  begin
    ServerUrl := ServerUrlPage.Values[0];
    Interval := IntervalPage.Values[0];
    ReportIp := ReportIpPage.Values[0];

    RunKey := 'Software\Microsoft\Windows\CurrentVersion\Run';
    AgentExe := ExpandConstant('{app}\SysDash.EndpointAgent.exe');
    
    CmdLine := '"' + AgentExe + '" --server-url "' + ServerUrl + '" --interval ' + Interval;
    
    if ReportIp <> 'auto' then
      CmdLine := CmdLine + ' --report-ip ' + ReportIp;

    RegWriteStringValue(HKEY_CURRENT_USER, RunKey, 'SysDashEndpointAgent', CmdLine);
  end;
end;

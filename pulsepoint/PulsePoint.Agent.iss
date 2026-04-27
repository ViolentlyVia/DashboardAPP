#define AppName "PulsePoint Agent"
#define AppVersion "1.0.0"
#define AppPublisher "PulsePoint"
#define AppExe "PulsePoint.Agent.exe"
#define BuildDir "PulsePoint.Agent\bin\Release\net10.0-windows\win-x64\publish"

[Setup]
AppId={{B4F3A2C1-D8E7-4F56-9A01-23BC4567DEF0}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
DefaultDirName={autopf}\PulsePoint Agent
DefaultGroupName={#AppName}
OutputDir=Installer
OutputBaseFilename=PulsePoint-Agent-Setup
SetupIconFile=
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=commandline
UninstallDisplayName={#AppName}
UninstallDisplayIcon={app}\{#AppExe}
MinVersion=10.0

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[CustomMessages]
ServerUrlLabel=PulsePoint Server URL (e.g. http://192.168.1.10:5000):
IntervalLabel=Report interval in seconds (minimum 5):
IpLabel=IP address to report (leave blank to auto-detect):

[Files]
Source: "{#BuildDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}";  Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Tasks]
Name: "desktopicon";   Description: "Create a desktop shortcut"; Flags: unchecked
Name: "startupentry";  Description: "Start automatically with Windows"

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; \
  ValueName: "PulsePointAgent"; ValueData: """{app}\{#AppExe}"""; \
  Flags: uninsdeletevalue; Tasks: startupentry

[Run]
; Seed config with wizard values, then launch in background
Filename: "{app}\{#AppExe}"; \
  Parameters: "--server-url ""{code:GetServerUrl}"" --interval {code:GetInterval} --ip ""{code:GetPreferredIp}"""; \
  Flags: nowait postinstall skipifsilent; \
  Description: "Launch PulsePoint Agent"

[UninstallRun]
Filename: "taskkill"; Parameters: "/f /im {#AppExe}"; Flags: runhidden

[Code]
var
  UrlPage:      TInputQueryWizardPage;
  IntervalPage: TInputQueryWizardPage;
  IpPage:       TInputQueryWizardPage;

procedure InitializeWizard();
begin
  UrlPage := CreateInputQueryPage(wpSelectTasks,
    'Server Connection', 'Where is your PulsePoint Server running?',
    'Enter the URL of the PulsePoint Server that this agent will report to.');
  UrlPage.Add(ExpandConstant('{cm:ServerUrlLabel}'), False);
  UrlPage.Values[0] := 'http://localhost:5000';

  IntervalPage := CreateInputQueryPage(UrlPage.ID,
    'Report Interval', 'How often should this agent report?',
    'The agent will send metrics to the server on this interval.');
  IntervalPage.Add(ExpandConstant('{cm:IntervalLabel}'), False);
  IntervalPage.Values[0] := '30';

  IpPage := CreateInputQueryPage(IntervalPage.ID,
    'Report IP', 'Which IP address should this machine report?',
    'Leave blank to auto-detect. Set a specific IP if this machine has multiple interfaces.');
  IpPage.Add(ExpandConstant('{cm:IpLabel}'), False);
  IpPage.Values[0] := '';
end;

function NextButtonClick(CurPageID: Integer): Boolean;
var
  V: Integer;
  Url: String;
begin
  Result := True;

  if CurPageID = UrlPage.ID then
  begin
    Url := Trim(UrlPage.Values[0]);
    if (Url = '') or
       ((Copy(Url,1,7) <> 'http://') and (Copy(Url,1,8) <> 'https://')) then
    begin
      MsgBox('Please enter a valid URL starting with http:// or https://',
             mbError, MB_OK);
      Result := False;
    end;
  end;

  if CurPageID = IntervalPage.ID then
  begin
    V := StrToIntDef(IntervalPage.Values[0], 0);
    if V < 5 then
    begin
      MsgBox('Interval must be at least 5 seconds.', mbError, MB_OK);
      IntervalPage.Values[0] := '30';
      Result := False;
    end;
  end;
end;

function GetServerUrl(Param: String): String;
begin
  Result := Trim(UrlPage.Values[0]);
end;

function GetInterval(Param: String): String;
begin
  Result := Trim(IntervalPage.Values[0]);
  if StrToIntDef(Result, 0) < 5 then Result := '30';
end;

function GetPreferredIp(Param: String): String;
begin
  Result := Trim(IpPage.Values[0]);
end;

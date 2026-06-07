; Corebalt POS - TILL installer (Inno Setup 6). Per-lane install on each till PC: the self-contained
; Avalonia app + shortcuts. Prompts for the store-server LAN address and the lane number and writes them
; to the till config (a stable RegisterId GUID is generated once and preserved across re-installs). The
; till holds no data, so uninstall removes the app only.
;
; Build inputs (placed by deploy/build-installers.ps1, relative to this .iss):
;   ..\..\dist\till\*              the self-contained till publish
;   scripts\provision-till.ps1    writes appsettings.json from the wizard answers

#ifndef MyAppVersion
  #define MyAppVersion "1.0.0"
#endif

[Setup]
AppId={{2B7F4D18-9A6C-4F33-B0E1-COREBALTPOSTILL}}
AppName=Corebalt POS Till
AppVersion={#MyAppVersion}
AppPublisher=Corebalt
DefaultDirName={autopf}\Corebalt POS\Till
DisableProgramGroupPage=yes
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=..\..\dist\installers
OutputBaseFilename=CorebaltPOS-Till-{#MyAppVersion}
SetupIconFile=..\..\src\Pos.Till\Assets\corebalt-icon.ico
UninstallDisplayIcon={app}\Pos.Till.exe
WizardStyle=modern
Compression=lzma2
SolidCompression=yes

[Files]
Source: "..\..\dist\till\*"; DestDir: "{app}"; Flags: recursesubdirs createallsubdirs ignoreversion
Source: "scripts\provision-till.ps1"; DestDir: "{app}\scripts"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\Corebalt POS Till"; Filename: "{app}\Pos.Till.exe"
Name: "{autodesktop}\Corebalt POS Till"; Filename: "{app}\Pos.Till.exe"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Shortcuts:"

[Run]
; Write the till config (server URL + lane). Runs on fresh AND upgrade; preserves the RegisterId.
Filename: "powershell.exe"; \
  Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\scripts\provision-till.ps1"" -InstallDir ""{app}"" -ServerUrl ""{code:GetServerUrl}"" -Lane {code:GetLane}"; \
  StatusMsg: "Configuring the till..."; Flags: runhidden
Filename: "{app}\Pos.Till.exe"; Description: "Launch Corebalt POS Till"; Flags: postinstall nowait skipifsilent

[Code]
var
  TillPage: TInputQueryWizardPage;

procedure InitializeWizard();
begin
  TillPage := CreateInputQueryPage(wpSelectDir,
    'Store server', 'Where is the store server, and which lane is this?',
    'Enter the store server''s address on the shop network and this till''s lane number.');
  TillPage.Add('Store server address (host:port):', False);
  TillPage.Add('Lane number:', False);
  TillPage.Values[0] := 'SERVER-PC:5080';
  TillPage.Values[1] := '1';
end;

function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;
  if CurPageID = TillPage.ID then
  begin
    if Trim(TillPage.Values[0]) = '' then
    begin
      MsgBox('Please enter the store server address (for example 192.168.1.10:5080).', mbError, MB_OK);
      Result := False;
    end;
  end;
end;

function GetServerUrl(Param: String): String;
begin
  Result := Trim(TillPage.Values[0]);
end;

function GetLane(Param: String): String;
begin
  Result := Trim(TillPage.Values[1]);
  if Result = '' then Result := '1';
end;

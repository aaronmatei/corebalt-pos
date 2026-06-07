; Corebalt POS - STORE SERVER installer (Inno Setup 6).
; Turnkey on-prem install for a non-technical retailer: bundles the self-contained server (part 1) AND a
; PORTABLE, isolated Postgres cluster on a dedicated port, registers both as Windows services, opens the
; LAN firewall port, and lands the operator in the web setup wizard. Upgrades preserve config/DB/backups;
; uninstall removes the apps + services but NEVER the client's data.
;
; Build inputs (placed by deploy/build-installers.ps1, relative to this .iss):
;   ..\..\dist\store-server\*        the self-contained server publish
;   pgsql\*                          portable Postgres binaries (deploy/fetch-postgres.ps1)
;   redist\vc_redist.x64.exe         (optional) MSVC runtime Postgres needs
;   scripts\provision-server.ps1     the fresh-install provisioning script

#ifndef MyAppVersion
  #define MyAppVersion "1.0.0"
#endif

[Setup]
AppId={{8E3C9A41-7C2B-4E6D-9F1A-COREBALTPOSSV}}
AppName=Corebalt POS Store Server
AppVersion={#MyAppVersion}
AppPublisher=Corebalt
DefaultDirName={autopf}\Corebalt POS\Store Server
DisableProgramGroupPage=yes
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=..\..\dist\installers
OutputBaseFilename=CorebaltPOS-StoreServer-{#MyAppVersion}
SetupIconFile=..\..\src\Pos.Api\wwwroot\favicon.ico
UninstallDisplayIcon={app}\app\Pos.Api.exe
WizardStyle=modern
; The portable Postgres tree is large - solid compression keeps the setup reasonable.
Compression=lzma2
SolidCompression=yes

[Files]
; The self-contained store server (always replaced - this is the upgradeable part).
Source: "..\..\dist\store-server\*"; DestDir: "{app}\app"; Flags: recursesubdirs createallsubdirs ignoreversion
; Portable Postgres binaries - only on a FRESH install (on upgrade the running cluster locks them).
Source: "pgsql\*"; DestDir: "{app}\pgsql"; Flags: recursesubdirs createallsubdirs ignoreversion; Check: IsFreshInstall
Source: "scripts\provision-server.ps1"; DestDir: "{app}\scripts"; Flags: ignoreversion
#ifexist "redist\vc_redist.x64.exe"
Source: "redist\vc_redist.x64.exe"; DestDir: "{tmp}"; Flags: deleteafterinstall; Check: IsFreshInstall
#endif

[Run]
#ifexist "redist\vc_redist.x64.exe"
Filename: "{tmp}\vc_redist.x64.exe"; Parameters: "/install /quiet /norestart"; StatusMsg: "Installing prerequisites..."; Check: IsFreshInstall
#endif
; FRESH: initdb the cluster, generate secrets, register+start both services, open the firewall.
Filename: "powershell.exe"; \
  Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\scripts\provision-server.ps1"" -InstallDir ""{app}"" -AppPort {code:GetAppPort} -PgPort 5544"; \
  StatusMsg: "Setting up the database and services (this can take a minute)..."; \
  Flags: runhidden; Check: IsFreshInstall
; UPGRADE: files were replaced; start the service again (it auto-migrates with a pre-migration backup).
Filename: "sc.exe"; Parameters: "start CorebaltPOS"; StatusMsg: "Restarting the store server..."; Flags: runhidden; Check: IsUpgrade
; Land the operator in the back-office / setup wizard.
Filename: "{cmd}"; Parameters: "/c start """" ""http://localhost:{code:GetAppPort}/"""; \
  Description: "Open the back-office to finish setup"; Flags: postinstall shellexec skipifsilent nowait

[UninstallRun]
; Stop + remove the store-server service.
Filename: "sc.exe"; Parameters: "stop CorebaltPOS"; Flags: runhidden runwaituntilterminated; RunOnceId: "StopApp"
Filename: "sc.exe"; Parameters: "delete CorebaltPOS"; Flags: runhidden runwaituntilterminated; RunOnceId: "DelApp"
; Stop + unregister the Postgres service (binaries still present at this point).
Filename: "sc.exe"; Parameters: "stop CorebaltPOSPostgres"; Flags: runhidden runwaituntilterminated; RunOnceId: "StopPg"
Filename: "{app}\pgsql\bin\pg_ctl.exe"; Parameters: "unregister -N CorebaltPOSPostgres"; Flags: runhidden runwaituntilterminated; RunOnceId: "UnregPg"

[Code]
var
  PortPage: TInputQueryWizardPage;

function IsFreshInstall(): Boolean;
begin
  Result := not FileExists(ExpandConstant('{app}\app\appsettings.Production.json'));
end;

function IsUpgrade(): Boolean;
begin
  Result := not IsFreshInstall();
end;

{ On upgrade, read the LAN port back from the existing config so the firewall/browser use the real value. }
function ReadConfiguredPort(): String;
var
  s, marker, digits: String;
  p, i: Integer;
begin
  Result := '5080';
  if not LoadStringFromFile(ExpandConstant('{app}\app\appsettings.Production.json'), s) then exit;
  marker := '0.0.0.0:';
  p := Pos(marker, s);
  if p = 0 then exit;
  i := p + Length(marker); digits := '';
  while (i <= Length(s)) and (s[i] >= '0') and (s[i] <= '9') do
  begin
    digits := digits + s[i];
    i := i + 1;
  end;
  if digits <> '' then Result := digits;
end;

function GetAppPort(Param: String): String;
begin
  if IsFreshInstall() then Result := PortPage.Values[0]
  else Result := ReadConfiguredPort();
end;

procedure InitializeWizard();
begin
  PortPage := CreateInputQueryPage(wpSelectDir,
    'Network', 'Which port should tills use to reach this store server?',
    'Tills on the shop LAN connect to this machine on the port below. The installer opens it in the Windows firewall.');
  PortPage.Add('Store server port:', False);
  PortPage.Values[0] := '5080';
end;

function ShouldSkipPage(PageID: Integer): Boolean;
begin
  { The port is only asked on a fresh install; on upgrade it comes from the existing config. }
  Result := (PageID = PortPage.ID) and IsUpgrade();
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  rc: Integer;
begin
  Result := '';
  if IsUpgrade() then
  begin
    { Stop the running service so its files can be replaced. }
    Exec('sc.exe', 'stop CorebaltPOS', '', SW_HIDE, ewWaitUntilTerminated, rc);
    Sleep(2000);
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usPostUninstall then
    MsgBox('Corebalt POS has been removed.' + #13#10#13#10 +
           'Your DATABASE and BACKUPS in "' + ExpandConstant('{commonappdata}\Corebalt POS') + '" were KEPT. ' +
           'Delete that folder manually only if you are sure you no longer need the data.',
           mbInformation, MB_OK);
end;

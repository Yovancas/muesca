; Puente de la instalación de Inno (0.3.9 / 0.4.0) a Velopack (docs/velopack-migracion.md).
; Lo baja y lo ejecuta NetSparkle de esas versiones, en silencio (/VERYSILENT /SP- /SUPPRESSMSGBOXES /NORESTART), cuando
; encuentra la versión nueva en releases/latest/download/appcast.xml. Sin asistente ni desinstalador propio:
;   1. Espera a que se cierre la app vieja (o la cierra).
;   2. Ejecuta, en silencio, el Setup.exe de Velopack que lleva dentro: instala en %LOCALAPPDATA%\ClaudeBar.
;      Si falla, el puente no toca nada más, vuelve a abrir la versión vieja y sale con error (NetSparkle lo reintentará).
;      Ojo: el hook de instalación de la app nueva (LegacyInnoCleanup) corre dentro de Setup.exe, así que este puede fallar
;      con la de Inno ya retirada; si su exe ya no está, se abre la app nueva en su lugar.
;   3. Quita la instalación de Inno A MANO: sus archivos, su acceso directo del menú Inicio y su entrada de «Aplicaciones».
;      NO ejecuta su desinstalador (unins000.exe): su [UninstallRun] quitaría los hooks de Claude Code (--uninstall-hooks).
;      El hook de instalación de la app nueva (LegacyInnoCleanup) ya hace lo mismo al terminar Setup.exe (y también cierra
;      la app vieja si sigue abierta y reapunta los accesos directos del paso 4); dónde estaba la instalación de Inno se lee
;      antes, por si aquel no pudo con todo.
;   4. Reapunta «Iniciar con Windows» (y el acceso directo del escritorio, si lo había) a la app nueva. Al arrancar, la app
;      pasa el acceso directo de la carpeta Inicio a la clave Run (StartupManager.RepairIfStale).
;   5. Abre la app nueva.
; La config (%APPDATA%\ClaudeBarWin) y los hooks de ~/.claude/settings.json no se tocan: valen igual para la app nueva.
; La versión llega por /DMyVersion=x.y.z y el Setup.exe por /DSetupExe=<ruta> desde scripts/release-velopack.ps1 -Bridge.
; La app ahora se llama Muesca (docs/branding.md): lo que se ve (nombre, editor, mensajes, la descripción de los accesos
; directos reapuntados y el lanzador Muesca.exe) es de Muesca; lo que identifica la instalación vieja de Inno (su AppId, su
; carpeta, su grupo y sus ClaudeBar.lnk), el mutex y el packId de Velopack siguen como estaban: si no, no se encontrarían.
#ifndef MyVersion
  #define MyVersion "0.0.0"
#endif
#ifndef SetupExe
  #define SetupExe "..\..\dist\velopack\ClaudeBar-win-Setup.exe"
#endif

[Setup]
; AppId PROPIO, no el de la instalación de Inno ({A7C3E1F2-…}, installer/ClaudeBarWin.iss): el puente ni se registra ni deja
; desinstalador; la app nueva la registra Velopack.
AppId={{199473B0-5F59-4F04-A200-510A05928B52}
AppName=Muesca
AppVersion={#MyVersion}
VersionInfoVersion={#MyVersion}
VersionInfoProductName=Muesca
VersionInfoCompany=Bekay
AppPublisher=Bekay
AppPublisherURL=https://github.com/Yovancas/muesca
CreateAppDir=no
Uninstallable=no
PrivilegesRequired=lowest
OutputDir=..\..\dist\bridge
OutputBaseFilename=ClaudeBarWin-Bridge-{#MyVersion}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
DisableWelcomePage=yes
DisableReadyPage=yes
DisableFinishedPage=yes
ShowLanguageDialog=no
LanguageDetectionMethod=uilanguage
CloseApplications=no
RestartApplications=no
; Log de Inno en %TEMP% (Setup Log *.txt): el puente corre en silencio y es lo único que queda si algo falla.
SetupLogging=yes

[Languages]
Name: "en"; MessagesFile: "compiler:Default.isl"
Name: "es"; MessagesFile: "compiler:Languages\Spanish.isl"

[CustomMessages]
en.SetupFailed=Muesca (formerly ClaudeBar) could not switch to its new installer (code %1). Nothing was changed: your current version keeps working and will try again later.%n%nDetails in %2
en.FolderInUse=Muesca (formerly ClaudeBar) could not switch to its new installer: the folder %1 already exists and is not a ClaudeBar or Muesca installation, so it was left untouched. Your current version keeps working.
es.FolderInUse=Muesca (antes ClaudeBar) no ha podido pasar a su nuevo instalador: la carpeta %1 ya existe y no es una instalación de ClaudeBar ni de Muesca, así que no se ha tocado. Tu versión actual sigue funcionando.
es.SetupFailed=Muesca (antes ClaudeBar) no ha podido pasar a su nuevo instalador (código %1). No se ha cambiado nada: tu versión actual sigue funcionando y lo volverá a intentar más tarde.%n%nDetalles en %2

[Files]
; Solo viaja Setup.exe de Velopack; se extrae a {tmp} en PrepareToInstall (dontcopy: no se instala nada con Inno).
Source: "{#SetupExe}"; DestDir: "{tmp}"; DestName: "ClaudeBarWin-Setup.exe"; Flags: dontcopy

[Code]
const
  AppMutexName = 'ClaudeBarWin_SingleInstance';   // VelopackStartup.SingleInstanceMutex
  // La instalación de Inno que se retira (AppId de installer/ClaudeBarWin.iss, sin la llave doble).
  LegacyAppId = '{A7C3E1F2-5B8D-4E9A-9C1F-3D2E4F6A8B0C}';
  // Velopack: --packId (la carpeta, interno: no cambia) y el lanzador estable de la raíz (--packTitle + .exe,
  // AppLocation.VelopackStubName: el de Muesca).
  VelopackPackId = 'ClaudeBar';
  VelopackStub = 'Muesca.exe';
  MainExe = 'ClaudeBarWin.exe';
  AutostartArg = '--autostart';                    // AppLocation.AutostartArg
  SetupExeName = 'ClaudeBarWin-Setup.exe';

var
  SetupLog: String;
  // Carpeta y grupo del menú Inicio de Inno, leídos antes de Setup.exe: su hook de instalación ya borra la clave de Inno.
  LegacyDirAtStart, LegacyGroupAtStart: String;

procedure KillRunningApp();
var
  ResultCode: Integer;
begin
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/IM ' + MainExe + ' /F', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;

// Como installer/ClaudeBarWin.iss: NetSparkle lanza el instalador y cierra la app a la vez; hasta 20 s para que salga sola.
function InitializeSetup(): Boolean;
var
  Waited: Integer;
begin
  Waited := 0;
  while CheckForMutexes(AppMutexName) and (Waited < 20000) do
  begin
    Sleep(500);
    Waited := Waited + 500;
  end;
  if CheckForMutexes(AppMutexName) then
    KillRunningApp();
  Result := True;
end;

function LegacyUninstallKey(): String;
begin
  Result := 'Software\Microsoft\Windows\CurrentVersion\Uninstall\' + LegacyAppId + '_is1';
end;

// Carpeta de la instalación de Inno: la de su entrada de «Aplicaciones»; si no, la de por defecto.
function LegacyDir(): String;
var
  Dir: String;
begin
  if LegacyDirAtStart <> '' then
  begin
    Result := LegacyDirAtStart;
    Exit;
  end;
  if not RegQueryStringValue(HKCU, LegacyUninstallKey(), 'InstallLocation', Dir) or (Dir = '') then
    Dir := ExpandConstant('{localappdata}\Programs\ClaudeBarWin');
  Result := RemoveBackslashUnlessRoot(Dir);
end;

// Carpeta del menú Inicio de Inno: la de su entrada de «Aplicaciones»; si no, la de por defecto.
function LegacyGroup(): String;
begin
  if LegacyGroupAtStart <> '' then
  begin
    Result := LegacyGroupAtStart;
    Exit;
  end;
  if not RegQueryStringValue(HKCU, LegacyUninstallKey(), 'Inno Setup: Icon Group', Result) or (Result = '') then
    Result := 'ClaudeBar';
end;

function VelopackRoot(): String;
begin
  Result := ExpandConstant('{localappdata}\') + VelopackPackId;
end;

// La app nueva: el lanzador estable de la raíz si está; si no, la de current\.
function VelopackLauncher(): String;
begin
  Result := AddBackslash(VelopackRoot()) + VelopackStub;
  if not FileExists(Result) then
    Result := AddBackslash(VelopackRoot()) + 'current\' + MainExe;
end;

// Setup.exe --silent vacía sin preguntar la carpeta donde instala si ya tiene algo. Solo se sigue si no existe, si está
// vacía o si ya es una instalación de ClaudeBar con Velopack (Update.exe y current\ClaudeBarWin.exe).
function VelopackRootIsFreeOrOurs(): Boolean;
var
  Root: String;
  FindRec: TFindRec;
begin
  Result := True;
  Root := AddBackslash(VelopackRoot());
  if not DirExists(Root) then Exit;
  if FileExists(Root + 'Update.exe') and FileExists(Root + 'current\' + MainExe) then Exit;
  if FindFirst(Root + '*', FindRec) then
  try
    repeat
      if (FindRec.Name <> '.') and (FindRec.Name <> '..') then
      begin
        Result := False;
        Exit;
      end;
    until not FindNext(FindRec);
  finally
    FindClose(FindRec);
  end;
end;

// Setup.exe de Velopack en silencio: sin preguntas, sin pantalla de carga y sin abrir la app (la abre el puente al final).
// Instala en %LOCALAPPDATA%\ClaudeBar, aparte de %LOCALAPPDATA%\ClaudeBarWin (el helper de los hooks y los snapshots), que
// sigue igual: los hooks de Claude Code no dejan de funcionar en ningún momento.
function RunVelopackSetup(var ResultCode: Integer): Boolean;
begin
  ExtractTemporaryFile(SetupExeName);
  ForceDirectories(ExtractFileDir(SetupLog));
  Result := Exec(ExpandConstant('{tmp}\') + SetupExeName, '--silent --log "' + SetupLog + '"', '', SW_HIDE,
    ewWaitUntilTerminated, ResultCode) and (ResultCode = 0);
  Log(Format('puente: Setup.exe de Velopack terminó con el código %d', [ResultCode]));
end;

procedure DeleteIfExists(const Path: String);
begin
  if FileExists(Path) and not DeleteFile(Path) then
    Log('puente: no se pudo borrar ' + Path);
end;

// Lo mismo que haría el desinstalador de Inno, sin su [UninstallRun]. Solo se borran archivos con nombre conocido y la
// carpeta si queda vacía: aunque alguien la hubiera instalado en una carpeta compartida, no se lleva nada más.
procedure RemoveLegacyInno();
var
  Dir, Group: String;
begin
  Dir := LegacyDir();
  if DirExists(Dir) then
  begin
    DeleteIfExists(AddBackslash(Dir) + MainExe);
    DeleteIfExists(AddBackslash(Dir) + 'claudebar-helper.exe');
    DeleteIfExists(AddBackslash(Dir) + 'unins000.exe');
    DeleteIfExists(AddBackslash(Dir) + 'unins000.dat');
    DeleteIfExists(AddBackslash(Dir) + 'unins000.msg');
    if not RemoveDir(Dir) then
      Log('puente: la carpeta de Inno no está vacía, se deja: ' + Dir);
  end;

  // Acceso directo del menú Inicio ({group}\ClaudeBar.lnk) y su carpeta, si queda vacía. El de Velopack va en la raíz del
  // menú (ClaudeBar.lnk), no en esta carpeta.
  Group := LegacyGroup();
  DeleteIfExists(ExpandConstant('{userprograms}\') + Group + '\ClaudeBar.lnk');
  RemoveDir(ExpandConstant('{userprograms}\') + Group);

  // La entrada de «Aplicaciones» de Inno (la de Velopack ya la ha escrito Setup.exe).
  if RegKeyExists(HKCU, LegacyUninstallKey()) and not RegDeleteKeyIncludingSubkeys(HKCU, LegacyUninstallKey()) then
    Log('puente: no se pudo quitar la entrada de Inno de «Aplicaciones»');
end;

// «Iniciar con Windows» (el acceso directo de la carpeta Inicio de la 0.4) y el opcional del escritorio apuntaban al exe de
// Inno: a la app nueva. La app hace lo mismo al arrancar (y pasa el de inicio a la clave Run, con lo que el usuario hubiera
// apagado en el Administrador de tareas); aquí se hace ya, por si no llegara a abrirse.
procedure RepointShortcuts();
var
  Launcher, Lnk: String;
begin
  Launcher := VelopackLauncher();
  Lnk := ExpandConstant('{userstartup}\ClaudeBarWin.lnk');
  if FileExists(Lnk) then
    CreateShellLink(Lnk, 'Muesca', Launcher, AutostartArg, ExtractFileDir(Launcher), '', 0, SW_SHOWNORMAL);
  Lnk := ExpandConstant('{userdesktop}\ClaudeBar.lnk');
  if FileExists(Lnk) then
    CreateShellLink(Lnk, 'Muesca', Launcher, '', ExtractFileDir(Launcher), '', 0, SW_SHOWNORMAL);
end;

procedure StartApp(const Exe: String);
var
  ResultCode: Integer;
begin
  if FileExists(Exe) then
    Exec(Exe, '', ExtractFileDir(Exe), SW_SHOWNORMAL, ewNoWait, ResultCode)
  else
    Log('puente: no está ' + Exe);
end;

// Tras un fallo de Setup.exe: la versión vieja. Pero el hook de instalación de la app nueva (LegacyInnoCleanup) corre dentro
// de Setup.exe y puede haber retirado ya la de Inno antes del fallo: si su exe ya no está, la app nueva, que sí se instaló.
procedure StartLegacyOrNewApp();
var
  Exe: String;
begin
  Exe := AddBackslash(LegacyDir()) + MainExe;
  if FileExists(Exe) then
    StartApp(Exe)
  else
  begin
    Log('puente: ya no está ' + Exe + ' (el hook de la app nueva retiró la de Inno): se abre la nueva');
    StartApp(VelopackLauncher());
  end;
end;

// Todo antes de «instalar» (el puente no instala nada con Inno): si Setup.exe falla, el texto que se devuelve aborta el
// puente con error, sin que el puente toque la instalación de Inno, y se vuelve a abrir la versión vieja (o la nueva, si el
// hook de Setup.exe ya retiró la vieja: StartLegacyOrNewApp).
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ResultCode: Integer;
begin
  Result := '';
  SetupLog := ExpandConstant('{userappdata}\ClaudeBarWin\velopack-setup.log');
  LegacyDirAtStart := LegacyDir();
  LegacyGroupAtStart := LegacyGroup();
  if not VelopackRootIsFreeOrOurs() then
  begin
    Log('puente: ' + VelopackRoot() + ' ya existe y no es una instalación de ClaudeBar: no se toca');
    Result := FmtMessage(CustomMessage('FolderInUse'), [VelopackRoot()]);
    StartApp(AddBackslash(LegacyDir()) + MainExe);
    Exit;
  end;
  if not RunVelopackSetup(ResultCode) then
  begin
    Result := FmtMessage(CustomMessage('SetupFailed'), [IntToStr(ResultCode), SetupLog]);
    StartLegacyOrNewApp();
    Exit;
  end;
  // Velopack ya está instalado: lo que queda es limpieza, y un fallo aquí (CreateShellLink lanza) no puede abortar el puente.
  try
    RemoveLegacyInno();
  except
    Log('puente: fallo retirando la instalación de Inno: ' + GetExceptionMessage);
  end;
  try
    RepointShortcuts();
  except
    Log('puente: fallo reapuntando los accesos directos: ' + GetExceptionMessage);
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
    StartApp(VelopackLauncher());
end;

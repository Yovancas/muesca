; Instalador per-user de Muesca (antes ClaudeBar for Windows), sin admin. HEREDADO: desde la 0.5 se instala con Velopack
; (scripts/release-velopack.ps1) y scripts/release.ps1 ya no publica. Los textos que se ven son de Muesca; lo que identifica
; la instalación (AppId, carpeta, grupo y accesos directos ClaudeBar.lnk) no cambia: LegacyInnoCleanup y el puente lo
; buscan por ahí para retirarla (docs/branding.md).
; La versión llega por /DMyVersion=x.y.z desde scripts/release.ps1.
; Asistente con perfiles (docs/instalador-perfiles.md): pregunta para qué lo vas a usar, recomienda ajustes según el
; equipo y, al terminar, ClaudeBarWin.exe --apply-setup aplica lo elegido a %APPDATA%\ClaudeBarWin\config.json.
; En silencioso (NetSparkle, winget) no pregunta nada ni toca la config, como siempre.
#ifndef MyVersion
  #define MyVersion "0.0.0"
#endif

[Setup]
AppId={{A7C3E1F2-5B8D-4E9A-9C1F-3D2E4F6A8B0C}
AppName=Muesca
AppVersion={#MyVersion}
VersionInfoVersion={#MyVersion}
AppPublisher=Bekay
AppPublisherURL=https://github.com/Yovancas/muesca
DefaultDirName={localappdata}\Programs\ClaudeBarWin
DefaultGroupName=ClaudeBar
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
OutputDir=..\dist
OutputBaseFilename=ClaudeBarWin-Setup-{#MyVersion}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
; La bienvenida explica lo que viene (Inno 6 la oculta por defecto).
DisableWelcomePage=no
; Idioma del asistente: el de Windows (español o inglés), sin preguntar.
ShowLanguageDialog=no
LanguageDetectionMethod=uilanguage
UsePreviousLanguage=no
; Cierra la instancia en marcha (Restart Manager). v0.3.10: se quitó AppMutex — con él, una
; instalación silenciosa (auto-update de NetSparkle, winget upgrade) con la app aún abierta se
; ABORTABA en vez de esperar/cerrarla. Ahora [Code] espera a que la app salga y, si no, la cierra.
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "en"; MessagesFile: "compiler:Default.isl"
Name: "es"; MessagesFile: "compiler:Languages\Spanish.isl"

[Messages]
en.WelcomeLabel2=This will install [name/ver] on your computer.%n%nMuesca shows your Claude Code quota in the tray and, if you want, in a notch at the top of the screen. The next pages ask what you'll use it for and recommend the settings that suit this computer; everything can be changed later.
es.WelcomeLabel2=Esto instalará [name/ver] en tu equipo.%n%nMuesca te enseña tu cuota de Claude Code en la bandeja y, si quieres, en un notch arriba de la pantalla. En las páginas siguientes te preguntamos para qué lo vas a usar y te recomendamos los ajustes que mejor le van a este equipo; todo se puede cambiar después.

[CustomMessages]
; ---- Mantener la configuración (actualización interactiva) ----
en.KeepCaption=Your current settings
es.KeepCaption=Tu configuración actual
en.KeepDescription=Muesca is already set up on this computer.
es.KeepDescription=Muesca ya está configurada en este equipo.
en.KeepSubCaption=This update only replaces the program. Untick the box to choose a profile again; a copy of your current settings is saved first.
es.KeepSubCaption=Esta actualización solo cambia el programa. Desmarca la casilla para volver a elegir un perfil; antes se guarda una copia de tu configuración.
en.KeepOption=Keep my settings (recommended)
es.KeepOption=Mantener mi configuración (recomendado)

; ---- Perfiles ----
en.ProfileCaption=What will you use Muesca for?
es.ProfileCaption=¿Para qué vas a usar Muesca?
en.ProfileDescription=Pick a starting point. You can fine-tune it on the next pages or later in the panel settings.
es.ProfileDescription=Elige un punto de partida. Puedes afinarlo en las páginas siguientes o luego en los ajustes del panel.
en.ProfileQuota=Quota only
es.ProfileQuota=Solo cuota
en.ProfileQuotaDesc=The tray icon and the panel with your 5-hour and weekly quota. No notch.
es.ProfileQuotaDesc=El icono de la bandeja y el panel con tu cuota de 5 h y semanal. Sin notch.
en.ProfileCoder=I code with Claude Code
es.ProfileCoder=Programo con Claude Code
en.ProfileCoderDesc=A notch at the top with your live sessions, alerts when Claude finishes, music and lyrics.
es.ProfileCoderDesc=Un notch arriba con tus sesiones en vivo, avisos al terminar, música y letras.
en.ProfileFull=Full notch
es.ProfileFull=Notch completo
en.ProfileFullDesc=All of the above plus lyrics in the small notch, the clock, the cat and tasks.
es.ProfileFullDesc=Todo lo anterior y además la letra en el notch pequeño, el reloj, el gato y las tareas.
en.ProfileMinimal=Minimal / gaming
es.ProfileMinimal=Minimalista / juego
en.ProfileMinimalDesc=A thin line that hides in full screen. No sounds, animations or lyrics.
es.ProfileMinimalDesc=Una raya discreta que se esconde en pantalla completa. Sin sonidos, animaciones ni letras.
en.ProfileCustom=Custom
es.ProfileCustom=Personalizado
en.ProfileCustomDesc=Choose every option yourself on the next page.
es.ProfileCustomDesc=Elige tú cada opción en la página siguiente.
en.Recommended=(recommended)
es.Recommended=(recomendado)
en.ClaudeMissing=Claude Code was not found on this computer. Muesca needs it to read your quota: install it and sign in whenever you like.
es.ClaudeMissing=No hemos encontrado Claude Code en este equipo. Muesca lo necesita para leer tu cuota: instálalo e inicia sesión cuando quieras.

; ---- Qué se activa ----
en.FeaturesCaption=What gets turned on
es.FeaturesCaption=Qué se activa
en.FeaturesDescription=Ticked with the values of «%1». Change whatever you like.
es.FeaturesDescription=Marcado con los valores de «%1». Cambia lo que quieras.
en.FeaturesHint=Music only reads what Windows is playing (nothing leaves your PC). Lyrics send title and artist to lrclib.net.
es.FeaturesHint=La música solo lee lo que suena en Windows (no sale nada del PC). Las letras envían título y artista a lrclib.net.
en.FeatNotch=Notch at the top of the screen — quota, activity and alerts
es.FeatNotch=Notch arriba de la pantalla — cuota, actividad y avisos
en.FeatMinimal=Thin line when idle — instead of the pill with the 5-hour %
es.FeatMinimal=Raya en reposo — en lugar de la pastilla con el % de 5 h
en.FeatHideFullscreen=Hide in full screen — games, video, presentations
es.FeatHideFullscreen=Ocultar en pantalla completa — juegos, vídeo, presentaciones
en.FeatSound=Sounds — a chime when Claude finishes and at 95 %
es.FeatSound=Sonidos — campanita al terminar y al 95 %
en.FeatMusic=Music — the song that's playing
es.FeatMusic=Música — la canción que suena
en.FeatLyrics=Synced lyrics — from lrclib.net
es.FeatLyrics=Letras sincronizadas — de lrclib.net
en.FeatCompactLyrics=Lyrics in the small notch — the current line instead of the title
es.FeatCompactLyrics=Letra en el notch pequeño — la línea que suena en vez del título
en.FeatClock=Clock — when idle
es.FeatClock=Reloj — en reposo
en.FeatMascot=The cat — when idle
es.FeatMascot=El gato — en reposo
en.FeatTasks=Tasks and reminders
es.FeatTasks=Tareas y recordatorios
en.FeatReduceMotion=Reduce motion — no animations in the panel or the notch
es.FeatReduceMotion=Reducir movimiento — sin animaciones en el panel ni en el notch
en.ResetRecommended=Reset to recommended
es.ResetRecommended=Restablecer recomendados

; ---- Ajustes recomendados ----
en.SettingsCaption=Recommended settings
es.SettingsCaption=Ajustes recomendados
en.SettingsDescription=Already set to what suits this computer.
es.SettingsDescription=Ya puestos con lo que mejor le va a este equipo.
en.SettingsDescriptionKeep=Your current values; «(recommended)» marks what suits this computer.
es.SettingsDescriptionKeep=Tus valores actuales; «(recomendado)» marca lo que mejor le va a este equipo.
en.Language=Language
es.Language=Idioma
en.LanguageSystem=Same as Windows (%1)
es.LanguageSystem=Como Windows (%1)
en.LanguageHint=Muesca's texts. «Same as Windows» follows the system language.
es.LanguageHint=Los textos de Muesca. «Como Windows» sigue el idioma del sistema.
en.Monitor=Notch monitor
es.Monitor=Monitor del notch
en.MonitorPrimaryItem=The main one
es.MonitorPrimaryItem=El principal
en.MonitorItem=Monitor %1 · %2×%3 · %4
es.MonitorItem=Monitor %1 · %2×%3 · %4
en.MonitorIsPrimary=main
es.MonitorIsPrimary=principal
en.MonitorLeft=left
es.MonitorLeft=izquierda
en.MonitorRight=right
es.MonitorRight=derecha
en.MonitorAbove=above
es.MonitorAbove=arriba
en.MonitorBelow=below
es.MonitorBelow=abajo
en.MonitorHint=You have %1 monitors. «The main one» follows whichever is the main monitor.
es.MonitorHint=Tienes %1 monitores. «El principal» sigue al que sea el principal.
en.Size=Notch size
es.Size=Tamaño del notch
en.SizeAuto=Auto (≈ %1 % on this screen)
es.SizeAuto=Auto (≈ %1 % en esta pantalla)
en.SizeHint=Auto fits the screen width and scaling (DPI): %1 logical px here.
es.SizeHint=Auto se adapta al ancho y la escala (DPI) de la pantalla: aquí, %1 px lógicos.
en.Open=Open the notch
es.Open=Abrir el notch
en.OpenHover=By hovering over it
es.OpenHover=Al pasar el ratón por encima
en.OpenClick=With a click
es.OpenClick=Con un clic
en.OpenHintHover=Resting the pointer on it for a moment is enough.
es.OpenHintHover=Basta con dejar el ratón un momento encima.
en.OpenHintAbove=There's a monitor right above: the pointer would cross the notch on its way up.
es.OpenHintAbove=Hay un monitor justo encima: al subir el ratón cruzarías el notch.
en.OpenHintSmall=Small screen (%1 logical px wide): opened, the notch would cover a lot.
es.OpenHintSmall=Pantalla pequeña (%1 px lógicos de ancho): abierto, el notch taparía mucho.
en.NotchOffNote=The notch is off: these settings only matter if you turn it on later.
es.NotchOffNote=El notch está apagado: esto solo cuenta si lo enciendes luego.
en.Startup=Start with Windows
es.Startup=Iniciar con Windows
en.StartupHint=Muesca waits in the tray from the moment you sign in.
es.StartupHint=Muesca te espera en la bandeja desde que entras en Windows.

; ---- Claude Code ----
en.ClaudeCaption=Claude Code
es.ClaudeCaption=Claude Code
en.ClaudeDescription=Live sessions and permissions from the notch.
es.ClaudeDescription=Sesiones en vivo y permisos desde el notch.
en.ClaudeFound=Claude Code is installed (%1).
es.ClaudeFound=Claude Code está instalado (%1).
en.LiveSessions=Turn on live sessions
es.LiveSessions=Activar las sesiones en vivo
en.LiveSessionsActive=Live sessions are already on
es.LiveSessionsActive=Las sesiones en vivo ya están activadas
en.LiveSessionsHint=Installs hooks in ~/.claude/settings.json, Claude Code's global settings (a backup is made first), so the notch and the panel see what each session is doing. You can remove them any time from the tray menu › Disable (remove hooks); uninstalling removes them too.
es.LiveSessionsHint=Instala unos hooks en ~/.claude/settings.json, la configuración global de Claude Code (antes hace una copia), para que el notch y el panel vean qué hace cada sesión. Se pueden quitar cuando quieras desde el menú de la bandeja › Desactivar (quitar hooks); al desinstalar también se quitan.
en.LiveSessionsActiveHint=Nothing to install. To turn them off: tray menu › Disable (remove hooks).
es.LiveSessionsActiveHint=No hay nada que instalar. Para quitarlas: menú de la bandeja › Desactivar (quitar hooks).
en.NotchPermissions=Answer permissions from the notch (optional)
es.NotchPermissions=Contestar los permisos desde el notch (opcional)
en.NotchPermissionsHint=When Claude Code asks for permission (run a command, edit a file…), you allow or deny it with the notch buttons. Nothing is decided without your click: if Muesca is closed or you don't answer within 25 s, Claude Code shows its usual prompt. Adds one more entry to settings.json.
es.NotchPermissionsHint=Cuando Claude Code pida permiso (ejecutar un comando, editar un archivo…), lo permites o deniegas con los botones del notch. Nada se decide sin tu clic: si Muesca está cerrada o no contestas en 25 s, sale el diálogo de siempre de Claude Code. Añade una entrada más a settings.json.
en.NotchPermissionsNeed=— needs the notch and live sessions
es.NotchPermissionsNeed=— necesita el notch y las sesiones en vivo

; ---- Resumen (página «Listo para instalar») ----
en.MemoTitle=Muesca settings:
es.MemoTitle=Configuración de Muesca:
en.MemoKeep=Your current settings are kept (nothing changes).
es.MemoKeep=Se mantiene tu configuración actual (no cambia nada).
en.MemoProfile=Profile: %1
es.MemoProfile=Perfil: %1
en.MemoNotchOn=Notch: on (%1 · size %2 · %3)
es.MemoNotchOn=Notch: sí (%1 · tamaño %2 · %3)
en.MemoNotchOff=Notch: off
es.MemoNotchOff=Notch: no
en.MemoFeatures=Turned on: %1
es.MemoFeatures=Activado: %1
en.MemoNothing=nothing else
es.MemoNothing=nada más
en.MemoLanguage=Language: %1
es.MemoLanguage=Idioma: %1
en.MemoStartup=Start with Windows: %1
es.MemoStartup=Iniciar con Windows: %1
en.MemoLive=Live sessions: %1
es.MemoLive=Sesiones en vivo: %1
en.MemoLiveInstall=turned on (hooks in ~/.claude/settings.json, with a backup)
es.MemoLiveInstall=se activan (hooks en ~/.claude/settings.json, con copia)
en.MemoLiveActive=already on
es.MemoLiveActive=ya activadas
en.MemoPermissions=Permissions from the notch: %1
es.MemoPermissions=Permisos desde el notch: %1
en.MemoBackup=A copy of your current config.json is saved first.
es.MemoBackup=Antes se guarda una copia de tu config.json actual.
en.Yes=yes
es.Yes=sí
en.No=no
es.No=no
en.ApplyingSetup=Applying your Muesca settings…
es.ApplyingSetup=Aplicando tu configuración de Muesca…
en.SetupHooksFailed=Your Muesca settings were applied, but live sessions could not be turned on: ~/.claude/settings.json could not be changed. You can turn them on later from the tray menu › Enable (install hooks).
es.SetupHooksFailed=Se ha aplicado tu configuración de Muesca, pero no se han podido activar las sesiones en vivo: no se pudo cambiar ~/.claude/settings.json. Puedes activarlas luego desde el menú de la bandeja › Activar (instalar hooks).
en.SetupFailed=Your Muesca settings could not be applied (code %1). Nothing was changed: Muesca will start with the settings it already had (or the defaults), and you can change everything later in the panel settings.%n%nDetails in %2
es.SetupFailed=No se ha podido aplicar tu configuración de Muesca (código %1). No se ha cambiado nada: Muesca arrancará con la configuración que ya tenía (o la de por defecto), y puedes cambiarlo todo luego en los ajustes del panel.%n%nDetalles en %2
en.SetupNotLaunched=Muesca could not be started to apply your settings (Windows error %1). Nothing was changed: Muesca will start with the settings it already had (or the defaults), and you can change everything later in the panel settings.
es.SetupNotLaunched=No se ha podido abrir Muesca para aplicar tu configuración (error de Windows %1). No se ha cambiado nada: Muesca arrancará con la configuración que ya tenía (o la de por defecto), y puedes cambiarlo todo luego en los ajustes del panel.
en.SetupCrashed=Something went wrong while applying your Muesca settings (code %1), and they may have been applied only in part. Check them in the panel settings when Muesca opens.%n%nDetails in %2
es.SetupCrashed=Algo ha fallado al aplicar tu configuración de Muesca (código %1) y puede que se haya aplicado solo en parte. Revísala en los ajustes del panel cuando se abra Muesca.%n%nDetalles en %2

[Files]
Source: "..\publish\ClaudeBarWin.exe"; DestDir: "{app}"; Flags: ignoreversion
; v0.4: helper nativo (hooks + statusline). La app lo copia a %LOCALAPPDATA%\ClaudeBarWin\bin al arrancar.
Source: "..\publish\claudebar-helper.exe"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\ClaudeBar"; Filename: "{app}\ClaudeBarWin.exe"
Name: "{userdesktop}\ClaudeBar"; Filename: "{app}\ClaudeBarWin.exe"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Run]
; El asistente aplica lo elegido a config.json desde [Code] (CurStepChanged, ssPostInstall), antes de esto: así se mira
; el código de salida de --apply-setup y, si falla, se avisa.
; Relanzar tras instalar (interactivo y silencioso/auto-update).
Filename: "{app}\ClaudeBarWin.exe"; Description: "{cm:LaunchProgram,Muesca}"; Flags: nowait postinstall skipifsilent runascurrentuser
Filename: "{app}\ClaudeBarWin.exe"; Flags: nowait runasoriginaluser; Check: WizardSilent

[UninstallRun]
; v0.3.10: quitar los hooks de ~/.claude/settings.json al desinstalar. Antes se quedaban puestos y cada
; evento de Claude Code seguía lanzando un powershell (~0,3-0,5 s) para siempre, sin nadie escuchando.
Filename: "{app}\ClaudeBarWin.exe"; Parameters: "--uninstall-hooks"; Flags: runhidden waituntilterminated; RunOnceId: "RemoveClaudeBarHooks"

[UninstallDelete]
; Solo limpia la carpeta de la app y su acceso directo de «Iniciar con Windows» (sin la app apuntaría a nada).
; NO toca %APPDATA%\ClaudeBarWin (config/history.db).
Type: filesandordirs; Name: "{app}"
Type: files; Name: "{userstartup}\ClaudeBarWin.lnk"

[Code]
const
  AppMutexName = 'ClaudeBarWin_SingleInstance';

  // Perfiles: espejo de Config/SetupProfiles.cs (lo que viaja en el JSON es su id, ver ProfileId).
  PQuota = 0;
  PCoder = 1;
  PFull = 2;
  PMinimal = 3;
  PCustom = 4;
  ProfileCount = 5;

  // Casillas de «Qué se activa», en el orden en que se ven (claves de AppConfig en FeatureKey).
  FNotch = 0;
  FMinimal = 1;
  FHideFullscreen = 2;
  FSound = 3;
  FMusic = 4;
  FLyrics = 5;
  FCompactLyrics = 6;
  FClock = 7;
  FMascot = 8;
  FTasks = 9;
  FReduceMotion = 10;
  FeatureCount = 11;

  LangCount = 9;   // «Como Windows» + los 8 idiomas de la app (Services/Localization.cs)
  SizeCount = 5;   // Auto, 100, 115, 130, 150 (AppConfig.NotchSizeOptions)
  // Espejo de SetupRecommendations.SmallScreenLogicalWidth.
  SmallScreenLogicalWidth = 1440;
  // SetupApply.TempFilePrefix: la app solo borra un JSON con este nombre.
  SetupJsonName = 'claudebar-setup.json';

type
  TMonRect = record
    Left, Top, Right, Bottom: Longint;
  end;
  // MONITORINFOEXW (104 bytes): szDevice es el nombre GDI (\\.\DISPLAY2) que guarda AppConfig.NotchMonitor.
  TMonitorInfoEx = record
    cbSize: DWORD;
    rcMonitor: TMonRect;
    rcWork: TMonRect;
    dwFlags: DWORD;
    szDevice: array[0..31] of Char;
  end;
  TSetupMonitor = record
    Device: String;
    Left, Top, Width, Height, Dpi: Integer;
    Primary: Boolean;
  end;

var
  // El equipo (InitializeWizard; en silencioso no se mira nada).
  Monitors: array of TSetupMonitor;
  ClaudeDir: String;
  ClaudeDetected, HooksDetected, ExistingConfig: Boolean;
  ConfigText: String;
  RecommendedProfile: Integer;
  // Perfil de la instalación anterior (clave de desinstalación), leído al empezar: en RegisterPreviousData ya no está.
  PreviousProfileId: String;

  // Páginas y controles.
  KeepPage: TInputOptionWizardPage;
  ProfilePage, FeaturesPage, SettingsPage, ClaudePage: TWizardPage;
  ProfileRadios: array of TNewRadioButton;
  ProfileDescs: array of TNewStaticText;
  ClaudeMissingText, ClaudeInfo, PermHint: TNewStaticText;
  LangLabel, LangHint, MonitorLabel, MonitorHint, SizeLabel, OpenLabel, StartupHint: TNewStaticText;
  FeatureList: TNewCheckListBox;
  FeatureInit: array of Boolean;
  PrefilledProfile: Integer;
  LangCombo, MonitorCombo, SizeCombo, OpenCombo: TNewComboBox;
  SizeHint, OpenHint, NotchOffNote: TNewStaticText;
  StartupCheck, LiveCheck, PermCheck: TNewCheckBox;
  LiveHint: TNewStaticText;
  OpenTouched: Boolean;

  // Lo que había al abrir el asistente: en una actualización solo viaja lo que cambie respecto a esto.
  InitLang, InitMonitor, InitSize, InitOpen: Integer;
  InitStartup, InitPermissions: Boolean;

  JsonOverrides: String;

function EnumDisplayMonitors(hdc, lprcClip, lpfnEnum, dwData: Longword): Longint;
  external 'EnumDisplayMonitors@user32.dll stdcall';
function GetMonitorInfo(hMonitor: Longword; var lpmi: TMonitorInfoEx): Longint;
  external 'GetMonitorInfoW@user32.dll stdcall';
function GetSystemMetrics(nIndex: Integer): Integer;
  external 'GetSystemMetrics@user32.dll stdcall';
function GetDpiForMonitor(hmonitor: Longword; dpiType: Integer; var dpiX, dpiY: Longword): Longint;
  external 'GetDpiForMonitor@shcore.dll stdcall delayload';

procedure KillRunningApp();
var
  ResultCode: Integer;
begin
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/IM ClaudeBarWin.exe /F', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;

function InitializeSetup(): Boolean;
var
  Waited: Integer;
begin
  // NetSparkle lanza el instalador y cierra la app a la vez: dar hasta 20 s a que salga sola.
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

// ---------------- JSON y config.json ----------------

function BoolJson(B: Boolean): String;
begin
  if B then Result := 'true' else Result := 'false';
end;

// Cadena JSON: comillas y barras escapadas; lo que no sea ASCII imprimible como \uXXXX (el archivo sale en ANSI).
function JsonStr(const S: String): String;
var
  I: Integer;
  C: Char;
begin
  Result := '"';
  for I := 1 to Length(S) do
  begin
    C := S[I];
    if (C = '"') or (C = '\') then
      Result := Result + '\' + C
    else if (Ord(C) < 32) or (Ord(C) > 126) then
      Result := Result + '\u' + Format('%.4x', [Ord(C)])
    else
      Result := Result + C;
  end;
  Result := Result + '"';
end;

function IsBlank(C: Char): Boolean;
begin
  Result := (C = ' ') or (C = #9) or (C = #13) or (C = #10);
end;

// Valor crudo de "Key" en config.json (true/false/número, o la cadena sin comillas); '' si no está. Lector mínimo: el
// archivo lo escribe la app (System.Text.Json) y aquí solo se leen ajustes sencillos para enseñar los valores actuales.
function CfgRaw(const Key: String): String;
var
  P, I, L: Integer;
begin
  Result := '';
  P := Pos('"' + Key + '"', ConfigText);
  if P = 0 then Exit;
  L := Length(ConfigText);
  I := P + Length(Key) + 2;
  while (I <= L) and IsBlank(ConfigText[I]) do I := I + 1;
  if (I > L) or (ConfigText[I] <> ':') then Exit;
  I := I + 1;
  while (I <= L) and IsBlank(ConfigText[I]) do I := I + 1;
  if I > L then Exit;
  if ConfigText[I] = '"' then
  begin
    I := I + 1;
    while (I <= L) and (ConfigText[I] <> '"') do
    begin
      if (ConfigText[I] = '\') and (I < L) then I := I + 1;
      Result := Result + ConfigText[I];
      I := I + 1;
    end;
  end
  else
    while (I <= L) and (Pos(ConfigText[I], ',}]') = 0) and not IsBlank(ConfigText[I]) do
    begin
      Result := Result + ConfigText[I];
      I := I + 1;
    end;
end;

function CfgBool(const Key: String; Default: Boolean): Boolean;
var
  R: String;
begin
  R := CfgRaw(Key);
  if R = 'true' then Result := True
  else if R = 'false' then Result := False
  else Result := Default;
end;

function CfgStr(const Key, Default: String): String;
begin
  if Pos('"' + Key + '"', ConfigText) = 0 then Result := Default else Result := CfgRaw(Key);
end;

// ---------------- El equipo ----------------

function MonitorEnumProc(hMonitor, hdcMonitor, lprcMonitor, dwData: Longword): Longint;
var
  Info: TMonitorInfoEx;
  I, N: Integer;
  DX, DY: Longword;
begin
  Info.cbSize := 104;
  if GetMonitorInfo(hMonitor, Info) <> 0 then
  begin
    N := GetArrayLength(Monitors);
    SetArrayLength(Monitors, N + 1);
    Monitors[N].Device := '';
    for I := 0 to 31 do
    begin
      if Info.szDevice[I] = #0 then Break;
      Monitors[N].Device := Monitors[N].Device + Info.szDevice[I];
    end;
    Monitors[N].Left := Info.rcMonitor.Left;
    Monitors[N].Top := Info.rcMonitor.Top;
    Monitors[N].Width := Info.rcMonitor.Right - Info.rcMonitor.Left;
    Monitors[N].Height := Info.rcMonitor.Bottom - Info.rcMonitor.Top;
    Monitors[N].Primary := (Info.dwFlags and 1) <> 0;
    Monitors[N].Dpi := 96;
    try
      DX := 0;
      if GetDpiForMonitor(hMonitor, 0, DX, DY) = 0 then
        if DX > 0 then Monitors[N].Dpi := DX;
    except
      // sin shcore: 96
    end;
  end;
  Result := 1;
end;

procedure DetectMonitors();
begin
  SetArrayLength(Monitors, 0);
  try
    EnumDisplayMonitors(0, 0, CreateCallback(@MonitorEnumProc), 0);
  except
    Log('asistente: no se pudieron enumerar los monitores: ' + GetExceptionMessage);
  end;
  if GetArrayLength(Monitors) = 0 then
  begin
    SetArrayLength(Monitors, 1);
    Monitors[0].Device := '';
    Monitors[0].Left := 0;
    Monitors[0].Top := 0;
    Monitors[0].Width := GetSystemMetrics(0);
    Monitors[0].Height := GetSystemMetrics(1);
    Monitors[0].Dpi := 96;
    Monitors[0].Primary := True;
  end;
end;

// Espejo de Services/ClaudePaths.ConfigDir: CLAUDE_CONFIG_DIR si existe; si no, %USERPROFILE%\.claude.
function DetectClaudeDir(): String;
begin
  Result := Trim(GetEnv('CLAUDE_CONFIG_DIR'));
  if (Result = '') or not DirExists(Result) then
    Result := AddBackslash(GetEnv('USERPROFILE')) + '.claude';
end;

// ¿Están ya nuestros hooks en settings.json? Solo para la página de Claude Code: quien decide de verdad es la app.
function DetectHooks(): Boolean;
var
  S: AnsiString;
begin
  Result := False;
  if LoadStringFromFile(AddBackslash(ClaudeDir) + 'settings.json', S) then
    Result := (Pos('claudebar-hook.ps1', S) > 0) or
              ((Pos('claudebar-helper', S) > 0) and (Pos('"UserPromptSubmit"', S) > 0));
end;

function StartupShortcut(): String;
begin
  Result := ExpandConstant('{userstartup}\ClaudeBarWin.lnk');
end;

// ---------------- Recomendaciones: espejo de SetupRecommendations ----------------

function PrimaryIndex(): Integer;
var
  I: Integer;
begin
  for I := 0 to GetArrayLength(Monitors) - 1 do
    if Monitors[I].Primary then
    begin
      Result := I;
      Exit;
    end;
  for I := 0 to GetArrayLength(Monitors) - 1 do
    if (Monitors[I].Left = 0) and (Monitors[I].Top = 0) then
    begin
      Result := I;
      Exit;
    end;
  Result := 0;
end;

function LogicalWidth(I: Integer): Integer;
begin
  if Monitors[I].Dpi > 0 then
    Result := Round(Monitors[I].Width * 96.0 / Monitors[I].Dpi)
  else
    Result := Monitors[I].Width;
end;

// Qué será «Auto» (NotchLayout.AutoScale): ≥ 3000 px lógicos → 130 %, ≥ 2400 → 115 %, si no 100 %.
function AutoSizePercent(I: Integer): Integer;
var
  W: Integer;
begin
  W := LogicalWidth(I);
  if W >= 3000 then Result := 130
  else if W >= 2400 then Result := 115
  else Result := 100;
end;

function HasMonitorAbove(I: Integer): Boolean;
var
  J, Cx: Integer;
begin
  Result := False;
  Cx := Monitors[I].Left + Monitors[I].Width div 2;
  for J := 0 to GetArrayLength(Monitors) - 1 do
    if (J <> I) and (Monitors[J].Top + Monitors[J].Height = Monitors[I].Top) and
       (Monitors[J].Left <= Cx) and (Cx < Monitors[J].Left + Monitors[J].Width) then
      Result := True;
end;

function RecommendOpenOnClick(I: Integer): Boolean;
begin
  Result := HasMonitorAbove(I) or (LogicalWidth(I) < SmallScreenLogicalWidth);
end;

// El número de \\.\DISPLAY2 (el que ve el usuario en Windows); si no tiene, su posición + 1. Como MonitorNames.
function MonitorNumber(I: Integer): Integer;
var
  S: String;
  K: Integer;
begin
  S := Monitors[I].Device;
  K := Length(S);
  while (K > 0) and (S[K] >= '0') and (S[K] <= '9') do K := K - 1;
  Result := StrToIntDef(Copy(S, K + 1, Length(S) - K), 0);
  if Result <= 0 then Result := I + 1;
end;

// Lado respecto al principal, con la misma regla que MonitorNames.Side.
function MonitorSideText(I: Integer): String;
var
  P, DX, DY: Integer;
begin
  P := PrimaryIndex();
  if (I = P) or Monitors[I].Primary then
    Result := CustomMessage('MonitorIsPrimary')
  else if Monitors[I].Left + Monitors[I].Width <= Monitors[P].Left then
    Result := CustomMessage('MonitorLeft')
  else if Monitors[I].Left >= Monitors[P].Left + Monitors[P].Width then
    Result := CustomMessage('MonitorRight')
  else if Monitors[I].Top + Monitors[I].Height <= Monitors[P].Top then
    Result := CustomMessage('MonitorAbove')
  else if Monitors[I].Top >= Monitors[P].Top + Monitors[P].Height then
    Result := CustomMessage('MonitorBelow')
  else
  begin
    DX := (Monitors[I].Left + Monitors[I].Width div 2) - (Monitors[P].Left + Monitors[P].Width div 2);
    DY := (Monitors[I].Top + Monitors[I].Height div 2) - (Monitors[P].Top + Monitors[P].Height div 2);
    if Abs(DX) >= Abs(DY) then
    begin
      if DX < 0 then Result := CustomMessage('MonitorLeft') else Result := CustomMessage('MonitorRight');
    end
    else if DY < 0 then
      Result := CustomMessage('MonitorAbove')
    else
      Result := CustomMessage('MonitorBelow');
  end;
end;

function MonitorCaption(I: Integer): String;
begin
  Result := FmtMessage(CustomMessage('MonitorItem'), [IntToStr(MonitorNumber(I)), IntToStr(Monitors[I].Width),
    IntToStr(Monitors[I].Height), MonitorSideText(I)]);
end;

// ---------------- Perfiles: espejo de SetupProfiles ----------------

function ProfileKey(P: Integer): String;
begin
  case P of
    PQuota: Result := 'Quota';
    PCoder: Result := 'Coder';
    PFull: Result := 'Full';
    PMinimal: Result := 'Minimal';
  else
    Result := 'Custom';
  end;
end;

// Id en el JSON (SetupProfiles.Id).
function ProfileId(P: Integer): String;
begin
  case P of
    PQuota: Result := 'quota';
    PCoder: Result := 'coder';
    PFull: Result := 'full';
    PMinimal: Result := 'minimal';
  else
    Result := 'custom';
  end;
end;

function ProfileFromId(const Id: String): Integer;
var
  P: Integer;
begin
  Result := -1;
  for P := 0 to ProfileCount - 1 do
    if CompareText(ProfileId(P), Id) = 0 then Result := P;
end;

function ProfileCaption(P: Integer): String;
begin
  Result := CustomMessage('Profile' + ProfileKey(P));
end;

// SetupProfiles.WantsLiveSessions.
function WantsLiveSessions(P: Integer): Boolean;
begin
  Result := (P = PCoder) or (P = PFull) or (P = PCustom);
end;

// Cambios de cada perfil sobre las casillas: copia LITERAL de SetupProfiles.Changes (sin las claves que no tienen
// casilla, como la carátula de la letra pequeña). InstallerScriptTests compara las dos: si cambias una, cambia la otra.
function ProfileTable(P: Integer): String;
begin
  case P of
    PQuota: Result := 'NotchEnabled=0';
    PCoder: Result := 'NotchEnabled=1;NotchMinimal=0;NotchSound=1;NotchMusic=1;NotchLyrics=1;NotchCompactLyrics=0;NotchClock=0;NotchMascot=0';
    PFull: Result := 'NotchEnabled=1;NotchMinimal=0;NotchSound=1;NotchMusic=1;NotchLyrics=1;NotchCompactLyrics=1;NotchClock=1;NotchMascot=1;NotchTasks=1';
    PMinimal: Result := 'NotchEnabled=1;NotchMinimal=1;NotchHideInFullscreen=1;NotchSound=0;ReduceMotion=1;NotchLyrics=0;NotchCompactLyrics=0;NotchClock=0;NotchMascot=0';
  else
    Result := '';
  end;
end;

// Valores por defecto de AppConfig para las casillas (también lo comprueba InstallerScriptTests).
function FeatureDefaultsTable(): String;
begin
  Result := 'NotchEnabled=0;NotchMinimal=0;NotchHideInFullscreen=1;NotchSound=1;NotchMusic=1;NotchLyrics=1;NotchCompactLyrics=1;NotchClock=1;NotchMascot=1;NotchTasks=1;ReduceMotion=0';
end;

// Busca «Key=0/1» en una tabla «K=V;K=V». False si la tabla no la nombra.
function TableValue(const Table, Key: String; var V: Boolean): Boolean;
var
  T: String;
  I: Integer;
begin
  T := ';' + Table + ';';
  I := Pos(';' + Key + '=', T);
  Result := I > 0;
  if Result then V := T[I + Length(Key) + 2] = '1';
end;

function FeatureKey(F: Integer): String;
begin
  case F of
    FNotch: Result := 'NotchEnabled';
    FMinimal: Result := 'NotchMinimal';
    FHideFullscreen: Result := 'NotchHideInFullscreen';
    FSound: Result := 'NotchSound';
    FMusic: Result := 'NotchMusic';
    FLyrics: Result := 'NotchLyrics';
    FCompactLyrics: Result := 'NotchCompactLyrics';
    FClock: Result := 'NotchClock';
    FMascot: Result := 'NotchMascot';
    FTasks: Result := 'NotchTasks';
  else
    Result := 'ReduceMotion';
  end;
end;

function FeatureMessage(F: Integer): String;
begin
  case F of
    FNotch: Result := 'FeatNotch';
    FMinimal: Result := 'FeatMinimal';
    FHideFullscreen: Result := 'FeatHideFullscreen';
    FSound: Result := 'FeatSound';
    FMusic: Result := 'FeatMusic';
    FLyrics: Result := 'FeatLyrics';
    FCompactLyrics: Result := 'FeatCompactLyrics';
    FClock: Result := 'FeatClock';
    FMascot: Result := 'FeatMascot';
    FTasks: Result := 'FeatTasks';
  else
    Result := 'FeatReduceMotion';
  end;
end;

function FeatureDefault(F: Integer): Boolean;
begin
  if not TableValue(FeatureDefaultsTable(), FeatureKey(F), Result) then Result := False;
end;

// ¿Fija el perfil P la casilla F? (y con qué valor)
function ProfileDefines(P, F: Integer; var V: Boolean): Boolean;
begin
  Result := TableValue(ProfileTable(P), FeatureKey(F), V);
end;

// Nombre corto de la casilla para el resumen: lo de antes de « — ».
function FeatureShort(F: Integer): String;
var
  P: Integer;
begin
  Result := CustomMessage(FeatureMessage(F));
  P := Pos(' — ', Result);
  if P > 0 then Result := Copy(Result, 1, P - 1);
end;

// ---------------- Idiomas (Services/Localization.cs) ----------------

function LangCode(I: Integer): String;
begin
  case I of
    1: Result := 'en';
    2: Result := 'es';
    3: Result := 'nl';
    4: Result := 'fr';
    5: Result := 'de';
    6: Result := 'ja';
    7: Result := 'ko';
    8: Result := 'zh-Hant';
  else
    Result := 'system';
  end;
end;

function LangName(I: Integer): String;
begin
  case I of
    2: Result := 'Español';
    3: Result := 'Nederlands';
    4: Result := 'Français';
    5: Result := 'Deutsch';
    6: Result := '日本語';
    7: Result := '한국어';
    8: Result := '繁體中文';
  else
    Result := 'English';
  end;
end;

// Idioma que usará la app con «system» (Localization.ResolveSystemCode): el de la interfaz de Windows.
function SystemLangIndex(): Integer;
begin
  case GetUILanguage and $3FF of
    $0A: Result := 2;
    $13: Result := 3;
    $0C: Result := 4;
    $07: Result := 5;
    $11: Result := 6;
    $12: Result := 7;
    $04: Result := 8;
  else
    Result := 1;
  end;
end;

function LangDisplay(I: Integer): String;
begin
  if I <= 0 then
    Result := FmtMessage(CustomMessage('LanguageSystem'), [LangName(SystemLangIndex())])
  else
    Result := LangName(I);
end;

function LangIndex(const Code: String): Integer;
var
  I: Integer;
begin
  Result := 0;
  for I := 0 to LangCount - 1 do
    if LangCode(I) = Code then Result := I;
end;

function SizeValue(I: Integer): Integer;
begin
  case I of
    1: Result := 100;
    2: Result := 115;
    3: Result := 130;
    4: Result := 150;
  else
    Result := 0;
  end;
end;

function SizeIndex(V: Integer): Integer;
var
  I: Integer;
begin
  Result := 0;
  for I := 0 to SizeCount - 1 do
    if SizeValue(I) = V then Result := I;
end;

// ---------------- Lo elegido ----------------

function KeepConfig(): Boolean;
begin
  Result := ExistingConfig and KeepPage.Values[0];
end;

function SelectedProfile(): Integer;
var
  P: Integer;
begin
  Result := RecommendedProfile;
  for P := 0 to ProfileCount - 1 do
    if ProfileRadios[P].Checked then Result := P;
end;

function FeatureOn(F: Integer): Boolean;
begin
  Result := FeatureList.Checked[F];
end;

function OfferLiveSessions(): Boolean;
begin
  Result := ClaudeDetected and WantsLiveSessions(SelectedProfile());
end;

// Índice en Monitors del monitor elegido (0 en el desplegable = seguir al principal).
function SelectedMonitorIndex(): Integer;
begin
  if MonitorCombo.ItemIndex <= 0 then Result := PrimaryIndex() else Result := MonitorCombo.ItemIndex - 1;
end;

function SelectedMonitorDevice(): String;
begin
  if MonitorCombo.ItemIndex <= 0 then Result := '' else Result := Monitors[MonitorCombo.ItemIndex - 1].Device;
end;

function MonitorIndexFor(const Device: String): Integer;
var
  I: Integer;
begin
  Result := 0;
  if Device = '' then Exit;
  for I := 0 to GetArrayLength(Monitors) - 1 do
    if CompareText(Monitors[I].Device, Device) = 0 then Result := I + 1;
end;

// Los permisos desde el notch necesitan el notch y las sesiones en vivo. Se mira al momento: PermCheck.Enabled solo se
// pone al día al entrar en la página de Claude Code, y desde ahí se puede volver atrás y apagar el notch.
function PermissionsAvailable(): Boolean;
begin
  Result := LiveCheck.Checked and FeatureOn(FNotch);
end;

function PermissionsValue(): Boolean;
begin
  Result := PermissionsAvailable() and PermCheck.Checked;
end;

// ---------------- Controles ----------------

function NewText(Page: TWizardPage; const Caption: String; X, Y, W: Integer; Hint: Boolean): TNewStaticText;
begin
  Result := TNewStaticText.Create(Page);
  Result.Parent := Page.Surface;
  Result.Left := X;
  Result.Top := Y;
  Result.Width := W;
  Result.AutoSize := False;
  Result.WordWrap := True;
  Result.Anchors := [akLeft, akTop, akRight];
  Result.Caption := Caption;
  if Hint then Result.Font.Color := clGrayText;
  Result.AdjustHeight;
end;

function NewCombo(Page: TWizardPage; X, Y, W: Integer): TNewComboBox;
begin
  Result := TNewComboBox.Create(Page);
  Result.Parent := Page.Surface;
  Result.Style := csDropDownList;
  Result.Left := X;
  Result.Top := Y;
  Result.Width := W;
  Result.Anchors := [akLeft, akTop, akRight];
end;

function NewCheck(Page: TWizardPage; const Caption: String; X, Y, W: Integer): TNewCheckBox;
begin
  Result := TNewCheckBox.Create(Page);
  Result.Parent := Page.Surface;
  Result.Left := X;
  Result.Top := Y;
  Result.Width := W;
  Result.Height := ScaleY(17);
  Result.Anchors := [akLeft, akTop, akRight];
  Result.Caption := Caption;
end;

function NewButton(Page: TWizardPage; const Caption: String): TNewButton;
begin
  Result := TNewButton.Create(Page);
  Result.Parent := Page.Surface;
  Result.Caption := Caption;
  Result.Width := ScaleX(160);
  Result.Height := ScaleY(23);
  Result.Left := Page.SurfaceWidth - Result.Width;
  Result.Top := Page.SurfaceHeight - Result.Height;
  Result.Anchors := [akRight, akBottom];
end;

// Pone (o quita) « (recomendado)» al final de un texto.
function WithRecommended(const S: String; Recommended: Boolean): String;
begin
  if Recommended then Result := S + ' ' + CustomMessage('Recommended') else Result := S;
end;

// Cambia el texto de un elemento sin perder la selección del desplegable.
procedure SetComboItem(Combo: TNewComboBox; I: Integer; const S: String);
var
  Sel: Integer;
begin
  Sel := Combo.ItemIndex;
  Combo.Items.Strings[I] := S;
  Combo.ItemIndex := Sel;
end;

// ---------------- Colocación ----------------
// Inno agranda las páginas (WizardStyle=modern) DESPUÉS de InitializeWizard: los textos con salto de línea se miden y
// se recolocan al mostrar cada página (CurPageChanged), con el ancho de verdad.

// Coloca un texto, ajusta su alto al contenido y devuelve dónde acaba.
function Flow(T: TNewStaticText; X, Y, W: Integer): Integer;
begin
  T.Left := X;
  T.Top := Y;
  T.Width := W;
  T.AdjustHeight;
  Result := T.Top + T.Height;
end;

// Fila «etiqueta | desplegable» con su explicación debajo del desplegable.
function FlowRow(L: TNewStaticText; C: TNewComboBox; H: TNewStaticText; Y, LW, CX, CW: Integer): Integer;
begin
  L.Left := 0;
  L.Top := Y + ScaleY(3);
  L.Width := LW;
  C.Left := CX;
  C.Top := Y;
  C.Width := CW;
  Result := Flow(H, CX, Y + C.Height + ScaleY(1), CW) + ScaleY(6);
end;

// Casilla con su explicación debajo, sangrada como el texto de la casilla.
function FlowCheck(C: TNewCheckBox; H: TNewStaticText; Y, W: Integer): Integer;
begin
  C.Top := Y;
  C.Width := W;
  Result := Flow(H, ScaleX(18), Y + ScaleY(18), W - ScaleX(18)) + ScaleY(6);
end;

procedure LayoutProfilePage();
var
  P, Y, W: Integer;
begin
  W := ProfilePage.SurfaceWidth;
  Y := 0;
  for P := 0 to ProfileCount - 1 do
  begin
    ProfileRadios[P].Top := Y;
    ProfileRadios[P].Width := W;
    Y := Flow(ProfileDescs[P], ScaleX(18), Y + ScaleY(18), W - ScaleX(18)) + ScaleY(6);
  end;
  if ClaudeMissingText <> nil then Flow(ClaudeMissingText, 0, Y + ScaleY(2), W);
end;

procedure LayoutSettingsPage();
var
  Y, W, LW, CX, CW: Integer;
begin
  W := SettingsPage.SurfaceWidth;
  LW := ScaleX(120);
  CX := LW + ScaleX(8);
  CW := W - CX;
  Y := FlowRow(LangLabel, LangCombo, LangHint, 0, LW, CX, CW);
  if MonitorCombo.Visible then Y := FlowRow(MonitorLabel, MonitorCombo, MonitorHint, Y, LW, CX, CW);
  Y := FlowRow(SizeLabel, SizeCombo, SizeHint, Y, LW, CX, CW);
  Y := FlowRow(OpenLabel, OpenCombo, OpenHint, Y, LW, CX, CW) + ScaleY(2);
  FlowCheck(StartupCheck, StartupHint, Y, W);
end;

procedure LayoutClaudePage();
var
  Y, W: Integer;
begin
  W := ClaudePage.SurfaceWidth;
  Y := Flow(ClaudeInfo, 0, 0, W) + ScaleY(12);
  Y := FlowCheck(LiveCheck, LiveHint, Y, W) + ScaleY(6);
  FlowCheck(PermCheck, PermHint, Y, W);
end;

// ---------------- Qué se activa ----------------

procedure UpdateFeatureStates();
var
  F: Integer;
  Notch: Boolean;
begin
  Notch := FeatureOn(FNotch);
  for F := 0 to FeatureCount - 1 do
    if (F <> FNotch) and (F <> FReduceMotion) then FeatureList.ItemEnabled[F] := Notch;
  // Las letras solo cuentan con la música, y la letra pequeña con las letras (como en Ajustes → Notch).
  FeatureList.ItemEnabled[FLyrics] := Notch and FeatureOn(FMusic);
  FeatureList.ItemEnabled[FCompactLyrics] := Notch and FeatureOn(FMusic) and FeatureOn(FLyrics);
end;

// Casillas con los valores del perfil; lo que el perfil no fija, como está (config actual o valores por defecto).
// Personalizado en una instalación nueva parte del perfil recomendado.
procedure PrefillFeatures(P: Integer);
var
  F, Src: Integer;
  V: Boolean;
begin
  Src := P;
  if (P = PCustom) and not ExistingConfig then Src := RecommendedProfile;
  for F := 0 to FeatureCount - 1 do
  begin
    if not ProfileDefines(Src, F, V) then V := CfgBool(FeatureKey(F), FeatureDefault(F));
    FeatureList.Checked[F] := V;
    FeatureInit[F] := V;
  end;
  PrefilledProfile := P;
  UpdateFeatureStates();
end;

procedure FeatureListClickCheck(Sender: TObject);
begin
  UpdateFeatureStates();
end;

procedure FeatureResetClick(Sender: TObject);
begin
  PrefillFeatures(SelectedProfile());
end;

// ---------------- Ajustes recomendados ----------------

// Textos que dependen del monitor elegido: qué será «Auto», qué apertura se recomienda y por qué.
procedure UpdateSettingsCaptions();
var
  M: Integer;
  Click: Boolean;
begin
  M := SelectedMonitorIndex();
  Click := RecommendOpenOnClick(M);
  SetComboItem(SizeCombo, 0, WithRecommended(FmtMessage(CustomMessage('SizeAuto'), [IntToStr(AutoSizePercent(M))]), True));
  SizeHint.Caption := FmtMessage(CustomMessage('SizeHint'), [IntToStr(LogicalWidth(M))]);
  SetComboItem(OpenCombo, 0, WithRecommended(CustomMessage('OpenHover'), not Click));
  SetComboItem(OpenCombo, 1, WithRecommended(CustomMessage('OpenClick'), Click));
  if HasMonitorAbove(M) then
    OpenHint.Caption := CustomMessage('OpenHintAbove')
  else if LogicalWidth(M) < SmallScreenLogicalWidth then
    OpenHint.Caption := FmtMessage(CustomMessage('OpenHintSmall'), [IntToStr(LogicalWidth(M))])
  else
    OpenHint.Caption := CustomMessage('OpenHintHover');
  // En una instalación nueva, mientras no se haya tocado, la apertura sigue a la recomendación del monitor.
  if not ExistingConfig and not OpenTouched then
    if Click then OpenCombo.ItemIndex := 1 else OpenCombo.ItemIndex := 0;
  LayoutSettingsPage();
end;

procedure UpdateSettingsEnabled();
var
  Notch: Boolean;
begin
  Notch := FeatureOn(FNotch);
  MonitorCombo.Enabled := Notch;
  SizeCombo.Enabled := Notch;
  OpenCombo.Enabled := Notch;
  NotchOffNote.Visible := not Notch;
end;

procedure ApplySettings(Lang, Monitor, Size, Open: Integer; Startup: Boolean);
begin
  LangCombo.ItemIndex := Lang;
  MonitorCombo.ItemIndex := Monitor;
  SizeCombo.ItemIndex := Size;
  OpenCombo.ItemIndex := Open;
  StartupCheck.Checked := Startup;
  UpdateSettingsCaptions();
end;

procedure SettingsResetClick(Sender: TObject);
var
  Open: Integer;
begin
  OpenTouched := False;
  if RecommendOpenOnClick(PrimaryIndex()) then Open := 1 else Open := 0;
  ApplySettings(0, 0, 0, Open, True);
end;

procedure MonitorComboChange(Sender: TObject);
begin
  UpdateSettingsCaptions();
end;

procedure OpenComboChange(Sender: TObject);
begin
  OpenTouched := True;
end;

// ---------------- Claude Code ----------------

procedure UpdateClaudePage();
begin
  PermCheck.Enabled := PermissionsAvailable();
  if PermCheck.Enabled then
    PermCheck.Caption := CustomMessage('NotchPermissions')
  else
    PermCheck.Caption := CustomMessage('NotchPermissions') + ' ' + CustomMessage('NotchPermissionsNeed');
end;

procedure LiveCheckClick(Sender: TObject);
begin
  UpdateClaudePage();
end;

// Sesiones en vivo: marcadas solo con «Programo con Claude Code», que es su razón de ser. Con los demás perfiles hay que
// marcarlas a propósito: tocan el ~/.claude/settings.json global, y desde la bandeja el mismo cambio pregunta con «No»
// por defecto. Si ya están puestas, la casilla se queda marcada y deshabilitada.
procedure PrefillLiveSessions(P: Integer);
begin
  if HooksDetected then Exit;
  LiveCheck.Checked := (P = PCoder);
  LiveCheck.Caption := WithRecommended(CustomMessage('LiveSessions'), P = PCoder);
end;

// ---------------- Páginas ----------------

procedure ProfileTextClick(Sender: TObject);
begin
  ProfileRadios[TNewStaticText(Sender).Tag].Checked := True;
end;

procedure CreateProfilePage();
var
  P, Prev: Integer;
  R: TNewRadioButton;
  D: TNewStaticText;
begin
  ProfilePage := CreateCustomPage(KeepPage.ID, CustomMessage('ProfileCaption'), CustomMessage('ProfileDescription'));
  SetArrayLength(ProfileRadios, ProfileCount);
  SetArrayLength(ProfileDescs, ProfileCount);
  for P := 0 to ProfileCount - 1 do
  begin
    R := TNewRadioButton.Create(ProfilePage);
    R.Parent := ProfilePage.Surface;
    R.Left := 0;
    R.Height := ScaleY(17);
    R.Anchors := [akLeft, akTop, akRight];
    R.Caption := WithRecommended(ProfileCaption(P), P = RecommendedProfile);
    R.Font.Style := [fsBold];
    ProfileRadios[P] := R;
    D := NewText(ProfilePage, CustomMessage('Profile' + ProfileKey(P) + 'Desc'), 0, 0, ProfilePage.SurfaceWidth, True);
    D.Tag := P;
    D.OnClick := @ProfileTextClick;
    ProfileDescs[P] := D;
  end;
  ClaudeMissingText := nil;
  if not ClaudeDetected then
    ClaudeMissingText := NewText(ProfilePage, CustomMessage('ClaudeMissing'), 0, 0, ProfilePage.SurfaceWidth, False);
  LayoutProfilePage();

  // La última elección (si la hubo) o la recomendada.
  Prev := ProfileFromId(PreviousProfileId);
  if Prev < 0 then Prev := RecommendedProfile;
  ProfileRadios[Prev].Checked := True;
end;

procedure CreateFeaturesPage();
var
  F: Integer;
  B: TNewButton;
  H: TNewStaticText;
begin
  FeaturesPage := CreateCustomPage(ProfilePage.ID, CustomMessage('FeaturesCaption'), '');
  // La lista se crea la primera: se lleva el foco al entrar y Enter sigue siendo «Siguiente».
  FeatureList := TNewCheckListBox.Create(FeaturesPage);
  FeatureList.Parent := FeaturesPage.Surface;
  FeatureList.Left := 0;
  FeatureList.Top := 0;
  FeatureList.Width := FeaturesPage.SurfaceWidth;
  FeatureList.Flat := True;
  for F := 0 to FeatureCount - 1 do
    FeatureList.AddCheckBox(CustomMessage(FeatureMessage(F)), '', 0, FeatureDefault(F), True, False, False, nil);
  FeatureList.OnClickCheck := @FeatureListClickCheck;

  B := NewButton(FeaturesPage, CustomMessage('ResetRecommended'));
  B.OnClick := @FeatureResetClick;
  H := NewText(FeaturesPage, CustomMessage('FeaturesHint'), 0, 0, B.Left - ScaleX(10), True);
  H.Top := FeaturesPage.SurfaceHeight - H.Height;
  H.Anchors := [akLeft, akRight, akBottom];
  FeatureList.Height := H.Top - ScaleY(8);
  FeatureList.Anchors := [akLeft, akTop, akRight, akBottom];
  SetArrayLength(FeatureInit, FeatureCount);
  PrefilledProfile := -1;
end;

procedure CreateSettingsPage();
var
  I, W: Integer;
  B: TNewButton;
begin
  if ExistingConfig then
    SettingsPage := CreateCustomPage(FeaturesPage.ID, CustomMessage('SettingsCaption'), CustomMessage('SettingsDescriptionKeep'))
  else
    SettingsPage := CreateCustomPage(FeaturesPage.ID, CustomMessage('SettingsCaption'), CustomMessage('SettingsDescription'));
  W := SettingsPage.SurfaceWidth;

  // Se crean en el orden de tabulación; LayoutSettingsPage los coloca.
  LangLabel := NewText(SettingsPage, CustomMessage('Language'), 0, 0, W, False);
  LangCombo := NewCombo(SettingsPage, 0, 0, W);
  LangCombo.Items.Add(WithRecommended(LangDisplay(0), True));
  for I := 1 to LangCount - 1 do LangCombo.Items.Add(LangName(I));
  LangHint := NewText(SettingsPage, CustomMessage('LanguageHint'), 0, 0, W, True);

  // Monitor (solo con 2+ monitores, como el menú de la bandeja)
  MonitorLabel := NewText(SettingsPage, CustomMessage('Monitor'), 0, 0, W, False);
  MonitorCombo := NewCombo(SettingsPage, 0, 0, W);
  MonitorCombo.Items.Add(WithRecommended(CustomMessage('MonitorPrimaryItem'), True));
  for I := 0 to GetArrayLength(Monitors) - 1 do MonitorCombo.Items.Add(MonitorCaption(I));
  MonitorCombo.OnChange := @MonitorComboChange;
  MonitorHint := NewText(SettingsPage, FmtMessage(CustomMessage('MonitorHint'), [IntToStr(GetArrayLength(Monitors))]),
    0, 0, W, True);
  if GetArrayLength(Monitors) < 2 then
  begin
    MonitorLabel.Visible := False;
    MonitorCombo.Visible := False;
    MonitorHint.Visible := False;
  end;

  // Tamaño
  SizeLabel := NewText(SettingsPage, CustomMessage('Size'), 0, 0, W, False);
  SizeCombo := NewCombo(SettingsPage, 0, 0, W);
  SizeCombo.Items.Add('Auto');
  for I := 1 to SizeCount - 1 do SizeCombo.Items.Add(IntToStr(SizeValue(I)) + ' %');
  SizeHint := NewText(SettingsPage, '', 0, 0, W, True);

  // Cómo se abre
  OpenLabel := NewText(SettingsPage, CustomMessage('Open'), 0, 0, W, False);
  OpenCombo := NewCombo(SettingsPage, 0, 0, W);
  OpenCombo.Items.Add(CustomMessage('OpenHover'));
  OpenCombo.Items.Add(CustomMessage('OpenClick'));
  OpenCombo.OnChange := @OpenComboChange;
  OpenHint := NewText(SettingsPage, '', 0, 0, W, True);

  // Inicio con Windows
  StartupCheck := NewCheck(SettingsPage, WithRecommended(CustomMessage('Startup'), True), 0, 0, W);
  StartupHint := NewText(SettingsPage, CustomMessage('StartupHint'), 0, 0, W, True);

  B := NewButton(SettingsPage, CustomMessage('ResetRecommended'));
  B.OnClick := @SettingsResetClick;
  NotchOffNote := NewText(SettingsPage, CustomMessage('NotchOffNote'), 0, B.Top, B.Left - ScaleX(10), True);
  NotchOffNote.Anchors := [akLeft, akRight, akBottom];

  // Valores de partida: en una instalación nueva, los recomendados; en una actualización, los actuales.
  if ExistingConfig then
  begin
    InitLang := LangIndex(CfgStr('Language', 'system'));
    InitMonitor := MonitorIndexFor(CfgStr('NotchMonitor', ''));
    InitSize := SizeIndex(StrToIntDef(CfgRaw('NotchSizePercent'), 0));
    if CfgBool('NotchOpenOnClick', False) then InitOpen := 1 else InitOpen := 0;
    InitStartup := FileExists(StartupShortcut());
  end
  else
  begin
    InitLang := 0;
    InitMonitor := 0;
    InitSize := 0;
    if RecommendOpenOnClick(PrimaryIndex()) then InitOpen := 1 else InitOpen := 0;
    InitStartup := True;
  end;
  ApplySettings(InitLang, InitMonitor, InitSize, InitOpen, InitStartup);
end;

procedure CreateClaudePage();
var
  W: Integer;
begin
  ClaudePage := CreateCustomPage(SettingsPage.ID, CustomMessage('ClaudeCaption'), CustomMessage('ClaudeDescription'));
  W := ClaudePage.SurfaceWidth;
  ClaudeInfo := NewText(ClaudePage, FmtMessage(CustomMessage('ClaudeFound'), [ClaudeDir]), 0, 0, W, False);

  // Lo marca el perfil al dejar su página (PrefillLiveSessions); aquí, antes de enganchar OnClick (PermCheck aún no existe).
  LiveCheck := NewCheck(ClaudePage, WithRecommended(CustomMessage('LiveSessions'), SelectedProfile() = PCoder), 0, 0, W);
  LiveCheck.Checked := HooksDetected or (SelectedProfile() = PCoder);
  LiveCheck.OnClick := @LiveCheckClick;
  LiveHint := NewText(ClaudePage, CustomMessage('LiveSessionsHint'), 0, 0, W, True);
  if HooksDetected then
  begin
    LiveCheck.Caption := CustomMessage('LiveSessionsActive');
    LiveCheck.Enabled := False;
    LiveHint.Caption := CustomMessage('LiveSessionsActiveHint');
  end;

  PermCheck := NewCheck(ClaudePage, CustomMessage('NotchPermissions'), 0, 0, W);
  PermHint := NewText(ClaudePage, CustomMessage('NotchPermissionsHint'), 0, 0, W, True);
  // Opt-in: apagado salvo que ya estuviera activo.
  InitPermissions := CfgBool('NotchPermissions', False);
  PermCheck.Checked := InitPermissions;
  LayoutClaudePage();
end;

procedure InitializeWizard();
var
  Raw: AnsiString;
begin
  // En silencioso (NetSparkle, winget) no se mira nada: las páginas se crean (Inno las recorre igual) pero se saltan.
  ExistingConfig := False;
  ConfigText := '';
  ClaudeDetected := False;
  HooksDetected := False;
  if not WizardSilent then
  begin
    try
      DetectMonitors();
      ClaudeDir := DetectClaudeDir();
      ClaudeDetected := DirExists(ClaudeDir);
      HooksDetected := ClaudeDetected and DetectHooks();
      ExistingConfig := FileExists(ExpandConstant('{userappdata}\ClaudeBarWin\config.json'));
      if ExistingConfig then
        if LoadStringFromFile(ExpandConstant('{userappdata}\ClaudeBarWin\config.json'), Raw) then
          ConfigText := String(Raw);
    except
      Log('asistente: fallo detectando el equipo: ' + GetExceptionMessage);
    end;
  end;
  if GetArrayLength(Monitors) = 0 then
  begin
    SetArrayLength(Monitors, 1);
    Monitors[0].Device := '';
    Monitors[0].Width := 1920;
    Monitors[0].Height := 1080;
    Monitors[0].Dpi := 96;
    Monitors[0].Primary := True;
  end;
  if ClaudeDetected then RecommendedProfile := PCoder else RecommendedProfile := PQuota;
  PreviousProfileId := GetPreviousData('SetupProfile', '');

  KeepPage := CreateInputOptionPage(wpWelcome, CustomMessage('KeepCaption'), CustomMessage('KeepDescription'),
    CustomMessage('KeepSubCaption'), False, False);
  KeepPage.Add(CustomMessage('KeepOption'));
  KeepPage.Values[0] := True;
  CreateProfilePage();
  CreateFeaturesPage();
  CreateSettingsPage();
  CreateClaudePage();
end;

function ShouldSkipPage(PageID: Integer): Boolean;
begin
  Result := False;
  if (PageID = KeepPage.ID) or (PageID = ProfilePage.ID) or (PageID = FeaturesPage.ID) or
     (PageID = SettingsPage.ID) or (PageID = ClaudePage.ID) then
  begin
    if WizardSilent then
      Result := True
    else if PageID = KeepPage.ID then
      Result := not ExistingConfig
    else if PageID = ClaudePage.ID then
      Result := KeepConfig() or not OfferLiveSessions()
    else
      Result := KeepConfig();
  end;
end;

procedure CurPageChanged(CurPageID: Integer);
begin
  if CurPageID = ProfilePage.ID then
    LayoutProfilePage()
  else if CurPageID = SettingsPage.ID then
  begin
    UpdateSettingsEnabled();
    UpdateSettingsCaptions();
  end
  else if CurPageID = ClaudePage.ID then
  begin
    UpdateClaudePage();
    LayoutClaudePage();
  end;
end;

function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;
  if WizardSilent then Exit;
  // Al dejar la página de perfiles, «Qué se activa» y las sesiones en vivo se rellenan con el perfil elegido (si cambió
  // desde la última vez: lo tocado a mano con el mismo perfil se respeta).
  if CurPageID = ProfilePage.ID then
  begin
    if PrefilledProfile <> SelectedProfile() then
    begin
      PrefillLiveSessions(SelectedProfile());
      PrefillFeatures(SelectedProfile());
    end;
    FeaturesPage.Description := FmtMessage(CustomMessage('FeaturesDescription'), [ProfileCaption(SelectedProfile())]);
  end;
end;

// ---------------- Al instalar: el JSON para --apply-setup ----------------

function ShouldApplySetup(): Boolean;
begin
  Result := not WizardSilent and not KeepConfig();
end;

function SetupJsonPath(Param: String): String;
begin
  Result := ExpandConstant('{tmp}\' + SetupJsonName);
end;

procedure AddOverride(const Key, Value: String);
begin
  if JsonOverrides <> '' then JsonOverrides := JsonOverrides + ',' + #13#10;
  JsonOverrides := JsonOverrides + '    ' + JsonStr(Key) + ': ' + Value;
end;

// { version, profile, liveSessions, startWithWindows, overrides } (Config/SetupApply.cs). En una instalación nueva viaja
// todo lo que se ha visto; en una actualización, solo lo que cambia respecto a lo que había.
function BuildSetupJson(): String;
var
  F: Integer;
  Fresh: Boolean;
begin
  Fresh := not ExistingConfig;
  JsonOverrides := '';
  for F := 0 to FeatureCount - 1 do
    if Fresh or (FeatureOn(F) <> FeatureInit[F]) then AddOverride(FeatureKey(F), BoolJson(FeatureOn(F)));
  if Fresh or (LangCombo.ItemIndex <> InitLang) then AddOverride('Language', JsonStr(LangCode(LangCombo.ItemIndex)));
  if Fresh or (MonitorCombo.ItemIndex <> InitMonitor) then AddOverride('NotchMonitor', JsonStr(SelectedMonitorDevice()));
  if Fresh or (SizeCombo.ItemIndex <> InitSize) then AddOverride('NotchSizePercent', IntToStr(SizeValue(SizeCombo.ItemIndex)));
  if Fresh or (OpenCombo.ItemIndex <> InitOpen) then AddOverride('NotchOpenOnClick', BoolJson(OpenCombo.ItemIndex = 1));
  if OfferLiveSessions() and (Fresh or (PermissionsValue() <> InitPermissions)) then
    AddOverride('NotchPermissions', BoolJson(PermissionsValue()));

  Result := '{' + #13#10 +
    '  "version": 1,' + #13#10 +
    '  "profile": ' + JsonStr(ProfileId(SelectedProfile())) + ',' + #13#10 +
    '  "liveSessions": ' + BoolJson(OfferLiveSessions() and LiveCheck.Checked) + ',' + #13#10;
  if Fresh or (StartupCheck.Checked <> InitStartup) then
    Result := Result + '  "startWithWindows": ' + BoolJson(StartupCheck.Checked) + ',' + #13#10;
  Result := Result + '  "overrides": {' + #13#10 + JsonOverrides + #13#10 + '  }' + #13#10 + '}' + #13#10;
end;

// --apply-setup, con su código de salida (SetupOutcome.ExitCode): 0 = aplicado; 1 = aplicado, pero sin las sesiones en vivo
// (no se pudo cambiar settings.json); 2 = no encontró el JSON; 3 = JSON no válido; 4 = no pudo copiar, leer o guardar
// config.json. Con 2, 3 y 4 no se cambió nada. Otro código es que la app se cayó a medias. Salvo 0, se avisa; en
// silencioso no se llega aquí (ShouldApplySetup).
procedure ApplySetup();
var
  ResultCode: Integer;
  Launched: Boolean;
  LogFile: String;
begin
  WizardForm.StatusLabel.Caption := CustomMessage('ApplyingSetup');
  Launched := Exec(ExpandConstant('{app}\ClaudeBarWin.exe'), '--apply-setup "' + SetupJsonPath('') + '"', '', SW_HIDE,
    ewWaitUntilTerminated, ResultCode);
  if Launched then
    Log(Format('asistente: --apply-setup terminó con el código %d', [ResultCode]))
  else
    Log(Format('asistente: no se pudo lanzar --apply-setup (error %d)', [ResultCode]));
  if WizardSilent or (Launched and (ResultCode = 0)) then Exit;
  LogFile := ExpandConstant('{userappdata}\ClaudeBarWin\claudebar.log');
  if not Launched then
    // Sin arrancar, ResultCode es un error de Windows: no se confunde con los códigos de la app.
    MsgBox(FmtMessage(CustomMessage('SetupNotLaunched'), [IntToStr(ResultCode)]), mbError, MB_OK)
  else if ResultCode = 1 then
    MsgBox(CustomMessage('SetupHooksFailed'), mbInformation, MB_OK)
  else if (ResultCode >= 2) and (ResultCode <= 4) then
    MsgBox(FmtMessage(CustomMessage('SetupFailed'), [IntToStr(ResultCode), LogFile]), mbError, MB_OK)
  else
    MsgBox(FmtMessage(CustomMessage('SetupCrashed'), [IntToStr(ResultCode), LogFile]), mbError, MB_OK);
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if (CurStep = ssInstall) and ShouldApplySetup() then
    if not SaveStringToFile(SetupJsonPath(''), BuildSetupJson(), False) then
      Log('asistente: no se pudo escribir ' + SetupJsonPath(''));
  // Con los archivos ya copiados y antes de arrancar la app («Ejecutar ClaudeBar» va después, en la última página).
  if (CurStep = ssPostInstall) and ShouldApplySetup() then
    ApplySetup();
end;

// ---------------- Resumen ----------------

// «Con un clic» → «con un clic», para ir dentro de una frase del resumen.
function LowerFirst(const S: String): String;
begin
  Result := S;
  if Length(S) > 0 then Result := Lowercase(Copy(S, 1, 1)) + Copy(S, 2, Length(S) - 1);
end;

function YesNo(B: Boolean): String;
begin
  if B then Result := CustomMessage('Yes') else Result := CustomMessage('No');
end;

function SetupSummary(Space, NewLine: String): String;
var
  F, Line: Integer;
  Feats, Size, Monitor, Open, Live: String;
begin
  Result := CustomMessage('MemoTitle') + NewLine;
  if KeepConfig() then
  begin
    Result := Result + Space + CustomMessage('MemoKeep') + NewLine;
    Exit;
  end;
  Result := Result + Space + FmtMessage(CustomMessage('MemoProfile'), [ProfileCaption(SelectedProfile())]) + NewLine;
  if FeatureOn(FNotch) then
  begin
    if MonitorCombo.ItemIndex <= 0 then Monitor := CustomMessage('MonitorPrimaryItem')
    else Monitor := MonitorCaption(MonitorCombo.ItemIndex - 1);
    if SizeCombo.ItemIndex <= 0 then Size := 'Auto' else Size := IntToStr(SizeValue(SizeCombo.ItemIndex)) + ' %';
    if OpenCombo.ItemIndex = 1 then Open := CustomMessage('OpenClick') else Open := CustomMessage('OpenHover');
    Result := Result + Space + FmtMessage(CustomMessage('MemoNotchOn'), [LowerFirst(Open), Size, LowerFirst(Monitor)]) + NewLine;
  end
  else
    Result := Result + Space + CustomMessage('MemoNotchOff') + NewLine;
  // Lista en varias líneas cortas: el memo no ajusta el texto y una línea larga sacaría scroll horizontal.
  Feats := '';
  Line := 0;
  for F := 0 to FeatureCount - 1 do
    if (F <> FNotch) and FeatureOn(F) and (FeatureOn(FNotch) or (F = FReduceMotion)) and FeatureList.ItemEnabled[F] then
    begin
      if Feats <> '' then
      begin
        Feats := Feats + ',';
        if Line >= 45 then
        begin
          Feats := Feats + NewLine + Space + Space;
          Line := 0;
        end
        else
          Feats := Feats + ' ';
      end;
      Feats := Feats + Lowercase(FeatureShort(F));
      Line := Line + Length(FeatureShort(F)) + 2;
    end;
  if Feats = '' then Feats := CustomMessage('MemoNothing');
  Result := Result + Space + FmtMessage(CustomMessage('MemoFeatures'), [Feats]) + NewLine;
  Result := Result + Space + FmtMessage(CustomMessage('MemoLanguage'), [LangDisplay(LangCombo.ItemIndex)]) + NewLine;
  Result := Result + Space + FmtMessage(CustomMessage('MemoStartup'), [YesNo(StartupCheck.Checked)]) + NewLine;
  if OfferLiveSessions() then
  begin
    if HooksDetected then Live := CustomMessage('MemoLiveActive')
    else if LiveCheck.Checked then Live := CustomMessage('MemoLiveInstall')
    else Live := YesNo(False);
    Result := Result + Space + FmtMessage(CustomMessage('MemoLive'), [Live]) + NewLine;
    Result := Result + Space + FmtMessage(CustomMessage('MemoPermissions'), [YesNo(PermissionsValue())]) + NewLine;
  end;
  if ExistingConfig then
    Result := Result + Space + CustomMessage('MemoBackup') + NewLine;
end;

function UpdateReadyMemo(Space, NewLine, MemoUserInfoInfo, MemoDirInfo, MemoTypeInfo, MemoComponentsInfo,
  MemoGroupInfo, MemoTasksInfo: String): String;
begin
  Result := '';
  if MemoDirInfo <> '' then Result := Result + MemoDirInfo + NewLine + NewLine;
  if MemoGroupInfo <> '' then Result := Result + MemoGroupInfo + NewLine + NewLine;
  if MemoTasksInfo <> '' then Result := Result + MemoTasksInfo + NewLine + NewLine;
  if not WizardSilent then Result := Result + SetupSummary(Space, NewLine);
end;

// La última elección se recuerda (clave de desinstalación) para proponerla en la próxima actualización.
procedure RegisterPreviousData(PreviousDataKey: Integer);
var
  Id: String;
begin
  if WizardSilent or KeepConfig() then
    Id := PreviousProfileId
  else
    Id := ProfileId(SelectedProfile());
  if Id <> '' then SetPreviousData(PreviousDataKey, 'SetupProfile', Id);
end;

function InitializeUninstall(): Boolean;
begin
  if CheckForMutexes(AppMutexName) then
    KillRunningApp();
  Result := True;
end;

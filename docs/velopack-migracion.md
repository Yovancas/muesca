# Instalación y actualizaciones con Velopack (desde la 0.5)

> La app ahora se llama **Muesca** (antes ClaudeBar for Windows; ver [branding.md](branding.md)). Lo que se ve (el
> lanzador `Muesca.exe`, el acceso directo, «Aplicaciones», los avisos) es de Muesca; las carpetas, el `packId`, el exe
> `ClaudeBarWin.exe` y el helper conservan sus nombres. El repositorio público pasa a `Yovancas/muesca`.

Hasta la 0.4, ClaudeBar se instalaba con Inno Setup (un asistente con perfiles, `installer/ClaudeBarWin.iss`) y se
actualizaba con NetSparkle, que bajaba ese mismo instalador entero (~40 MB) y lo ejecutaba con `/VERYSILENT`. Desde la
0.5 se instala y se actualiza con [Velopack](https://github.com/velopack/velopack) (MIT, versión **1.2.161**):

- **Instalar** son 2-3 s, sin asistente y con una pantalla de carga (`installer/assets/splash.gif`). Medido en Windows 11:
  2,5 s con `--silent` y 3,4 s con ventana.
- **El asistente de perfiles se muda a la app**: se abre la primera vez, dentro del notch (lo hace otra rama).
- **Las actualizaciones** se descargan en segundo plano, con parches delta, y se aplican al pulsar o al cerrar la app.
  Medido: un delta de 133 KB frente a 52,7 MB del paquete completo; comprobar, 53 ms; bajar y reconstruir, 3,2 s;
  aplicar y volver a abrir, 2,3 s.
- **La firma no se pierde**: cada release lleva su manifiesto firmado con la misma clave Ed25519 que usaba NetSparkle.

## Dónde queda cada cosa

| Qué                                   | Dónde                                                                    |
| ------------------------------------- | ------------------------------------------------------------------------ |
| La app                                | `%LOCALAPPDATA%\ClaudeBar\current\ClaudeBarWin.exe`. La ruta no cambia entre versiones: Update.exe sustituye `current\` entera |
| Lanzador estable                      | `%LOCALAPPDATA%\ClaudeBar\Muesca.exe` (el *stub* de vpk: `--packTitle` + `.exe`); reenvía los argumentos |
| Update.exe y paquetes descargados     | `%LOCALAPPDATA%\ClaudeBar\Update.exe`, `…\packages\`                     |
| Helper de los hooks y snapshots       | `%LOCALAPPDATA%\ClaudeBarWin\bin\`, `…\ratelimits\` (como en la 0.4, **fuera** de la carpeta de Velopack) |
| Config, historial y `claudebar.log`   | `%APPDATA%\ClaudeBarWin\` (sin cambios)                                  |
| Log de Velopack                       | `%LOCALAPPDATA%\velopack\velopack_ClaudeBar.log`                         |
| Accesos directos                      | Solo el del menú Inicio (`Muesca`; Setup.exe pone también uno en el escritorio si no se le dice `--shortcuts`) |
| «Iniciar con Windows»                 | `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`, valor `ClaudeBarWin` = `"…\ClaudeBar\Muesca.exe" --autostart` |
| «Aplicaciones»                        | `HKCU\…\Uninstall\ClaudeBar` (la clave es el `packId`), que se ve como «Muesca», de Bekay, con `QuietUninstallString` = `Update.exe --uninstall --silent` |

**`--packId ClaudeBar` y `--packTitle Muesca` no se cambian nunca** (desde la primera release con Velopack): son la
carpeta, el lanzador y la entrada de «Aplicaciones» de todas las instalaciones. El `packTitle` pasó de «ClaudeBar» a
«Muesca» antes de esa primera release, cuando aún no había ninguna instalación (branding.md). El `packId` NO es `ClaudeBarWin` a propósito: Update.exe **mata sin cierre
ordenado** todo lo que corre desde la carpeta de instalación al aplicar una actualización o al desinstalar, y Setup.exe
vacía esa carpeta si reinstala encima. Con la carpeta de datos aparte, un `claudebar-helper.exe` contestando un permiso
no muere en mitad de una actualización, y el helper al que apuntan los hooks de `settings.json` nunca desaparece.

«Iniciar con Windows» (`StartupManager`) es un valor de la clave Run con el lanzador estable y `--autostart` (antes, un
acceso directo en la carpeta Inicio). `Main` trata `--autostart` como un arranque normal (`StartupManager.StartedWithWindows`).
Al arrancar, la app reescribe la entrada si apunta a un exe que ya no existe, a `current\`, a la instalación de Inno o si le
falta `--autostart`, y pasa a la clave Run el acceso directo de la 0.4. Lo que el usuario apagó en el Administrador de
tareas (`Explorer\StartupApproved`) sigue apagado. Velopack no toca la clave Run: la quita el hook de desinstalación.

El helper solo acepta respuestas de permisos de un `ClaudeBarWin.exe` de su carpeta, de `%LOCALAPPDATA%\ClaudeBar\current`
o (para la transición) de `%LOCALAPPDATA%\Programs\ClaudeBarWin` (`PermissionClient.IsTrustedServerPath`). En la edición de
la Store (MSIX, sin Velopack) el helper va dentro del paquete y, con identidad de paquete, solo se fía de la app de su
paquete: `docs/msix.md`.

## Arranque y hooks de Velopack

`Main` empieza por `VelopackApp.Build()…Run()` (configurado en `VelopackStartup`), antes de cualquier modo por línea de
comandos. Setup.exe y Update.exe llaman a la app con `--veloapp-install/updated/obsolete/uninstall <versión>`; Velopack
ejecuta el hook y cierra el proceso. Los hooks rápidos tienen de 15 a 60 s y no pueden enseñar nada (no existe un «después
de desinstalar»). Al actualizar, el orden medido es: `obsolete` en la versión vieja, `updated` en la nueva y, si Update.exe
la vuelve a abrir, `OnRestarted`.

- **Primera instalación**: NO aplica perfiles. Solo crea `config.json` con `OnboardingPending = true`, y solo si no había
  config (`OnboardingState`). Quien llega desde Inno o reinstala ya está configurado. Con `--silent`, Setup.exe ejecuta
  este hook pero no abre la app.
- **Actualización**: despliega el helper nuevo al momento (`HelperDeployer.Ensure`).
- **Reinicio tras actualizar** (`OnRestarted`): la bandeja avisa «Muesca se ha actualizado a la versión X».
- **Desinstalación** (con la app ya parada): lo mismo que el `[UninstallRun]` y el `[UninstallDelete]` de Inno
  (`UninstallCleanup`): quita nuestros hooks de `~/.claude/settings.json`, restaura la statusline, borra los snapshots y el
  helper (y sus carpetas si quedan vacías) y la entrada de la clave Run. Después Velopack borra su carpeta y sus accesos
  directos. La config no se toca.
- **Arranque normal** (sin argumentos o con `--autostart`): si hay una versión ya descargada y sin aplicar, Velopack la
  aplica y vuelve a abrir la app, salvo con otra instancia abierta (`VelopackStartup.ShouldAutoApply`).
- **AppUserModelID**: el de Velopack (`velopack.ClaudeBar`), el mismo que lleva el acceso directo del menú Inicio. La app
  no fijaba uno propio y sus avisos son globos de la bandeja, así que no se cambia (`SetAppUserModelId` haría falta si
  algún día hay notificaciones que dependan de uno concreto).

### `OnboardingPending`

Clave nueva de `AppConfig`, para el asistente del notch:

- Un `config.json` que ya existía y no la trae se lee como **false**.
- Una config nueva (no había `config.json`, o el que había no se podía leer y se apartó a `.bad`) nace con **true**:
  `AppConfig.Load` la crea así, y el hook de primera instalación también.
- El asistente la pone a false al terminar. `--apply-setup` (el asistente de Inno) no la toca.

## Actualizaciones: `IAppUpdates`

`Services/Updates/IAppUpdates.cs` es la interfaz que usan la bandeja y el notch; la implementación es `VelopackUpdates`.

| Etapa          | Qué pasa                                                    | Menú de la bandeja                     |
| -------------- | ----------------------------------------------------------- | -------------------------------------- |
| `Idle`         | Al día, sin comprobar, o esta copia no la instaló Velopack   | «Buscar actualizaciones»               |
| `Checking`     | Preguntando a GitHub y verificando la firma                 | «Buscando actualizaciones…» (gris)     |
| `Downloading`  | Bajando la versión nueva (`DownloadProgress` de 0 a 1)       | «Descargando la v0.5.1… 42 %» (gris)   |
| `ReadyToApply` | Descargada y verificada                                     | «Reiniciar para actualizar a la v0.5.1» |
| `Failed`       | Sin red, firma mala, paquete corrupto… (detalles en el log) | «Buscar actualizaciones» o, si la descarga falló, «Actualización disponible» (reintenta y aplica) |

- `StartLoop()`: comprueba al arrancar y cada 6 h, en silencio; con una versión ya lista no vuelve a preguntar.
- `CheckNowAsync()`: termina al saber si hay versión nueva; la descarga sigue en segundo plano. El menú avisa con un globo
  («Estás al día», «se está descargando…», y «está lista» cuando acaba).
- `ApplyAndRestartAsync()`: descarga si hace falta, lanza Update.exe (`WaitExitThenApplyUpdates`, con reinicio) y cierra la
  app **con normalidad** (icono de la bandeja, notch, pipes); Update.exe espera a que salga y la vuelve a abrir ya
  actualizada. No se usa `ApplyUpdatesAndRestart`, que sale con `Environment.Exit` sin cerrar nada.
- Al cerrar la app con una versión lista, Update.exe la aplica en silencio en cuanto sale (`Dispose`), y el siguiente
  arranque ya es la nueva. Si la app no llegó a cerrarse bien (un apagado), la aplica Velopack al arrancar.
- `Changed` llega siempre en el hilo de UI.

## La firma (obligatoria)

Velopack solo comprueba que cada paquete que baja (completo o delta) cuadre con el SHA-256 y el tamaño que dice el
manifiesto de la release (`releases.win.json`). Sin más, quien pudiera subir assets a la release podría cambiar paquete y
manifiesto a la vez.

- **Qué se firma**: con Ed25519, `claudebar/velopack-manifest/v1\n<dueño/repo en minúsculas>\n` seguido de los bytes de
  `releases.win.json` tal cual los sirve GitHub (`ManifestSignature.SignedMessage`). La firma va al lado, en
  `releases.win.json.sig`: 64 bytes en base64, sin salto de línea (el formato de los `.signature` de NetSparkle). El
  contexto hace que nada firmado con la misma clave para otra cosa valga como manifiesto: ni un `appcast.xml` de NetSparkle
  renombrado, ni el manifiesto de un repositorio de pruebas copiado al público.
- **Con qué clave**: la misma de NetSparkle. La pública está en `ManifestSignature.PublicKeyBase64`
  (`UxMaJjAdtg6sQZrydaUvC0N088ZIheKsFw+HjQCsLS8=`); la privada NUNCA va al repo: el script la lee de
  `SPARKLE_PRIVATE_KEY` (base64 de la semilla de 32 bytes, que es lo que guarda `netsparkle-generate-appcast`; también
  admite los 64 bytes del formato NaCl) y se niega a firmar si no corresponde a la pública.
- **Cómo se verifica**: `SignedGithubSource` hereda de `GithubSource` y repite su `GetReleaseFeed` (Velopack 1.2.161: las
  10 últimas releases publicadas, un manifiesto por release), pero baja cada manifiesto y su `.sig` y verifica ANTES de
  leer el JSON. Una release sin firma, con la firma mala, con el manifiesto tocado o firmado pero ilegible, o cuyo manifiesto
  o `.sig` no se pudo bajar (fallo de red), se descarta entera (sin tumbar la consulta) y se anota en `claudebar.log`. Las
  releases de Inno (sin manifiesto de Velopack) ni cuentan.
- Lo que sí pasa la verificación sigue por Velopack: `DownloadReleaseEntry` es el de `GitBase`, que solo acepta los assets
  que salieron del feed verificado, y `UpdateManager` comprueba tamaño y SHA-256 de cada paquete y de cada delta. Además,
  `SignedGithubSource.DownloadReleaseEntry` lo vuelve a comprobar contra el manifiesto firmado justo después de descargar
  (y exige SHA-256: el SHA-1 solo no basta), para no depender de que Velopack lo siga haciendo.
- Si no hay versión nueva verificada pero se descartó alguna release que podría ser más nueva que la instalada (por su
  nombre, `v0.5.2`, o sin versión legible), o si entre las 10 últimas no hay ninguna con el manifiesto bien firmado, `VelopackEngine` lo trata como fallo (`Failed`), no como «al día»: una release manipulada (o que
  desaparezcan todas) no pasa en silencio. Una descartada más vieja que la instalada no tapa nada: se anota y ya.
- Al aplicar se usa lo que de verdad está descargado (`UpdatePendingRestart`); si no hay nada, falla en vez de dejar que
  Update.exe reinstale la versión instalada. Y con la aplicación ya pedida no empieza ninguna comprobación ni descarga:
  Velopack borra los demás paquetes al descargar, también el que espera Update.exe.
- Diferencia con el `SignedFeedSource` probado en Windows (que envuelve la fuente y lanza `CryptographicException` con una
  firma inválida): aquí se verifica el manifiesto de cada release (las 10 últimas) y se descarta solo la que no verifica.
  Una versión más nueva y bien firmada se sigue ofreciendo aunque otra release esté manipulada; si no hay ninguna, la
  comprobación falla igual (`Failed`). Los assets que llegan a Velopack se construyen a partir del manifiesto ya
  verificado, como `GitBaseAsset` con su release, que es lo que `GitBase` necesita para descargar.
- **Librería**: `NetSparkleUpdater.Chaos.NaCl` 0.9.4, la que NetSparkle ya usaba para verificar (Ed25519 estándar; los tests
  incluyen el vector 1 de la RFC 8032).

`vpk upload github` no sube el `releases.win.json` del disco: lo **regenera** al subir, solo con los paquetes de esa versión.
Por eso el script firma el manifiesto **que ha subido**, pero no a ciegas (alguien con permisos de escritura en el repo
podría cambiarlo en el borrador mientras se suben los 50 MB):

- antes de firmar comprueba que es exactamente el de esta versión: los mismos paquetes que hay en `dist\velopack`, con su
  nombre, su tamaño y su SHA-256 calculados aquí, la versión y el `packId`, y nada más (`Assert-ManifestMatchesBuild`);
- exige un solo borrador con ese tag, el mismo al empezar y al acabar (`Get-DraftReleaseId`), y que en él solo esté lo
  nuestro, con los paquetes del tamaño de aquí (`Assert-ReleaseAssets`);
- vuelve a bajar manifiesto y firma, los verifica como la app y publica **ese** borrador por su id.

**La base del delta** también se comprueba: `vpk download github` no verifica nada, y con una base manipulada el delta
marcaría como «sin cambios» archivos que sí cambian (Update.exe deja entonces el archivo viejo del usuario y no lee los
`.shasum`). El script compara el paquete bajado con el manifiesto **firmado** de su release (`Test-SignedBase`): si no
cuadra, para; si no se puede comprobar, publica sin delta. En el cliente no se puede volver a comprobar el paquete que
reconstruye Update.exe: lo vuelve a comprimir con su propio zip y no coincide byte a byte con el completo firmado, así que
la comprobación desactivaría los deltas.

## Publicar una versión: `scripts/release-velopack.ps1`

Requisitos (Windows): .NET 9 SDK, las Build Tools de C++ (helper NativeAOT), `vpk` en la **misma** versión que el paquete
Velopack (`dotnet tool install -g vpk --version 1.2.161`), `gh` con sesión iniciada y, para el puente, Inno Setup 6 y
`netsparkle-generate-appcast`.

```powershell
$env:SPARKLE_PRIVATE_KEY = Get-Content .sparkle-keys\NetSparkle_Ed25519.priv   # o de un gestor de secretos
pwsh scripts/release-velopack.ps1 -Version 0.5.0 -Bridge            # local: nada sale del equipo
pwsh scripts/release-velopack.ps1 -Version 0.5.0 -Bridge -Upload    # LÍNEA ROJA: sube y publica
```

1. `dotnet publish` self-contained y **sin** single-file (así los deltas solo llevan los archivos que cambian), más el helper.
2. `vpk download github` baja la release anterior (solo lectura) para sacar el parche delta, y se comprueba contra su
   manifiesto firmado.
3. `vpk pack -r win-x64`: `--packId ClaudeBar`, `--packTitle Muesca`, `--packAuthors Bekay`, `--shortcuts StartMenuRoot`, `--noPortable`, las notas de
   `installer/notes/<versión>.md`, y `--icon branding/muesca.ico` y `--splashImage installer/assets/splash.gif`
   solo si existen.
4. Firma el `releases.win.json` local (para comprobar la clave y probar en local).
5. `-Bridge`: compila `installer/bridge/ClaudeBarWin-Bridge.iss` con el Setup.exe de esta versión y genera el `appcast.xml`
   firmado de NetSparkle.
6. `-Upload`: exige árbol limpio, `HEAD` = `origin/main` (con el `git fetch` comprobado), el `<Version>` en el csproj, las
   notas y la clave. Después `vpk upload github` (borrador), comprobación y firma del manifiesto subido, puente, comprobación
   final y publicación del borrador por su id (y como la última release). Si algo falla después de subir, la release se
   queda en borrador (no la ve nadie) y el script dice cómo borrarla para volver a empezar.

Sin `-Upload` el script no sube nada. `scripts/release.ps1` (Inno + NetSparkle) se queda de referencia y se niega a publicar:
la app de la 0.5 ya no lleva NetSparkle.

### `--splashImage` y los GIF

Sí admite **GIF animado**: medido en Windows 11, se anima (Setup registra «loaded 12 frames»). Según el código de
Setup.exe de esa versión (`windows/splash.rs`) carga todos los fotogramas, pero con **un solo retardo**, la media de
todos (no respeta retardos distintos por fotograma); admite
transparencia; se escala con el DPI del monitor del ratón y como mucho al 70 % del monitor; encima pinta una barra de
progreso (`--splashProgressColor`, o `None` para quitarla). En silencio (`--silent`, como lo lanza el puente) no se enseña.

## Puente desde la instalación de Inno (0.3.9 / 0.4.0)

Esas versiones leen `releases/latest/download/appcast.xml` con NetSparkle, bajan lo que diga (verificando su firma Ed25519) y
lo ejecutan con `/VERYSILENT /SP- /SUPPRESSMSGBOXES /NORESTART`. El puente (`installer/bridge/ClaudeBarWin-Bridge.iss`) es
ese instalador, mínimo y sin páginas:

1. Espera hasta 20 s a que se cierre la app vieja (mutex `ClaudeBarWin_SingleInstance`) o la cierra.
2. Ejecuta en silencio el Setup.exe de Velopack que lleva dentro (`--silent --log %APPDATA%\ClaudeBarWin\velopack-setup.log`).
   Si falla, **no toca nada más**, vuelve a abrir la versión vieja y sale con error: NetSparkle lo reintentará. Antes, si
   `%LOCALAPPDATA%\ClaudeBar` ya existe con cosas y no es una instalación de ClaudeBar, no sigue (Setup.exe la vaciaría).
3. Retira la instalación de Inno **a mano**: borra sus archivos conocidos (`ClaudeBarWin.exe`, `claudebar-helper.exe`,
   `unins000.*`), la carpeta si queda vacía, el acceso directo del menú Inicio y su entrada de «Aplicaciones» (HKCU). **No**
   ejecuta su desinstalador: su `[UninstallRun]` quitaría los hooks de Claude Code.
4. Reapunta el acceso directo de «Iniciar con Windows» (con `--autostart`) y el del escritorio (si lo había) al lanzador
   nuevo. Al arrancar, la app pasa el de inicio a la clave Run.
5. Abre la app nueva.

La config no se mueve (`%APPDATA%\ClaudeBarWin` es la misma) y los hooks de `settings.json` no dejan de funcionar en ningún
momento: apuntan al helper de `%LOCALAPPDATA%\ClaudeBarWin\bin`, que Velopack no toca (instala en `%LOCALAPPDATA%\ClaudeBar`).

**Mientras dure la transición, cada release tiene que llevar el puente** (`-Bridge`): `releases/latest/download/appcast.xml`
siempre apunta a la última, y una release sin `appcast.xml` dejaría a esas instalaciones sin actualizar (404). El script no
deja subir sin `-Bridge`; para hacerlo a propósito hay que pasar `-NoBridge`, y entonces la release no se marca como la última
(las de Velopack la ven igual: miran la lista de releases).

## Riesgos

| Riesgo | Qué pasa | Mitigación |
| ------ | -------- | ---------- |
| Update.exe mata lo que corre en su carpeta | Al aplicar o desinstalar, Update.exe mata sin cierre ordenado todo lo que corre desde `%LOCALAPPDATA%\ClaudeBar`. | La app se cierra antes con normalidad (`WaitExitThenApplyUpdates` + cierre de la app). El helper vive fuera (`%LOCALAPPDATA%\ClaudeBarWin\bin`): no se muere a media respuesta. |
| Setup.exe vacía su carpeta si reinstala encima | Una reinstalación aparta y borra `%LOCALAPPDATA%\ClaudeBar`. | Ahí solo están la app y Velopack; los datos y el helper, aparte. |
| Cambiar `--packId` o `--packTitle` | Otra carpeta, otro lanzador y otra entrada de «Aplicaciones»: las instalaciones existentes dejarían de actualizarse bien. | Fijos en el script y vigilados por `ReleaseScriptTests`. |
| Release publicada sin firma o con la firma mala | La app la descarta y, si no hay otra más nueva, se queda en `Failed`. | El script comprueba la firma desde GitHub antes de publicar y deja la release en borrador si no cuadra. |
| Una release sin `appcast.xml` | Las instalaciones de Inno no se enteran de nada. | `-Bridge` en todas las releases durante la transición: con `-Upload` el script lo exige (o `-NoBridge`, que no marca la release como la última). |
| Instalan la nueva con Setup.exe sin pasar por el puente | Quedarían dos entradas (ClaudeBar y Muesca) en «Aplicaciones»; quitar el de Inno ejecutaría su `[UninstallRun]` (`--uninstall-hooks`) y se llevaría los hooks, la statusline y el helper que usa la nueva. | El hook de instalación de Velopack retira la de Inno igual que el puente (`LegacyInnoCleanup`): cierra la app vieja si sigue abierta (solo la que corre desde la carpeta de Inno, con una espera de 3 s como mucho), borra los archivos con nombre conocido, su acceso directo del menú Inicio y su entrada de «Aplicaciones», sin ejecutar su desinstalador, y reapunta «Iniciar con Windows» y el acceso directo del escritorio al lanzador de Velopack. Sin su clave no hace nada. |
| El puente falla | Si falla antes de instalar (la carpeta de Velopack ocupada por otra cosa), la instalación de Inno queda intacta y la app vieja se vuelve a abrir. Si falla Setup.exe, su hook de instalación puede haber retirado ya la de Inno: el puente abre la vieja si su exe sigue ahí y, si no, la nueva. | Log de Inno en `%TEMP%\Setup Log *.txt` y el de Setup.exe en `%APPDATA%\ClaudeBarWin\velopack-setup.log`. NetSparkle reintenta el puente si la vieja sigue instalada. |
| SmartScreen | Setup.exe no lleva firma Authenticode (el instalador de Inno tampoco). La primera instalación desde el navegador enseña el aviso de Windows. | El puente y las actualizaciones no pasan por el navegador: sin aviso. |
| Apagar con una versión lista | Update.exe puede morir a mitad. | Velopack cambia `current\` con renombrados y vuelta atrás; al arrancar, aplica lo pendiente. |
| Alguien con permisos de escritura en el repo | Podría cambiar el manifiesto o los paquetes del borrador mientras se sube, o los de la release anterior. | Se firma solo lo que coincide con lo compilado aquí; la base del delta se comprueba contra su manifiesto firmado; un paquete cambiado después no pasa el SHA-256 en los clientes. |
| La clave privada | Si se filtra, se pueden firmar actualizaciones. Es la misma clave de siempre. | Igual que antes: solo en el equipo de quien publica, en una variable de entorno. Rotarla exige publicar antes una versión con la pública nueva. |
| Congelación | Quien controle GitHub puede no enseñar versiones nuevas (o repetir manifiestos viejos firmados). | No hay bajadas de versión: `UpdateManager` no instala nada más viejo que lo instalado. |

## Cómo probarlo (Windows, mejor en una máquina virtual)

**Tests**: `dotnet test ClaudeBarWin.sln -c Release`. Los nuevos: `ManifestSignatureTests`, `SignedGithubSourceTests`,
`VelopackUpdatesTests`, `UpdateMenuTests`, `VelopackStartupTests`, `OnboardingStateTests`, `AppLocationTests`,
`ReleaseScriptTests` y los casos nuevos de `PermissionClientTests`.

**Instalación nueva**:
1. `pwsh scripts/release-velopack.ps1 -Version 0.5.0` y ejecuta `dist\velopack\ClaudeBar-win-Setup.exe`.
2. Mira: pantalla de carga, 2-3 s, la app se abre; `%LOCALAPPDATA%\ClaudeBar\` con `current\`, `Muesca.exe` y
   `Update.exe`; acceso directo «Muesca» en el menú Inicio; Muesca, de Bekay, en «Aplicaciones»; `config.json` con
   `"OnboardingPending": true`.
3. Activa «Iniciar con Windows» en los ajustes: `HKCU\…\Run\ClaudeBarWin` = `"…\ClaudeBar\Muesca.exe" --autostart`;
   reinicia sesión y comprueba que arranca (en el log, «inicio con Windows»). Apágalo en el Administrador de tareas: el
   interruptor de los ajustes sale apagado. Desinstala: la entrada desaparece.
4. Activa las sesiones en vivo y los permisos desde el notch: un permiso de Claude Code se contesta desde el notch (si el
   helper no confiara en `current\`, saldría el diálogo de la terminal).
5. Avisos: `VelopackApp.Run()` fija el AppUserModelID del proceso (el acceso directo del menú Inicio lleva el mismo).
   Comprueba que los globos de la bandeja (hitos de cuota, «estás al día») salen con el nombre y el icono de Muesca.

**Actualización** (contra un repositorio de pruebas, sin tocar el público):
1. Crea un repositorio de pruebas en GitHub y publica en él dos versiones firmadas:
   `release-velopack.ps1 -Version 0.5.0 -Upload -GhRepo <dueño>/<repo-pruebas>` y luego la `0.5.1` (el `-Upload` pide
   `HEAD` = `origin/main` y el `<Version>` commiteado: hazlo en una rama de pruebas con `origin` apuntando a ese repo).
2. Instala la 0.5.0 y arranca la app con `CLAUDEBAR_UPDATE_REPO=https://github.com/<dueño>/<repo-pruebas>`.
3. Mira el menú: «Descargando la v0.5.1… %» y luego «Reiniciar para actualizar a la v0.5.1». Púlsalo: se cierra, Update.exe
   aplica y se vuelve a abrir en la 0.5.1. Repite cerrando la app en vez de pulsar: el siguiente arranque ya es la 0.5.1.
4. Firma: borra `releases.win.json.sig` de una release de pruebas (o sube uno de otra versión) y busca actualizaciones:
   etapa `Failed` y «release descartada» en `claudebar.log`.

**Puente**:
1. En una máquina con la 0.4.0 instalada con Inno (sesiones en vivo activadas, «Iniciar con Windows» activado), ejecuta
   `dist\bridge\ClaudeBarWin-Bridge-0.5.0.exe /VERYSILENT /SP- /SUPPRESSMSGBOXES /NORESTART` (lo que hace NetSparkle).
2. Mira: `%LOCALAPPDATA%\Programs\ClaudeBarWin` ya no está; ni su entrada en «Aplicaciones» (sí la nueva); «Iniciar con
   Windows» ya es el valor de la clave Run (y el acceso directo de la carpeta Inicio desapareció); `config.json` igual que
   antes (sin `OnboardingPending` o a false); los hooks de `~/.claude/settings.json` siguen y funcionan, también durante
   el puente (`%LOCALAPPDATA%\ClaudeBarWin\bin` no se toca). Repite con «Iniciar con Windows» apagado en el Administrador
   de tareas: tiene que seguir apagado.
3. Fallo: compila un puente con un Setup.exe que salga con error (`ISCC … /DSetupExe=<un exe que falle>`), ejecútalo y
   comprueba que la 0.4.0 sigue instalada, en «Aplicaciones» y abierta.
4. De punta a punta: publica en el repositorio de pruebas con `-Bridge` y deja que la 0.4.0 se actualice sola desde el menú
   (para eso, el appcast de la 0.4.0 tiene que apuntar a ese repositorio: solo se puede probar con una 0.4.0 compilada
   contra él).

**Capturas**: el splash durante la instalación; el menú en cada etapa; los globos de «al día», «descargando» y «lista»;
«Aplicaciones» antes y después del puente.

## Preguntas abiertas

- **API de Velopack**: todo lo usado está comprobado contra el código de la 1.2.161 (biblioteca, `vpk` y Setup/Update.exe):
  `GitBase.GetReleaseFeed` es `virtual` y su `GitBaseAsset` es accesible desde una subclase; `GetReleases` y
  `GetAssetUrlFromName` son `protected`. Nada de eso es API documentada como estable: al subir de versión hay que repasar
  que `SignedGithubSource` siga repitiendo el `GetReleaseFeed` de `GitBase` (los tests con un GitHub falso lo vigilan).
- **Medido en Windows 11** (fuera de esta rama, con Velopack/vpk 1.2.161): instalación, actualización por delta,
  desinstalación, hooks y su orden, el GIF del splash y una fuente firmada equivalente. **Sin probar todavía**: esta rama en
  Windows (los tests, la migración del autoarranque a la clave Run, el puente; el `.iss` del puente no se ha compilado,
  ISCC no corre aquí), `GithubSource` contra el GitHub de verdad, la vuelta al paquete completo cuando falla un delta, y
  Authenticode. El empaquetado sí se ha probado aquí con `vpk [win] pack` (nombres de Setup.exe, del lanzador y del
  manifiesto).
- **Revisión de seguridad independiente** (otra instancia, sin ver mis conclusiones): sus hallazgos están aplicados (firma
  con contexto, comprobaciones del script antes de firmar y publicar, base del delta, fallos en vez de «al día», nada que
  empiece con la aplicación pedida, el puente no vacía un `%LOCALAPPDATA%\ClaudeBar` ajeno). Quedan, menores: el manifiesto
  y su `.sig` se bajan enteros antes de mirar su tamaño (la API de releases de Velopack no lo da); y un modo por línea de
  comandos lanzado mientras la bandeja descarga hace que `VelopackApp.Run()` limpie los deltas a medias (esa descarga acaba
  con el paquete completo).
- **`gh` con borradores**: el script da por hecho que `gh release download/upload/edit` encuentran una release en borrador
  por su tag (es lo que documenta `gh release edit --draft=false`). Comprobarlo en la primera publicación.
- **Seguridad, decisiones tomadas**: misma clave que NetSparkle (no se rota); se firma el manifiesto y no cada paquete
  (cada paquete y cada delta quedan atados por su SHA-256 dentro del manifiesto firmado; el paquete que Velopack reconstruye
  con los deltas no se vuelve a comprobar, pero todo lo que entra en él sí); se descartan solo las releases que no
  verifican, en vez de bloquearlo todo; `CLAUDEBAR_UPDATE_REPO` solo admite repositorios de github.com y no salta la firma.
- **`--packId ClaudeBar` en vez de `ClaudeBarWin`** (decisión tomada, se puede discutir antes de la primera release, después
  ya no): la app queda en `%LOCALAPPDATA%\ClaudeBar` y la carpeta de datos de siempre, `%LOCALAPPDATA%\ClaudeBarWin`, fuera
  del alcance de Update.exe y Setup.exe. Con `ClaudeBarWin` habrían compartido carpeta.

## Pendiente

- **Notch (otra rama)**: el asistente de primer arranque lee `AppConfig.OnboardingPending` y lo pone a false al terminar
  (y puede mirar `StartupManager.StartedWithWindows` si no quiere abrirse al iniciar sesión). El
  notch puede enseñar el estado de `IAppUpdates` (namespace `ClaudeBarWin.Services.Updates`): por ejemplo, un aviso con
  `ReadyToApply` y un botón que llame a `ApplyAndRestartAsync()`.
- **Assets**: el icono es `branding/muesca.ico`. `installer/assets/splash.gif` no existe todavía: se monta con los
  fotogramas de `SHOTS_DIR\splash\` (`OnboardingShotsTests`), y el script lo usa si aparece.
- **Al publicar la 0.5**: el README (sección de instalación: el instalador pasa a ser `ClaudeBar-win-Setup.exe`; la de
  auto-actualización; la de desinstalar), el manifiesto de winget (`InstallerType: exe`, `Silent: --silent`; para
  desinstalar, la `QuietUninstallString` de Velopack) y las notas.
- **Cuando el asistente del notch esté fusionado**: retirar `installer/ClaudeBarWin.iss`, `scripts/release.ps1`,
  `--apply-setup` si ya no lo usa nadie, y la ruta de Inno en `PermissionClient` cuando no quede ninguna 0.4.

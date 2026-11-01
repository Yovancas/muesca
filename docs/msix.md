# Muesca en la Microsoft Store (MSIX)

> 31-10-2026. Edición de la Store de Muesca (by Bekay): paquete MSIX firmado por Microsoft, Pro con el complemento «Pro» de
> la Store. La edición de GitHub (Velopack / winget) no cambia. Diseño del reparto gratis / Pro:
> `docs/superpowers/specs/2026-10-30-gratis-y-pro-design.md`. Subida a Partner Center: `docs/store-submission.md`.

## Resumen

| Qué | Edición de GitHub (sin cambios) | Edición de la Store (identidad de paquete) |
| --- | ------------------------------- | ------------------------------------------ |
| Cómo se detecta | Sin identidad de paquete | `PackageIdentity.HasIdentity()` → `StoreEdition.IsActive` |
| Instala y actualiza | Velopack (`VelopackApp`, `VelopackUpdates`) | La Store. `VelopackApp.Run()` no se llama; `StoreUpdates` no ofrece nada y el menú no tiene «Buscar actualizaciones» |
| Helper de los hooks | Copiado a `%LOCALAPPDATA%\ClaudeBarWin\bin` | Dentro del paquete; Claude Code lo lanza por el alias `muesca-helper.exe` |
| Datos compartidos con el helper (snapshots de la statusline) | `%LOCALAPPDATA%\ClaudeBarWin\ratelimits` | `%LOCALAPPDATA%\Packages\<familia>\LocalState\ClaudeBarWin\ratelimits` |
| Iniciar con Windows | Clave Run de HKCU | Tarea de inicio del paquete (`MuescaStartup`) |
| Pro | Gratis con candado y enlace a la Store | `StoreContext`: complemento «Pro» y 7 días de prueba |
| Desinstalar | Hook de Velopack: quita hooks, statusline y helper | Windows borra el paquete y sus datos; **no ejecuta código nuestro** (ver riesgos) |

Código: `Services/Packaging/` (`StoreEdition`, `StoreStartupTask`, `AppExecLink`), `Services/Updates/StoreUpdates.cs`, las
ramas `StoreEdition.IsActive` de `Program`, `TrayAppContext`, `StartupManager` y `HelperDeployer`, y en el helper
`HelperPackage`; lo que los dos tienen que hacer igual (carpeta de datos, identidad de paquete), en `Shared/PackageShared.cs`,
que compilan la app y el helper. Paquete: `installer/msix/`. Ficha: `store/`.

## La decisión: el helper sale de AppData por el alias de ejecución del paquete

### El problema

Claude Code (un proceso de fuera del paquete) lanza el helper en cada evento de hook y en cada refresco de la statusline,
por la ruta que la app escribe en `~/.claude/settings.json`. Hoy esa ruta es `%LOCALAPPDATA%\ClaudeBarWin\bin\
claudebar-helper.exe`, una copia que hace la app. Con MSIX esa copia no existe para nadie más:

> «All newly created files and folders in the user's `AppData` folder […] are written to a private per-user, per-app
> location; but merged at runtime to appear in the real `AppData` location.» — [Understanding how packaged desktop apps
> run on Windows](https://learn.microsoft.com/en-us/windows/msix/desktop/desktop-to-uwp-behind-the-scenes)

> «Files in the virtualized location are visible only to the app.» — [Flexible virtualization](https://learn.microsoft.com/en-us/windows/msix/desktop/flexible-virtualization)

Lo mismo pasa con HKCU («All writes under *HKCU* are copied on write to a private per-user, per-app location»), así que
tampoco sirve la clave Run de «Iniciar con Windows». En cambio, fuera de AppData sí se escribe de verdad: «Apart from
**AppData**, the app can write to any location where the user has write access, including other parts of
`%userprofile%`». Por eso `~/.claude/settings.json` y el script de respaldo `~/.claude/hooks/claudebar-hook.ps1` siguen
funcionando igual.

### Las opciones

| Opción | Veredicto | Por qué |
| ------ | --------- | ------- |
| Copiar el helper a AppData como hoy | ✗ | Va a la copia privada del paquete: Claude Code no lo encuentra. |
| `desktop6:FileSystemWriteVirtualization` = `disabled` | ✗ | Necesita la capacidad restringida `unvirtualizedResources`, que «is designed for certain types of desktop PC games that are published by Microsoft and our partners […] It is not intended to be used for other scenarios, because it could compromise the system's ability to uninstall cleanly» ([App capability declarations](https://learn.microsoft.com/en-us/windows/uwp/packaging/app-capability-declarations#restricted-capabilities)); el propio elemento dice «currently intended to be used only by certain types of desktop PC games» ([desktop6:FileSystemWriteVirtualization](https://learn.microsoft.com/en-us/uwp/schemas/appxpackage/uapmanifestschema/element-desktop6-filesystemwritevirtualization)). Casi seguro que no se aprueba, y deja archivos al desinstalar. |
| `virtualization:ExcludedDirectories` (p. ej. `%LOCALAPPDATA%\Muesca\bin`) | ✗ | Es la versión fina de lo anterior: también pide `unvirtualizedResources` y solo existe desde Windows 11 ([Flexible virtualization](https://learn.microsoft.com/en-us/windows/msix/desktop/flexible-virtualization)). La app soporta Windows 10 1809. |
| Copiarlo fuera de AppData (`%USERPROFILE%\.muesca\bin`, `~/.claude/…`) | ✗ | Se puede escribir, pero es un exe sacado del paquete: pierde la firma de la Store (Smart App Control y WDAC lo tratan como un exe sin firmar), se queda al desinstalar (política 10.2.7) y va contra el modelo de MSIX. |
| Copiarlo a la carpeta de datos del paquete y escribir esa ruta real | ✗ | Ruta real y Windows la borra al desinstalar, pero es la misma copia sin firmar, y la ruta de la copia privada de AppData no está documentada. |
| **Alias de ejecución** (`uap3:AppExecutionAlias`) | ✓ | El helper se queda dentro del paquete (firmado por la Store, de solo lectura) y Windows mantiene el alias al día en cada actualización: «Users and other processes can use an alias to start your application without having to specify the full path to your app» ([Start your application by using an alias](https://learn.microsoft.com/en-us/windows/apps/desktop/modernize/desktop-to-uwp-extensions#alias)). No pide capacidades restringidas aparte de `runFullTrust`. |

### Cómo queda

- **Manifiesto** (`installer/msix/Package.appxmanifest`): una segunda `<Application Id="Helper">` con
  `Executable="claudebar-helper.exe"`, `AppListEntry="none"` (no sale en Inicio) y el alias `muesca-helper.exe` («You can
  only specify a single app execution alias for each application in the package», por eso va en su propia aplicación).
- **Qué ruta va a `settings.json`** (`StoreEdition.ResolveHelperAlias`): Windows deja el alias en
  `%LOCALAPPDATA%\Microsoft\WindowsApps\muesca-helper.exe` (la carpeta que está en el PATH) y también en una subcarpeta
  con la familia del paquete. **Solo se escribe la de la familia**, que es solo de este paquete. La general no se usa nunca:
  «If multiple apps register for the same alias, the system will invoke the last one that was registered», y un paquete
  que declarase el mismo alias después del arranque haría que Claude Code lanzase su exe (también para contestar permisos)
  hasta el siguiente. Además, el destino del alias de la familia tiene que ser el helper de este paquete: se lee del punto
  de reanálisis (`AppExecLink`, etiqueta `IO_REPARSE_TAG_APPEXECLINK`). Ese formato **no está documentado** (se sigue el
  que usa Cygwin, `winsup/cygwin/path.cc`), así que solo sirve para descartar: si no se puede leer, el alias de la familia
  vale igual. Si Windows no crease la subcarpeta de la familia, la app se queda sin helper (script de PowerShell): es lo
  primero que hay que comprobar en Windows (abajo, «Cómo probarlo»).
- **Sin alias** (el usuario lo apagó en *Configuración › Aplicaciones › Configuración avanzada de aplicaciones › Alias de
  ejecución de aplicaciones*: «The user can disable the AppExecutionAlias entry for your packaged app»): `HelperDeployer.
  Current` es null y los hooks usan el script de PowerShell de siempre (`~/.claude/hooks`, fuera de AppData). Las sesiones
  en vivo siguen funcionando; los permisos desde el notch y la statusline no (el log lo dice una vez).
- **El nombre `muesca-helper`** se reconoce en `settings.json` con nombre exacto, como `claudebar-helper`
  (`HelperCommand`): pasar de la edición de GitHub a la de la Store (o al revés) reescribe solo nuestras entradas.

### Con identidad y sin ella

Claude Code lanza el alias en *exec form* (`command` + `args`, sin shell) con `CreateProcess`, y el proceso arranca con la
identidad del paquete. Pero la statusline (y los hooks de Claude Code anteriores a la 2.1.139) pasan por un shell: Git Bash
si está instalado. Git Bash es MSYS2, que hereda de Cygwin el trato de los alias **como enlaces simbólicos** a su destino
(`check_reparse_point_target`, «App execution aliases are commonly used by Windows Store apps»): ejecuta el exe de la
carpeta del paquete directamente, **sin identidad**. El helper funciona igual en los dos casos:

- **Datos compartidos** (`HelperPaths.DataDir`, `StoreEdition.DataDir`; la regla está en `Shared/PackageShared.cs`, un solo
  archivo que compilan la app y el helper): en la carpeta de datos del paquete,
  `%LOCALAPPDATA%\Packages\<familia>\LocalState\ClaudeBarWin`, una ruta real que no pasa por la virtualización de AppData.
  El helper saca la familia de su identidad (`GetCurrentPackageFamilyName`) o, sin ella, del nombre de la carpeta
  `…\WindowsApps\<nombre completo del paquete>` desde la que corre. Al arrancar, la app comprueba que esa carpeta está
  dentro de `ApplicationData.Current.LocalFolder` y, si no, lo anota en el log. Windows la borra al desinstalar.
- **Confianza** (`PermissionClient.IsTrustedServerPath`): ver abajo.

### La comprobación de confianza del helper

El helper solo contesta un permiso a un servidor del pipe que sea `ClaudeBarWin.exe`, que no tenga menos nivel que él
(integridad y elevación, sin cambios) y que esté en una carpeta de confianza:

| Helper | Carpetas de confianza | Por qué |
| ------ | --------------------- | ------- |
| Con identidad de paquete (alias, *exec form*) | **Solo** la carpeta de instalación de su paquete (`GetCurrentPackagePath`) | «After deployment, package files are marked read-only, and are heavily locked down by the operating system (OS). Windows prevents apps from launching if those files are tampered with» ([fuente](https://learn.microsoft.com/en-us/windows/msix/desktop/desktop-to-uwp-behind-the-scenes#installation)). Lanzado por el alias, la carpeta del propio helper puede ser la del alias, que el usuario puede escribir: no cuenta. Tampoco Velopack ni Inno: la app de la Store reescribe los hooks a su alias al arrancar. |
| Sin identidad, ejecutado desde la carpeta del paquete (Git Bash) | **Solo** esa carpeta (`HelperPackage.TrustedPackageDir`: la carpeta `…\WindowsApps\<paquete>` desde la que corre) | La misma protección. Velopack e Inno no valen: el usuario puede escribir en ellas. |
| Fuera de un paquete (edición de GitHub) | La del propio helper, `%LOCALAPPDATA%\ClaudeBar\current` y `%LOCALAPPDATA%\Programs\ClaudeBarWin`, como antes | Sin cambios. |

Nada se relaja para las instalaciones de GitHub: `PermissionClientTests` sigue igual y suma los casos con identidad
(la carpeta del alias, otro paquete, otra versión, una subcarpeta, `..`, otro exe del paquete: nada de eso vale).

## El resto de la edición de la Store

- **Edición y Pro.** `Services/Licensing` ya lo tenía: con identidad, `ProLicenseFactory` crea `StoreProLicense` sobre
  `WindowsStoreBackend` (`StoreContext`, complemento duradero con `InAppOfferToken` = `Pro`, prueba de 7 días guardada en
  `config.json`); sin ella, `FreeEditionLicense`. Ahora la edición se decide una sola vez (`StoreEdition.IsActive`) y la
  usan también el arranque, las actualizaciones, el inicio con Windows y el helper. `ProCatalog.StoreProductId` es un
  marcador: hay que poner el Store ID real (ver `docs/store-submission.md`).
- **Iniciar con Windows** (`StoreStartupTask`): `desktop:StartupTask` con `Enabled="false"`; se enciende solo si el usuario
  lo elige en la bienvenida o en Ajustes. «If RequestEnableAsync is called from a packaged desktop app, no user-consent
  dialog is shown», y «If a user disables a task, you can't programmatically re-enable it»: en ese caso el interruptor de
  Ajustes abre `ms-settings:startupapps` ([StartupTask](https://learn.microsoft.com/en-us/uwp/api/windows.applicationmodel.startuptask)).
  La app sabe que arrancó con Windows por `ActivationKind.StartupTask`.
- **Actualizaciones.** Las da la Store. Con la app abierta, Windows la cierra para actualizar y, si está registrada con
  `RegisterApplicationRestart`, la vuelve a abrir después («call the RegisterApplicationRestart function in every process
  that you want to restart. Each active window in your application receives a WM_QUERYENDSESSION message. At this point,
  your application can call the RegisterApplicationRestart function again», [fuente](https://learn.microsoft.com/en-us/windows/apps/desktop/modernize/desktop-to-uwp-extensions#updates)).
  La app **no se registra al arrancar**: una ventana oculta (`UI/SessionEndWindow`) recibe `WM_QUERYENDSESSION` y solo
  registra el reinicio si el aviso es el cierre para reemplazar archivos (`ENDSESSION_CLOSEAPP`, sin `ENDSESSION_LOGOFF`);
  en cualquier otro caso (apagar, reiniciar, cerrar sesión, un cierre cancelado) lo quita (`StoreRestart`, con
  `RESTART_NO_CRASH | RESTART_NO_HANG | RESTART_NO_REBOOT`). En `WM_ENDSESSION` la app se cierra con normalidad **dentro
  del propio mensaje** (guarda lo pendiente, quita el icono de la bandeja): al volver, Windows puede terminar el proceso.
- **Lo que no cambia en la Store.** La config, el historial y el log siguen en `%APPDATA%\ClaudeBarWin`: con MSIX van a la
  copia privada del paquete, que la propia app ve igual. Quien venga de la edición de GitHub conserva su `config.json`
  («Modifications to existing files under the user's AppData folder is allowed»).

## Empaquetado (`installer/msix/`)

| Archivo | Qué es |
| ------- | ------ |
| `Package.appxmanifest` | «Muesca» de «Bekay». Identidad como marcadores (`__IDENTITY_NAME__`, `__IDENTITY_PUBLISHER__`, `__VERSION__`, `__ARCH__`) que rellena el script con los datos de Partner Center. Solo `runFullTrust`. Windows 10 1809+. Los 8 idiomas. |
| `Assets/` | Los logos (escalas 100/125/150/200/400, `targetsize` 16-256 y `altform-unplated`). Los genera `make-assets.py` desde `branding/` (reproducible; copia los de `branding/` que ya tienen el tamaño exacto). |
| `build-msix.ps1` | `dotnet publish` self-contained (x64 y arm64; el helper con NativeAOT), `makepri` (para que Windows elija la escala de cada logo), `makeappx pack` y `bundle`, el `.msixupload` y, con `-TestSign`, una copia firmada con un certificado de prueba para instalar en local. `-TestSign -Wack` pasa el Windows App Certification Kit a esa copia. No sube nada. |

**La versión.** La Store exige cuatro partes, la última a 0 («reserved for Store use and must be left as 0») y la primera
distinta de 0 ([App package requirements](https://learn.microsoft.com/en-us/windows/apps/publish/publish-your-app/msix/app-package-requirements)):
las 0.x no valen. El script rechaza `0.x` y convierte `1.2.3` en `1.2.3.0`; la app compila con la misma versión. **Hay
que decidir la numeración** (propuesta: Muesca empieza en la 1.0.0 en las dos ediciones).

**La firma.** «The Microsoft Store will automatically re-sign your MSIX/AppX packages […] You don't need to purchase a
CA-trusted code signing certificate»: lo que se sube va sin firmar. La firma de prueba solo sirve para instalar en local
(el certificado tiene que confiarse en *Personas de confianza* del equipo, como dice el script).

**arm64.** El helper es NativeAOT: para compilarlo para arm64 hacen falta las «MSVC v143 – VS 2022 C++ ARM64 build tools».
Sin ellas, `-Arch x64`.

## Política de la Store: riesgos y mitigaciones

Repaso de las [Microsoft Store Policies 7.19](https://learn.microsoft.com/en-us/windows/apps/publish/store-policies)
(efectivas desde el 14-10-2025) contra lo que hace la app.

| Riesgo | Política | Cómo queda | Qué falta |
| ------ | -------- | ---------- | --------- |
| **Lee el token OAuth de Claude Code** (`~/.claude/.credentials.json`) | 10.5.1, 10.5.4, 10.2; 10.8.3 cuenta las «API secret keys» como información financiera que una cuenta individual no puede **requerir** | Solo lectura, en memoria, solo a `api.anthropic.com` (HTTPS, sin redirecciones, host comprobado), nunca se guarda ni se registra; se puede apagar (la cuota sale entonces de la statusline). La app no pide credenciales al usuario ni las necesita para funcionar. | Explicarlo en las notas de certificación (abajo) y en la política de privacidad. Si certificación lo objeta: que la edición de la Store arranque con el ajuste apagado y lo ofrezca en la bienvenida. |
| **Contador anónimo de uso** | 10.5.1, 10.5.2 | Sin identificadores, una vez al día, apagable, documentado en `PRIVACY.md`; Cloudflare ve la IP en tránsito y no se guarda. | Nada; se declara en *Support info* (privacidad: sí). |
| **Hooks y statusline en `settings.json` de Claude Code** | 10.2.8 (consentimiento para cambiar ajustes), 10.2.7 (desinstalar limpio) | Solo si el usuario lo activa, con copia de seguridad, y al apagarlo se quita solo lo nuestro. **MSIX no ejecuta código al desinstalar**: si el usuario desinstala sin apagarlos, quedan entradas que apuntan a un alias que ya no existe. **Mitigado:** (1) los hooks que fallan no bloquean Claude Code: es un error no bloqueante (nuestras entradas nunca llevan `onFailure: "block"`) y en los permisos sale el diálogo de Claude Code; (2) la statusline enganchada es **resistente a que falte el helper**: `if [ -e H ]; then H statusline --tee; else cat; fi \| C` en Git Bash y lo mismo con `Test-Path` en PowerShell (`StatuslineInstaller.TeeCommand`), así que sin el helper la statusline del usuario recibe el JSON directo y sigue igual (`StatuslineShellTests` lo ejecuta en los dos shells; `scripts/tap-e2e.ps1`, caso «Sin helper»). El arranque pasa a este formato el enganche de antes; (3) el botón **«Quitar la integración con Claude Code»** de Ajustes quita de una vez hooks y statusline (con copia y confirmación), y la ficha lo pide en «Antes de desinstalar». | Lo que queda tras desinstalar sin apagar es inofensivo pero no se limpia solo: entradas de hooks que fallan en silencio y un `if [ -e … ]` delante de la statusline. Pendiente (otra rama, si hace falta): un aviso en la bienvenida de la Store. |
| **Letras de LRCLIB** | 11.2 (contenido de terceros con permiso), 10.5.1 | Solo con las letras activadas (Pro); manda título y artista a `lrclib.net` y enseña la letra de la canción que el usuario está escuchando. | Nombrado en `PRIVACY.md` y `PRIVACY.es.md` (sección «Lyrics» / «Letras»). Las letras tienen derechos de sus editoriales: si certificación o un titular lo objeta, apagar las letras en la edición de la Store (el resto de la página Música no depende de ellas). Atribuir LRCLIB en la app o en `THIRD-PARTY-NOTICES.md`. |
| **`runFullTrust`** | 10.6 | Es una app Win32 (WinForms) de integridad media; sin ella no se instala. Ninguna otra capacidad. | Justificación en *Submission options* (texto abajo). |
| **Nombre y marca** | 10.1.1, 11.2 | «Muesca», sin «Claude» en el nombre; «para Claude Code» como uso descriptivo; «no afiliada a Anthropic» y el aviso de marca en todas las fichas. Palabras clave sin «Claude» (10.1.3: «Not use other product titles»). | Nada. |
| **Depende de otro software** | 10.2.4 | La primera línea de la descripción lo dice en los 8 idiomas. | Nada. |
| **Testable** | 10.3 | Sin cuenta de Claude, los revisores pueden ver todo con datos de ejemplo: la bienvenida y el tutorial interactivo (menú de la bandeja › Tutorial). | Decirlo en las notas (abajo). |
| **Prueba y compra** | 10.8.1, 10.8.4, 10.8.7 | Compra con la API de la Store. La prueba de 7 días y lo que pasa al acabar (tareas y bandeja en solo lectura) están en la ficha. Precio de lanzamiento sin tachado ficticio (`store/listing/README.md`). | Revisar en la app que el aviso de fin de prueba diga que los datos se conservan. |
| **Código dinámico** | 10.2.2 | En la Store no hay Velopack: nada se descarga ni se ejecuta fuera de la Store. | Nada. |
| **IA generativa** | 11.16 | Muesca no usa ningún modelo: enseña el estado y las peticiones de permiso del Claude Code del propio usuario. | En *Product declarations*, no marcar «generative AI»; explicarlo en las notas. |
| **Las dos ediciones instaladas a la vez** | — | Comparten el mutex de instancia única (solo corre una) y las dos reconocen `claudebar-helper` y `muesca-helper` como suyos, para que pasar de una a otra reescriba solo nuestras entradas. Pero cada una, al arrancar, apunta los hooks a su helper; desinstalar la de GitHub (hook de Velopack) quita también los de la Store; el helper de GitHub no se fía de la app de la Store (permisos al diálogo de Claude Code), y los snapshots de uno no los lee la otra. | Documentarlo en el README y en la ficha si hace falta: «no instales las dos a la vez». Si se quiere separar, que cada edición solo reescriba sus propias entradas (otra rama). |
| **Reapertura al iniciar sesión** | — | `RegisterApplicationRestart` (para volver tras una actualización de la Store) también marca la app como «reiniciable»: con *Guardar automáticamente mis apps reiniciables y reiniciarlas al iniciar sesión* activado, Windows podría reabrirla tras un reinicio aunque «Iniciar con Windows» esté apagado. **Mitigado:** la app ya no se registra al arrancar. Solo se registra al recibir el aviso de cierre por actualización (`WM_QUERYENDSESSION` con `ENDSESSION_CLOSEAPP` y sin `ENDSESSION_LOGOFF`) y se desregistra (`UnregisterApplicationRestart`) con cualquier otro aviso (apagar, reiniciar, cerrar sesión) y si el cierre se cancela (`WM_ENDSESSION` con `wParam` = FALSE). Así, durante la sesión normal nunca está registrada. Código: `StoreRestart` (lógica pura, `StoreRestartTests`) y `UI/SessionEndWindow`. | **Comprobar en Windows 10 y 11** (abajo, «Cómo probarlo», pasos 10 y 11): que tras una actualización de la Store se vuelve a abrir (la documentación dice que el registro en `WM_QUERYENDSESSION` vale; si no, el log dice «Windows la cierra para actualizarla» y no vuelve) y que con el ajuste de Windows encendido e «Iniciar con Windows» apagado no se abre al iniciar sesión. |
| **Tipo de cuenta y nombre del editor** | 10.14 | Una cuenta individual publica «under your own name»; una de empresa, «under the name of a registered legal business entity». «A company account is required […] if a reasonable consumer would interpret your application or publisher name to be that of a business entity». | Comprobar al registrarse si la cuenta individual admite «Bekay» como nombre para mostrar (el perfil se rellena con los datos del documento de identidad y el nombre no se puede cambiar después). Si no, `PublisherDisplayName` del manifiesto tiene que ser el que dé Partner Center, y «by Bekay» queda solo como marca dentro de la app. |

### Privacidad: letras y edición de la Store

**Hecho:** `PRIVACY.md` y `PRIVACY.es.md` (la URL de la ficha) tienen ya las dos secciones, a partir de este texto (con la
ruta exacta del ajuste, que lrclib.net ve la IP como cualquier servidor y el enlace a la declaración de privacidad de
Microsoft):

> **Lyrics (optional, Pro).** When synced lyrics are on, Muesca sends the title and artist of the song that is playing to
> [lrclib.net](https://lrclib.net) to download its lyrics. Nothing else is sent, and you can turn lyrics off in the notch
> settings.
>
> **Microsoft Store edition.** The Store edition checks your Pro license and processes purchases through the Microsoft
> Store (Windows.Services.Store); Microsoft handles the payment. It never checks GitHub for updates: the Store updates it.

> **Letras (opcional, Pro).** Con las letras sincronizadas activadas, Muesca manda a [lrclib.net](https://lrclib.net) el
> título y el artista de la canción que suena para bajar su letra. No manda nada más, y las letras se apagan en los ajustes
> del notch.
>
> **Edición de Microsoft Store.** La edición de la Store comprueba la licencia del Pro y hace las compras con Microsoft
> Store (Windows.Services.Store); el pago lo gestiona Microsoft. No busca actualizaciones en GitHub: la actualiza la Store.

### Notas para certificación (texto para *Submission options › Notes for certification*)

```
Muesca is a desktop companion for Claude Code (Anthropic's coding tool). It shows the user's Claude Code quota, the
state of their Claude Code sessions and Claude Code's permission requests in a notch at the top of the screen and in the
system tray. Muesca is an independent app; it is not affiliated with Anthropic.

HOW TO TEST WITHOUT A CLAUDE ACCOUNT
Claude Code is required for live data. Without it, every feature can be reviewed with sample data:
1. On first launch the welcome wizard opens (profiles, settings, what each feature does).
2. Right-click the tray icon > "Tutorial": an interactive tutorial runs a demo notch with sample data for every feature
   (sessions, Allow/Deny permissions, music and lyrics, tasks, file shelf, usage charts, focus timer). Nothing is sent to
   Claude.
3. Pro: the 7-day trial starts automatically on first launch. The "Pro" durable add-on (product ID "Pro") is bought from
   the Pro card in the notch or from the tray menu > "Muesca Pro…", with Windows.Services.Store. "Restore purchase"
   re-reads the license.

CAPABILITIES
runFullTrust: Muesca is a Win32 (WinForms, .NET) desktop app that runs at medium integrity. It declares no other
capability.
App execution alias "muesca-helper.exe": a small helper inside the package that Claude Code launches on each Claude Code
event (hooks) and for its status line, when the user turns on live sessions. The helper only forwards session state to
the app through a local named pipe and saves the quota windows Claude Code passes to it.
Startup task "MuescaStartup": disabled by default; enabled only if the user chooses "Start with Windows".

DATA AND NETWORK
- Reads Claude Code's local OAuth token (~/.claude/.credentials.json), read-only and in memory, only to request the user's
  own quota and plan from api.anthropic.com. It is never stored, logged or sent anywhere else. It can be turned off in
  Settings > Quota source. Muesca never asks the user for credentials or payment information.
- Anonymous usage count: once a day, version, edition and language only, no identifiers; can be turned off.
- Optional synced lyrics (Pro): song title and artist are sent to lrclib.net.
- Public service status (status.claude.com) and public model prices (models.dev, can be turned off).
- No ads. No generative AI in the product: it displays the status of the user's own Claude Code.
Privacy policy: https://github.com/Yovancas/muesca/blob/main/PRIVACY.md

CLAUDE CODE SETTINGS
Only when the user turns on live sessions, permissions or the status line, Muesca adds its own entries to Claude Code's
settings.json (~/.claude/settings.json), after making a timestamped backup, and removes only those entries when they are
turned off. The Store description asks users to turn these off before uninstalling.
```

## Cómo probarlo en Windows

**Tests:** `dotnet test ClaudeBarWin.sln -c Release`. Nuevos: `StoreHelperTests`, `StoreEditionTests`, `MsixPackageTests`,
`StoreListingTests`, `HelperPackageTests` y los casos con identidad de `PermissionClientTests`; y de los arreglos antes del
lanzamiento, `StoreRestartTests`, `SingleFlightTests`, `SharedSourcesTests`, `StatuslineShellTests` (ejecuta el enganche en
Git Bash y PowerShell, con y sin helper), `ClaudeIntegrationTests` y `PrereleaseLocalizationTests`. Aquí (Linux) se han pasado
los del helper y la lógica pura de los demás compilada aparte; los de la app enteros no se pueden pasar fuera de Windows.

**Paquete de prueba:**
1. `pwsh installer/msix/build-msix.ps1 -Version 1.0.0 -TestSign` (sin la identidad de Partner Center usa la de pruebas).
2. En una consola de administrador, confiar en el certificado (el script dice el comando) y `Add-AppxPackage` del bundle
   de `dist\msix\prueba`.
3. Opcional: `-TestSign -Wack` (administrador) y revisar el informe.

**Qué mirar** (con la edición de GitHub desinstalada, o al menos cerrada):
1. Arranca desde Inicio como «Muesca», con su icono; no hay «Muesca helper» en Inicio. En el log
   (`%LOCALAPPDATA%\Packages\<familia>\LocalCache\Roaming\ClaudeBarWin\claudebar.log` si el archivo es nuevo):
   «edición de la Store: paquete …, helper …\WindowsApps\<familia>\muesca-helper.exe» y ningún aviso de `LocalFolder`.
2. `Get-ChildItem $env:LOCALAPPDATA\Microsoft\WindowsApps -Recurse -Filter muesca-helper.exe`: el alias está en la raíz y
   en la carpeta de la familia. **Si no está en la de la familia, avisa**: la app no usa la general y se quedaría sin helper.
3. Activar las sesiones en vivo: `~/.claude/settings.json` apunta al alias. Abrir Claude Code: el notch enseña la sesión
   (comprueba que el helper lanzado por el alias cuelga de Claude Code en el árbol de procesos, que es lo que mira
   `PermissionPipeServer`).
4. Activar los permisos desde el notch y pedir uno en Claude Code: se contesta desde el notch. Con Claude Code elevado y
   Muesca normal, sale el diálogo de siempre.
5. Activar la statusline con Git Bash instalado: la cuota se actualiza sola con cada mensaje (el helper, sin identidad,
   escribe en `…\Packages\<familia>\LocalState\ClaudeBarWin\ratelimits`). Repetir sin Git Bash (PowerShell).
6. Apagar el alias en *Alias de ejecución de aplicaciones* y reiniciar Muesca: los hooks pasan al script de PowerShell;
   anotar si el alias de la carpeta de la familia sigue existiendo.
7. «Iniciar con Windows» en Ajustes: aparece «Muesca» en *Configuración › Aplicaciones › Inicio*; cerrar sesión y volver:
   arranca (en el log, «inicio con Windows»). Apagarlo desde el Administrador de tareas: el interruptor sale apagado y al
   pulsarlo abre Configuración.
8. El menú de la bandeja no tiene «Buscar actualizaciones». Pro: la prueba de 7 días empieza sola; la compra con una
   cuenta de prueba de Partner Center (necesita el paquete asociado a la app de Partner Center).
9. **Quitar la integración** (Ajustes, debajo de «Sesiones en vivo»): con sesiones en vivo, permisos y statusline
   enganchada, el botón pide confirmación y deja `settings.json` sin nada de Muesca (hooks ajenos y la statusline del
   usuario intactos; hay una copia `settings.json.claudebar-bak-…`). Sin nada instalado, lo dice y no escribe nada.
   Desinstalar: el paquete y `…\Packages\<familia>` desaparecen. Si no se quitó antes la integración, las entradas se
   quedan, pero la statusline del usuario sigue funcionando (el `if [ -e … ]` cae a la suya) y los hooks fallan sin
   bloquear Claude Code (riesgo conocido, arriba).
10. Actualizar con la app abierta (subir una versión de prueba, o `Add-AppxPackage -ForceApplicationShutdown` de un bundle
    con la versión siguiente): la app se cierra sola (en el log, «fin de sesión (actualización)» y «se volverá a abrir
    después») y vuelve a abrirse ya actualizada; el icono de la bandeja no se queda huérfano.
11. Con *Configuración › Cuentas › Opciones de inicio de sesión › Guardar automáticamente mis apps reiniciables…* encendido e
    «Iniciar con Windows» apagado: reiniciar el equipo con Muesca abierta. Al volver, **no** se abre sola (en el log de la
    sesión anterior, «fin de sesión (apagado)»). Repetir cerrando sesión.

## Fuentes

learn.microsoft.com no se puede abrir desde el entorno en el que se escribió esto: se leyó su fuente Markdown oficial en
los repositorios `MicrosoftDocs/msix-docs`, `MicrosoftDocs/windows-dev-docs`, `MicrosoftDocs/winrt-related` y
`MicrosoftDocs/winrt-api` (rama `docs`, la publicada). Las citas son de ahí; los enlaces, a la página publicada.

- [Understanding how packaged desktop apps run on Windows](https://learn.microsoft.com/en-us/windows/msix/desktop/desktop-to-uwp-behind-the-scenes)
- [Flexible virtualization](https://learn.microsoft.com/en-us/windows/msix/desktop/flexible-virtualization)
- [App capability declarations](https://learn.microsoft.com/en-us/windows/uwp/packaging/app-capability-declarations)
- [desktop6:FileSystemWriteVirtualization](https://learn.microsoft.com/en-us/uwp/schemas/appxpackage/uapmanifestschema/element-desktop6-filesystemwritevirtualization)
- [Integrate your desktop app with Windows by using packaging extensions](https://learn.microsoft.com/en-us/windows/apps/desktop/modernize/desktop-to-uwp-extensions) (alias, tarea de inicio, reinicio tras actualizar)
- [uap3:AppExecutionAlias](https://learn.microsoft.com/en-us/uwp/schemas/appxpackage/uapmanifestschema/element-uap3-appexecutionalias), [desktop:ExecutionAlias](https://learn.microsoft.com/en-us/uwp/schemas/appxpackage/uapmanifestschema/element-desktop-executionalias), [desktop:StartupTask](https://learn.microsoft.com/en-us/uwp/schemas/appxpackage/uapmanifestschema/element-desktop-startuptask), [Application](https://learn.microsoft.com/en-us/uwp/schemas/appxpackage/uapmanifestschema/element-application)
- [StartupTask class](https://learn.microsoft.com/en-us/uwp/api/windows.applicationmodel.startuptask)
- [Microsoft Store Policies](https://learn.microsoft.com/en-us/windows/apps/publish/store-policies)
- [App package requirements](https://learn.microsoft.com/en-us/windows/apps/publish/publish-your-app/msix/app-package-requirements), [Upload app packages](https://learn.microsoft.com/en-us/windows/apps/publish/publish-your-app/msix/upload-app-packages)
- Cygwin, `winsup/cygwin/path.cc` (`check_reparse_point_target`, alias de ejecución como enlaces simbólicos) — no es de
  Microsoft; solo explica por qué Git Bash puede lanzar el helper sin identidad.

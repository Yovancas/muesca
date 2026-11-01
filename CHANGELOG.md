# Changelog

Todos los cambios relevantes de Muesca (antes ClaudeBar for Windows) se documentan en este archivo.

El formato sigue [Keep a Changelog](https://keepachangelog.com/es-ES/1.1.0/) y el proyecto usa
[Versionado Semántico](https://semver.org/lang/es/).

Las notas largas que ve el usuario en la ventana de actualización viven en `installer/notes/<versión>.md`
(las firma y embebe `scripts/release.ps1`); este archivo es el resumen para desarrolladores.

## [Unreleased]

Pendiente de publicar. La versión del código ya es **0.4.0** (`ClaudeBarWin.csproj`) e incluye la
**0.3.10**, que nunca llegó a etiquetarse. Notas de usuario: `installer/notes/0.4.0.md` y
`installer/notes/0.3.10.md`.

### Ahora se llama Muesca
- **ClaudeBar for Windows pasa a llamarse Muesca** («notch», en español), y la publica **Bekay**. «Claude» es una marca
  de Anthropic y el producto no puede llamarse así; la relación con Claude Code se dice de forma descriptiva («Muesca, el
  notch para Claude Code»). Decisiones y lista completa en `docs/branding.md`.
  - **Todo lo que se ve**: los textos en los 8 idiomas (también el tutorial), la bandeja y sus globos, los títulos de
    ventana, la cabecera del panel, la bienvenida, el notch, la tarjeta «Muesca Pro», los avisos y los errores
    (`Services/Brand.cs`; `BrandingTextTests` vigila que ningún texto diga «ClaudeBar»).
  - **«Acerca de»** (Ajustes y bandeja): «Muesca, el notch para Claude Code · de Bekay» y «No afiliado a Anthropic.
    Claude es una marca de Anthropic.».
  - **Logo nuevo** pintado en vector con GDI+ (`UI/BrandMark.cs`, el de `branding/muesca.svg`) en la bienvenida, el notch
    que baja al empezar, el tutorial y el splash; icono del exe `branding/muesca.ico`.
  - **Acento violeta** en lugar del naranja de Claude: `#7B4DFF` como relleno con texto blanco (4,8:1), `#B49BFF` como
    texto en oscuro y `#6236F0` en claro (AA). Los colores de estado no cambian; el foco del pomodoro sigue separado de
    los descansos (`FocusPalette`). El hover de los botones con el acento ya no les quita contraste.
  - **Metadatos**: Product, Company (Bekay), Description y Copyright del exe y del helper; en Velopack, `--packTitle
    Muesca` (menú Inicio, «Aplicaciones» y el lanzador `Muesca.exe`) y `--packAuthors Bekay`; el puente y el instalador
    de Inno heredado, con el nombre y los mensajes nuevos.
  - **Repositorio `Yovancas/muesca`** (GitHub redirige `claudebar-win`): enlaces, User-Agent y
    `SignedGithubSource.RepoUrl`. La 0.3.9/0.4.0 siguen encontrando la actualización por la URL vieja.
  - **Sin cambios a propósito** (instalaciones, hooks y configuraciones que ya existen): namespaces y ensamblado
    `ClaudeBarWin`, `%APPDATA%\ClaudeBarWin`, el `packId` de Velopack, `claudebar-helper.exe`, las variables
    `CLAUDEBAR_*`, los hooks y la clave de firma.
  - README y README.es reescritos (logotipo para fondo claro y oscuro, funciones, capturas de `OnboardingShotsTests`),
    y PRIVACY, SECURITY, CONTRIBUTING, plantillas y THIRD-PARTY-NOTICES al día.

### Antes del lanzamiento (revisión de código)
- **Edición de la Store, cierre:** al cerrar sesión, apagar o actualizar desde la Store, la app se cierra con normalidad
  **dentro** de `WM_ENDSESSION` (`UI/SessionEndWindow`, en lugar de `SystemEvents.SessionEnded` + `PostToUi`, que Windows
  podía no llegar a ejecutar): guarda lo pendiente (el pomodoro y la página Uso) y quita el icono. `ExitApp` corre una vez.
- **Reapertura solo tras actualizar:** `RegisterApplicationRestart` ya no se llama al arrancar; se registra con el aviso de
  cierre por actualización (`ENDSESSION_CLOSEAPP`) y se quita con cualquier otro (`StoreRestart`). Con «reiniciar apps al
  iniciar sesión» de Windows, Muesca ya no se abre sola si «Iniciar con Windows» está apagado.
- **«Iniciar con Windows» (Store) sin congelar la UI:** encender y apagar van en segundo plano (el interruptor dice
  «Cambiando…»), y hasta la primera lectura el estado es desconocido («Comprobando…»), no «apagado»: la bienvenida ya no
  puede apagar una tarea encendida por no saber cómo estaba.
- **Statusline resistente:** si falta el helper (Muesca desinstalada sin apagarla), la statusline del usuario recibe el JSON
  directo, en Git Bash y en PowerShell; el arranque pasa a este formato el enganche de antes.
- **Ajustes › «Quitar la integración con Claude Code»:** hooks y statusline fuera de `settings.json` de una vez, con copia
  y confirmación, en los 8 idiomas; la ficha de la Store lo pide antes de desinstalar.
- **Pomodoro:** el hover del botón principal ya no baja el texto blanco sobre el violeta de AA (`ColorMath.HoverFill`).
- **Logo:** la bienvenida y el tutorial lo pintan una vez por tamaño y DPI (`BrandMarkCache`), no en cada frame.
- **App y helper** comparten un solo archivo para la carpeta de datos y la identidad de paquete (`Shared/PackageShared.cs`).
- **Release:** `scripts/release-velopack.ps1` para antes de firmar si el repositorio de destino no existe o es un nombre viejo.
- **Privacidad:** `PRIVACY.md` nombra las letras de lrclib.net (Pro) y la edición de la Store.
- Fuera el código muerto `StatuslineInstaller.Marker` y `HookInstaller.HelperMarker`.

### Añadido
- **Tutorial interactivo** (`Services/Tutorial`, `UI/Tutorial`): una ventana con el estilo de la bienvenida y, dentro, un
  notch de demostración pintado con el painter de verdad y datos de ejemplo (nunca los del usuario; nada se manda a
  Claude). Diez capítulos y 37 pasos: el notch y sus caras, sesiones, permisos (simulados, con los botones que se arman),
  páginas, música y letras, tareas y recordatorios, bandeja de archivos (soltando un archivo de ejemplo), uso, foco y la
  bandeja de Windows con el panel. Cada paso se completa haciendo el gesto de verdad (o con Intro / las flechas) y se
  marca con un visto que entra con el muelle de atención; siempre hay «Saltar paso», «Atrás» y «Salir». Las funciones
  Pro se prueban con su chip y, al acabar un capítulo sin el Pro, una línea discreta con el enlace a la tarjeta. Se abre
  al final de la bienvenida («Ver el tutorial al terminar (2 min)»), desde el menú de la bandeja y desde Ajustes › Acerca
  de; los capítulos hechos se guardan en `config.json` (`TutorialChaptersDone`) para retomarlo. Textos en los ocho
  idiomas; «reducir movimiento» cambia las animaciones por saltos; 0 % de CPU quieto. La máquina de pasos es pura y
  tiene tests exhaustivos. La ventana y la forma del notch grande pasan a `LayeredNotchForm` y a `OnboardingPainter`
  (partial), compartidas con la bienvenida.
- **Página «Foco» en el notch: un pomodoro** (diseño propio). Fases de foco (25 min), descanso corto (5) y largo (15)
  cada 4 focos; Empezar / Pausa / Reanudar, saltar y reiniciar; los focos de hoy. Con el pomodoro en marcha, el notch
  cerrado enseña un mini anillo con lo que queda en el color de la fase (reposo, vista previa, junto al % de Trabajando
  y la raya de Mínimo), sin tapar nunca un aviso. Al acabar una fase el notch se abre un momento con «Empezar
  descanso» / «Empezar foco» (en la cola, detrás de los recordatorios) y suena un aviso discreto que se puede apagar.
  El tiempo sale de un reloj monotónico y de la hora de fin guardada en `config.json` (`NotchFocusState`): sobrevive a
  una suspensión y a reiniciar la app. 0 % de CPU en reposo: repinta cuando cambia el segundo (página a la vista) o el
  minuto (notch cerrado). Pro (`ProFeature.NotchFocus`): enlazar el foco con una tarea (cada foco completo se apunta en
  ella), las estadísticas de la semana y las duraciones personalizadas en Ajustes.
- **Página «Uso» en el notch** (`docs/superpowers/specs/2026-10-30-notch-pagina-uso-design.md`): cuota 5 h / 7 d con
  su reinicio y su ritmo, Opus/Sonnet y uso extra, plan y salud de Claude, y una gráfica de gasto por modelo (áreas
  apiladas) o del % real de la cuota (umbrales de los avisos y proyección del ritmo) con rangos 1H…30D. Con el Pro
  (`ProFeature.NotchUsage`): hover con tooltip, morphing al cambiar de rango, proyección y series alternables con la
  leyenda o la rueda; sin él, la gráfica quieta con el chip «Pro». Clic en un anillo del reposo = la página con esa
  ventana. La superficie del notch pasa de 200 a 272 px lógicos de alto.
- **Edición gratis y ClaudeBar Pro** (`docs/superpowers/specs/2026-10-30-gratis-y-pro-design.md`): la app de
  bandeja y el notch básico siguen gratis; Permitir / Denegar desde el notch, música y letras, tareas, bandeja de
  archivos y varios monitores son Pro (`Services/Licensing`, todo pasa por `ProGate`). Edición de la Store
  (identidad de paquete): complemento duradero «Pro» con `Windows.Services.Store` y 7 días de prueba; edición
  GitHub: candado con «Conseguir Pro en Microsoft Store». Candado y tarjeta dentro del notch, desbloqueo en
  caliente, un único aviso al acabar la prueba, «ClaudeBar Pro…» en la bandeja y lo Pro marcado en el
  asistente. Sin el Pro los datos de tareas y bandeja no se tocan. Override `CLAUDEBAR_PRO` solo en Debug.
- **Cuota desde Claude Code (0 peticiones)**: ClaudeBar se engancha delante de la statusline y lee
  las ventanas de 5 h y 7 d que Claude Code ya le pasa. Mientras hay sesión abierta solo se consulta
  la API cada 15 min. Si no había statusline, pone una compacta (`5h 23% | 7d 41%`); al desactivarlo
  se restaura la original exacta, con copia de seguridad de `settings.json`.
- **`claudebar-helper.exe` nativo (NativeAOT)** para hooks y statusline: sustituye a lanzar
  PowerShell en cada evento. Los hooks se migran solos al arrancar.
- Snapshot por sesión en `%LOCALAPPDATA%\ClaudeBarWin\ratelimits\`, con fusión «ventana más nueva,
  mayor %».
- Diagnóstico `ClaudeBarWin.exe --tap-test` (sin llamar a la API).
- **Instalador con perfiles**: pregunta para qué lo vas a usar (Solo cuota, Programo con Claude
  Code, Notch completo, Minimalista / juego o Personalizado) y propone ajustes según el equipo
  (monitores, escala DPI y si está Claude Code): idioma, monitor, tamaño, abrir con hover o clic e
  iniciar con Windows. Ofrece las sesiones en vivo (marcadas solo con «Programo con Claude Code»: instalan
  hooks en `~/.claude/settings.json`) y explica los permisos desde el notch, que son opcionales. En una
  actualización, «Mantener mi configuración» viene marcado. En silencioso (NetSparkle, winget) no
  pregunta nada, como antes. Textos en español e inglés. Detalles en `docs/instalador-perfiles.md`.
- `ClaudeBarWin.exe --apply-setup <json>`: aplica lo elegido en el instalador sobre `config.json`.
  Con config previa, antes guarda `config.json.bak-setup-<fecha>` y solo cambia lo elegido. Si
  `config.json` no se puede leer o guardar no toca nada, y el instalador avisa cuando no se aplicó.
- **Asistente de bienvenida dentro del notch**: en el primer arranque (`OnboardingPending`) el notch
  baja del borde de arriba y se despliega en grande con las páginas del instalador (perfiles en
  tarjetas con vista previa en vivo del notch, qué se activa, ajustes recomendados, Claude Code y
  resumen). Teclado completo (Tab, Enter, Esc = saltar con confirmación). Al terminar aplica con el
  núcleo de `--apply-setup` y se encoge hasta el notch de reposo, que aparece en su sitio. Con
  reducir movimiento, solo fundidos. Desde la bandeja: «Volver a configurar…». Detalles en
  `docs/asistente-notch.md`.
- **Aviso de versión nueva en el notch** (`NotchHost.SetUpdateOffer`): un punto de acento en reposo
  y, en el expandido, «Nueva versión 0.6» con el anillo de la descarga o el botón «Actualizar»
  (solo en la ventana interactiva).
- Log de diagnóstico local en `%APPDATA%\ClaudeBarWin\claudebar.log` (sin tokens). Se respeta
  `CLAUDE_CONFIG_DIR`.
- **Ajustes → Origen de la cuota → «Leer la cuota con tu sesión de Claude Code (no oficial)»**
  (activado por defecto), con una línea que explica qué hace con el token. Apagado, ni se abre
  `.credentials.json` ni se llama a `/api/oauth/*`: la cuota sale solo de la statusline y, sin sesión
  abierta, el panel dice «Última lectura hace X min»; el plan, el último de la API de perfil o «Plan
  desconocido».
- Apartado «Privacidad y datos» en `SECURITY.md` y en los README.
- Repo: CI en GitHub Actions (build + tests en Windows con resultados `.trx` y cobertura),
  `Directory.Build.props`, `Directory.Packages.props` (versiones NuGet centralizadas), `global.json`,
  `.editorconfig`, Dependabot, plantillas de issues y PR, `SECURITY.md`, `CONTRIBUTING.md` y este
  `CHANGELOG.md`.
- **Edición de Microsoft Store (MSIX)** (`docs/msix.md`, `docs/store-submission.md`): con identidad de paquete, el helper
  no se copia a AppData (MSIX la virtualiza) y Claude Code lo lanza por el alias del paquete `muesca-helper.exe`; los
  snapshots van a la carpeta de datos del paquete; el helper con identidad solo se fía de la app de su paquete; «Iniciar
  con Windows» por la tarea de inicio del paquete; sin Velopack (actualiza la Store) y con reinicio tras actualizar.
  `installer/msix/` (manifiesto, logos reproducibles, `build-msix.ps1` con `.msixupload` y firma de prueba) y la ficha de
  la Store en los 8 idiomas con sus capturas (`store/`). La edición de GitHub no cambia.

### Cambiado
- **Instalación y actualizaciones con Velopack** (para la 0.5; detalles en `docs/velopack-migracion.md`):
  - Instalar son 2-3 s, sin asistente y con pantalla de carga; la app va en `%LOCALAPPDATA%\ClaudeBar` (los
    datos y el helper siguen en `%LOCALAPPDATA%\ClaudeBarWin`, fuera del alcance de Update.exe).
    El asistente de perfiles se muda a la app: la primera instalación solo marca `OnboardingPending` en
    `config.json` (y solo si no había config).
  - Las actualizaciones se bajan en segundo plano, con parches delta, y se aplican al pulsar «Reiniciar
    para actualizar» o al cerrar la app. El menú dice en qué anda (buscando, descargando con %, lista).
  - La firma se mantiene: cada release lleva `releases.win.json` firmado con la misma clave Ed25519 que
    NetSparkle, y la app descarta las releases que no verifican.
  - Al desinstalar se quitan los hooks, la statusline, el helper y «Iniciar con Windows», como con Inno.
  - «Iniciar con Windows» pasa a la clave Run, con el lanzador estable (`ClaudeBar.exe --autostart`); el
    acceso directo de la carpeta Inicio de la 0.4 se migra solo, respetando si estaba apagado.
  - Quien tiene la 0.3.9 o la 0.4.0 pasa solo con un instalador puente (`installer/bridge/`) que llega por
    NetSparkle, instala Velopack y retira la instalación de Inno sin tocar la config ni los hooks.
  - Publicar: `scripts/release-velopack.ps1` (no sube nada sin `-Upload`); `scripts/release.ps1` queda de
    referencia y ya no publica.
- El plan («Pro», «Max · 5x»…) se pregunta a la API de perfil de Anthropic al arrancar y cada hora,
  en vez de leerse solo de `.credentials.json` al inicio.
- Backoff frente al 429 del límite de la cuenta (5 → 10 → 20 … hasta 60 min), respetando
  la espera que pide el servidor; tocar ajustes ya no gasta peticiones.
- ClaudeBar **ya no renueva el token ni escribe `.credentials.json`**: podía chocar con la renovación
  de Claude Code y forzar un nuevo inicio de sesión.
- `claude -p .` (forzar la renovación del token caducado) ya no es automático: gasta un mensaje de tu
  cuota. Ahora es el ajuste «Forzar renovación con la CLI (gasta un mensaje)», apagado por defecto.
- El token OAuth se trata como secreto de punta a punta:
  - De `.credentials.json` solo se leen `accessToken` y `expiresAt` (nunca el `refreshToken`), justo
    antes de cada petición.
  - Solo viaja por HTTPS a `api.anthropic.com`, sin seguir redirecciones, con timeout corto y
    respuesta acotada.
  - Se imprime como `Creds(***)` y el log tapa más formas de token.
- Un token caducado o rechazado no se vuelve a probar hasta que Claude Code reescribe
  `.credentials.json`. Entre dos peticiones de cuota pasan al menos 55 s.
- Gasto y gráfica incrementales: solo se lee lo nuevo de los transcripts, no la semana entera.
- Hook más barato: la mitad de eventos por herramienta y sin pasar por Git Bash; se actualiza al
  arrancar y se quita al desinstalar.
- Tests: `CodexAdversarialTests` y `CodexV04ReviewTests` renombrados por área
  (`HookPipeAndTranscriptRegressionTests`, `ClaudeSettingsIntegrationTests`).

### Corregido
- Tutorial: el capítulo 1 decía «resetea en reseteando…» porque los reinicios de los datos de ejemplo se contaban con el
  reloj de la máquina; ahora, con el de la demo (`NotchFrame.Clock`).
- Tutorial: el paso 1 («En reposo») empezaba con el notch de demostración abierto si el cursor estaba donde aparece; ahora
  empieza en reposo y se abre al pasar el ratón, que es lo que completa el paso.
- Auto-actualización en bucle de la v0.3.9 (el `+sha` de `InformationalVersion` hacía que la versión
  instalada pareciera más antigua).
- El plan no se actualizaba tras pasar de Pro a Max.
- El panel quedaba destruido tras Alt+F4 o un cierre externo; ahora solo se oculta.
- `config.json` con errores ya no se pierde (se guarda como `config.json.bad`) y los valores fuera de
  rango no impiden arrancar.
- Sesiones en vivo: «Silenciar si la terminal tiene el foco» funciona, las sesiones cerradas sin
  despedirse desaparecen solas, las que esperan respuesta no se borran a los 10 min, el aviso de
  permiso ya no pasa a «esperando input» y las rutas con tildes se ven bien.

### Eliminado
- NetSparkle (`SparkleUpdateService` y sus paquetes) y su ventana de actualización: lo sustituye Velopack.
- `SmokeTest.cs` (solo hacía `Assert.True(true)`).
- Letras del notch: las fuentes NetEase (API no oficial) y Musixmatch, con sus ajustes (y el cifrado DPAPI de
  la clave, que solo usaba Musixmatch); solo queda LRCLIB. Un `config.json` con esos ajustes se carga sin
  errores y los pierde al guardar; lo que la caché de letras tenga de esas fuentes se vuelve a pedir a LRCLIB.

## [0.3.9] - 2026-06-12

Reúne el sprint de auditoría (0.3.8, nunca publicada por separado) y el pulido de UX.

### Añadido
- Familias de modelos dinámicas (Fable, Mythos y las que vengan) con nombre y color propios.
- Catálogo de tarifas desde models.dev con caché local, copia offline embebida y un interruptor de
  privacidad («Actualizar tarifas online»).
- Líneas de umbral en la gráfica en modo %.

### Cambiado
- El color de la barra de cuota sube con el % real (verde → ámbar → rojo); el ritmo va en el marcador.
- La línea de ritmo colorea cada ventana (5 h / 7 d) por su propio estado.
- Contraste WCAG en todo el panel, escalado por DPI (125 / 150 %) y formatos por idioma.
- El panel de ajustes recuerda el scroll y ordena mejor los grupos.
- Escritura atómica de credenciales.

### Corregido
- El gasto con Fable salía a 0 €.
- «99+» en el icono de bandeja se lee entero y el aviso de pendientes recupera su forma.
- Barra «57 % en rojo / 84 % en verde» incoherente.

## [0.3.7] - 2026-06-04

### Añadido
- Tope de alto del panel de ajustes (~65 % del área útil) con scroll por rueda, compatible con el
  smooth-scrolling de los trackpads.

### Cambiado
- La mascota queda solo en talla compacta; el ajuste pasa a un interruptor «Mostrar mascota» (la
  configuración antigua migra sola).
- README (EN + ES) con capturas y GIFs nuevos.

### Corregido
- El icono de bandeja se recodificaba a una paleta de 16 colores (verde teal, ámbar oliva).

## [0.3.6] - 2026-06-03

### Cambiado
- Mascota grande rediseñada como gato *loaf*, con cola animada en las fases vivas.
- README reescrito de arriba abajo (EN + ES), con diagrama de flujo de datos y FAQ.

## [0.3.5] - 2026-06-03

### Cambiado
- Panel de ajustes rediseñado en una sola pantalla agrupada, con interruptores tipo pastilla y la
  mascota en un control de 3 estados.
- Mejor contraste y legibilidad en los tres temas.

### Corregido
- «Mostrar mascota» no hacía nada sin las sesiones en vivo activadas.
- Textos cortados con elipsis en subtítulos, valores y línea de ritmo.
- La cuota de la sesión se pintaba dos veces.

## [0.3.4] - 2026-06-03

Rediseño «tipo Apple» completo desde la 0.2.0.

### Añadido
- Microinteracciones: fade del panel, secciones escalonadas, números y barras animados, resaltado al
  pasar el ratón y destello al renovarse la cuota, todo sujeto a «Reducir movimiento».
- **Sesiones en vivo** (opt-in): mascota ASCII que reacciona a las sesiones de Claude Code mediante
  hooks y una *named pipe* local, con aviso ámbar en el icono de bandeja.
- Marcador de ritmo y marcas de umbral en la barra, reset en hora local, aviso de datos desfasados y
  sello de privacidad.
- Panel de ajustes ⚙ dentro del dashboard.

### Cambiado
- Sistema de diseño: rejilla de 8 pt, tipografía con dígitos tabulares y color interpolado por riesgo.
- Icono de bandeja adaptativo a barra clara/oscura y estado por forma (accesible a daltónicos).

### Corregido
- El sello de privacidad y el pie ya no se truncan.

## [0.3.3] - 2026-06-02

### Añadido
- Señales de un vistazo: marcador de ritmo, marcas de umbral, reset en hora local, estado desfasado
  con sello de privacidad e icono de bandeja adaptativo con estados por forma.

## [0.3.2] - 2026-06-02

### Cambiado
- Cimientos visuales: tokens semánticos de tema, tipografía, espaciado y `RiskColor`; el badge del
  icono de bandeja usa color por riesgo.

## [0.3.1] - 2026-06-02

### Añadido
- Dashboard rediseñado con modo Datos/Ajustes, cabecera de un vistazo y secciones plegables.
- Sesiones en vivo: servidor de *named pipe*, instalador de hooks idempotente con copia de seguridad,
  modo `--hook-test` y mascota ASCII.

### Cambiado
- Menú de clic derecho mínimo; la configuración pasa al panel de ajustes.

## [0.2.0] - 2026-06-01

### Añadido
- Auto-actualización con NetSparkle: appcast firmado (Ed25519), instalador Inno Setup per-user y notas
  de versión en la ventana de actualización.
- Histórico de % real con ritmo y ETA de agotamiento.
- Gráfica de gasto por modelo con ventana móvil de 1 h a 30 d.
- Temas Oscuro / Claro / CLI e importación de paletas `.itermcolors`.
- 9 idiomas, notificaciones por hitos (25/50/75/95 %), alertas de ritmo, estado del servicio de
  Claude y caché offline.

### Corregido
- La etiqueta de pico se solapaba con el total/% en las gráficas.
- `ObjectDisposedException` al abrir la ventana de actualización tras refrescar el icono.

## [0.1.0] - 2026-05-28

### Añadido
- Primera versión pública: monitor de bandeja para la cuota real de 5 h / 7 d de Claude Code, panel
  arrastrable con opacidad ajustable, temas, menú contextual y manifiesto de winget.

[Unreleased]: https://github.com/Yovancas/muesca/compare/v0.3.9...HEAD
[0.3.9]: https://github.com/Yovancas/muesca/compare/v0.3.7...v0.3.9
[0.3.7]: https://github.com/Yovancas/muesca/compare/v0.3.6...v0.3.7
[0.3.6]: https://github.com/Yovancas/muesca/compare/v0.3.5...v0.3.6
[0.3.5]: https://github.com/Yovancas/muesca/compare/v0.3.4...v0.3.5
[0.3.4]: https://github.com/Yovancas/muesca/compare/v0.3.3...v0.3.4
[0.3.3]: https://github.com/Yovancas/muesca/compare/v0.3.2...v0.3.3
[0.3.2]: https://github.com/Yovancas/muesca/compare/v0.3.1...v0.3.2
[0.3.1]: https://github.com/Yovancas/muesca/compare/v0.2.0...v0.3.1
[0.2.0]: https://github.com/Yovancas/muesca/compare/v0.1.0...v0.2.0
[0.1.0]: https://github.com/Yovancas/muesca/releases/tag/v0.1.0

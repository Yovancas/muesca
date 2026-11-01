# Marca: Muesca (by Bekay)

La app se llamaba **ClaudeBar for Windows**. «Claude» es una marca de Anthropic y la app no es de Anthropic, así que el
producto no puede llamarse así. Desde ahora se llama **Muesca** («notch», en español) y la publica **Bekay**. La relación
con Claude Code se dice de forma descriptiva: «Muesca, el notch para Claude Code». «Acerca de» (Ajustes y bandeja) y los
README llevan **«No afiliado a Anthropic. Claude es una marca de Anthropic.»**.

Los recursos de la marca están en [`branding/`](../branding/README.md): el logo (`muesca.svg`, la fuente de todo), el icono
(`muesca.ico`), PNG de 16 a 1024 px y los logotipos con el nombre para fondo claro y oscuro.

**La regla:** cambia todo lo que **ve** el usuario; lo **interno** se queda como estaba, porque lo llevan instalaciones,
hooks y configuraciones que ya existen y no se pueden migrar sin riesgo.

## Lo que ve el usuario: Muesca

| Qué | Dónde |
| --- | --- |
| Nombre, editor, URL del repositorio y colores del logo | `Services/Brand.cs` (`Brand.Name`, `Brand.Publisher`, `Brand.RepoUrl`…) |
| Textos (8 idiomas) y tutorial | `Services/Localization.cs`, `Services/TutorialStrings.cs`. `BrandingTextTests`: ningún texto dice «ClaudeBar» |
| «Acerca de»: qué es y el aviso de marca | `Strings.AboutProduct` / `AboutDisclaimer`; Ajustes (`DashboardSettingsView.NoteRow`) y la bandeja (`TrayAppContext.ShowAbout`) |
| Bandeja (texto del icono, globos), títulos de ventana, cabecera del panel, bienvenida | `Brand.Name` |
| Logo pintado (bienvenida, notch que baja, tutorial, splash del instalador) | `UI/BrandMark.cs`, en vector con GDI+ |
| Icono del exe, del acceso directo y de «Aplicaciones» | `ApplicationIcon` = `branding/muesca.ico`; `--icon` de vpk |
| Color de acento | `Theme.Accent` / `AccentText` / `OnAccent` (abajo) |
| Metadatos del exe (Administrador de tareas, Propiedades) | `Product`, `AssemblyTitle`, `Company` (Bekay), `Description`, `Copyright` del csproj |
| Menú Inicio, «Aplicaciones», lanzador | `--packTitle Muesca` (lanzador `Muesca.exe`), `--packAuthors Bekay` |
| «Denegar» desde el notch | Claude Code recibe «Denegado desde Muesca» |
| Puente desde Inno y el instalador de Inno heredado | Nombre, editor, URL y mensajes |
| Documentación | README, PRIVACY, SECURITY, CONTRIBUTING, CHANGELOG, plantillas de issues |

## Lo interno: no cambia (y por qué)

| Qué | Valor | Por qué se queda |
| --- | --- | --- |
| Namespaces, ensamblado y exe | `ClaudeBarWin`, `ClaudeBarWin.exe` | El helper solo acepta respuestas de permisos de un `ClaudeBarWin.exe` (`PermissionClient`); el `--mainExe` de Velopack, «Iniciar con Windows», el cierre de la app vieja y el puente lo buscan por ese nombre. Renombrarlo es un proyecto aparte sin beneficio visible |
| Carpeta de datos | `%APPDATA%\ClaudeBarWin` (config, historial, `claudebar.log`) | Ahí están la configuración y el historial de quien ya la usa |
| Carpeta del helper y snapshots | `%LOCALAPPDATA%\ClaudeBarWin` (`bin\`, `ratelimits\`) | Los hooks de `~/.claude/settings.json` apuntan a esa ruta |
| Id del paquete de Velopack | `--packId ClaudeBar` (carpeta `%LOCALAPPDATA%\ClaudeBar`, AppUserModelID `velopack.ClaudeBar`) | Es la carpeta y la identidad de la instalación; Velopack no sabe mudarla. Efecto visible: el instalador se descarga como `ClaudeBar-win-Setup.exe` |
| Helper | `claudebar-helper.exe` | Es el comando de los hooks y de la statusline en `settings.json`; `HelperCommand` reconoce las entradas por ese nombre exacto |
| Hooks y tuberías | `claudebar-hook.ps1`, tuberías `claudebar` y `claudebar-perm`, copias `.claudebar-bak-*` | Identifican lo nuestro en `settings.json` para actualizarlo y quitarlo sin tocar lo del usuario |
| Variables de entorno | `CLAUDEBAR_*` (`CLAUDEBAR_NO_STATS`, `CLAUDEBAR_PRO`, `CLAUDEBAR_UPDATE_REPO`, `CLAUDEBAR_DATA_DIR`…) | Las usan scripts, tests y quien ya las tiene puestas |
| Firma de las actualizaciones | La clave pública Ed25519 y el contexto `claudebar/velopack-manifest/v1` | Cambiar la clave exige publicar antes una versión con la nueva; el contexto es parte del formato firmado |
| Instancia única, «Iniciar con Windows» | Mutex `ClaudeBarWin_SingleInstance`, valor `ClaudeBarWin` de la clave Run | Los usa la versión vieja durante el puente y la entrada que ya existe |
| Instalación de Inno (0.3.9 / 0.4.0) | AppId, carpeta `Programs\ClaudeBarWin`, grupo y accesos `ClaudeBar.lnk` | Así la encuentran el puente y `LegacyInnoCleanup` para retirarla |
| User-Agent | `ClaudeBarWin/<versión>` (Anthropic, LRCLIB) y `ClaudeBar` (contador de uso) | Identifica el cliente, no se ve; el del contador no se cambia para no partir las cifras |
| Contador de uso | `claudebar-stats.claudebar-stats-worker.workers.dev` | Es el servidor desplegado |
| winget | `Yovancas.ClaudeBarWin` | Cambiar el id es dar de alta otro paquete (pregunta abierta, abajo) |

Si algún día se quiere cambiar algo de esta tabla, tiene que ir con su migración y sus tests, nunca dentro de un cambio
de marca.

## Cambiados justo a tiempo (y ya no se tocan)

La primera release con Velopack es la **0.5**: hasta entonces no existe ninguna instalación de Velopack. Por eso se ha
podido cambiar ahora, y a partir de esa release ya no se cambian:

- **`--packTitle`: «ClaudeBar» → «Muesca».** Es el nombre del acceso directo del menú Inicio y de «Aplicaciones», y el del
  lanzador estable de la raíz: comprobado en el código de vpk, `(PackTitle ?? PackId) + ".exe"`, así que
  `AppLocation.VelopackStubName` = `Muesca.exe` (y el puente lo abre).
- **`SignedGithubSource.RepoUrl` = `https://github.com/Yovancas/muesca`.** El repositorio va en lo que se firma
  (`claudebar/velopack-manifest/v1\nyovancas/muesca\n` + el manifiesto), y `scripts/release-velopack.ps1` firma con el
  mismo (`-GhRepo`, vigilado por `ReleaseScriptTests`).

## El repositorio pasa a `Yovancas/muesca`

GitHub redirige el nombre viejo de un repositorio renombrado (web, descargas de releases y API). Con eso:

- **0.3.9 y 0.4.0 (Inno + NetSparkle)** buscan la actualización en
  `https://github.com/Yovancas/claudebar-win/releases/latest/download/appcast.xml`, una URL que llevan dentro y no se
  puede cambiar. Tras el renombrado, GitHub la redirige a `Yovancas/muesca` y NetSparkle sigue la redirección. El appcast
  y el puente van firmados con la misma clave de siempre, que no depende del nombre del repositorio, y el appcast apunta
  al puente con la URL nueva (`--base-url https://github.com/$GhRepo/...`). **No hay que tocar nada en esas versiones.**
- **0.5 en adelante (Velopack)** preguntan directamente a `Yovancas/muesca` y verifican la firma con ese nombre.

Para que siga funcionando:

1. **Renombrar** el repositorio público (*Settings › General › Repository name*), no crear uno nuevo y archivar el viejo:
   sin el renombrado no hay redirección.
2. **No crear nunca otro repositorio llamado `claudebar-win`** en la cuenta: GitHub deja de redirigir en cuanto existe.
3. Publicar la siguiente versión **después** del renombrado (`vpk upload github --repoUrl` usa `Yovancas/muesca`) y, como
   hasta ahora, con `-Bridge`: cada release sigue llevando el `appcast.xml` para quien sigue en Inno. El script lo
   comprueba antes de compilar y de firmar (`Assert-TargetRepo`, con `gh repo view`): para si `Yovancas/muesca` no existe,
   si está archivado o si el nombre pedido redirige a otro (un nombre viejo: la firma llevaría un repositorio que la app no
   acepta). Con `-Upload` la comprobación es obligatoria; en local, solo si se va a firmar, y sin `gh` o sin red avisa.
4. El repositorio de desarrollo (`claudebar-win-dev`) y su CI no dependen del nombre público.

## Colores

| Token | Oscuro | Claro | Contraste |
| --- | --- | --- | --- |
| `Accent` (rellenos: botones, interruptores, chips, puntos, anillos, foco) | `#7B4DFF` | `#7B4DFF` | 3,7:1 sobre el fondo oscuro, 4,3:1 sobre el negro del notch, 4,6:1 sobre el claro (≥3:1 no textual) |
| `AccentText` (el acento como texto) | `#B49BFF` (el anillo del logo) | `#6236F0` | 7,7:1 y 6,1:1 (AA) |
| `OnAccent` (texto encima del acento) | blanco | blanco | 4,8:1 (AA) |

- El CLI conserva su verde. Los colores de estado (aviso, crítico, éxito) no cambian.
- El hover de los botones con el acento oscurece en vez de aclarar (`ColorMath.HoverFill`): aclarar el violeta bajaba el
  blanco de encima a ~4,1:1.
- El foco del pomodoro usa el acento; los descansos, verde y azul cielo (`FocusPalette`). El violeta y el azul cielo
  quedan a ~58° de tono; `FocusPaletteTests` exige al menos 40°.
- Los tests de contraste (`ThemeTokenTests`, `FocusPaletteTests`) lo vigilan; los de píxeles reconocen el acento con
  `BrandPixels.IsAccent`.

## El logo en la app

`UI/BrandMark.cs` pinta `branding/muesca.svg` con GDI+: el cuadrado con el degradado violeta → azul, el notch negro que
cuelga del borde de arriba (su borde sigue el arco de las esquinas del cuadrado, sin recortes por región, que en GDI+ no
llevan antialias) y los dos anillos con los extremos redondos. Los degradados imitan el `spreadMethod="pad"` del SVG y el
del anillo va girado, como el `rotate(-90)` del SVG. La geometría (`MuescaMarkLayout`) es pura y tiene tests
(`BrandMarkTests`). Para compararlo con los PNG de `branding/`: `OnboardingShotsTests` deja el logo a esos tamaños en
`SHOTS_DIR\marca\`.

## Preguntas abiertas

- **Acceso directo del escritorio heredado.** Quien instaló la 0.3.9/0.4.0 con «icono en el escritorio» conserva
  `ClaudeBar.lnk` (el puente y `LegacyInnoCleanup` lo reapuntan a `Muesca.exe` y le ponen la descripción «Muesca», pero no
  lo renombran). ¿Renombrarlo a `Muesca.lnk` en la migración?
- **Nombre del instalador.** Velopack llama al instalador `<packId>-win-Setup.exe` (`ClaudeBar-win-Setup.exe`). Se puede
  renombrar el asset al subirlo, pero hay que comprobar antes que `vpk upload` y el script no dependen del nombre.
- **winget.** ¿Nuevo paquete (`Bekay.Muesca`) o el mismo id con `PackageName: Muesca`? Los manifiestos de `winget/` son
  copias de los publicados (0.3.9) y no se han tocado.
- **LICENSE.** El copyright sigue a nombre de Yovan Castro; el del csproj y el editor son Bekay.
- **Microsoft Store.** La ficha y el complemento Pro se dan de alta con el nombre nuevo (`ProCatalog.StoreProductId` sigue
  siendo un marcador).
- **Contador de uso.** El texto del servidor (`server/stats-worker`) ya dice «Muesca»; hace falta volver a desplegarlo.
- **Capturas del panel.** `assets/dashboard-*.png`, `settings*.png` y los GIF del panel salen de `--render-demo`,
  `--render-test` y `--render-gif`, y hay que regenerarlos en Windows (ya salen en violeta y con «Muesca»). Las del notch
  y el README, con `OnboardingShotsTests` (`SHOTS_DIR\readme\` → `assets/readme/`).

# Contribuir a Muesca (antes ClaudeBar for Windows)

> **Licencia.** Desde la 1.0 el código es público pero no libre ([LICENSE](LICENSE)): puedes modificarlo y compilarlo
> para tu uso, pero no distribuirlo ni venderlo. Las contribuciones son bienvenidas por pull request. Al enviar una,
> concedes al autor permiso para usarla, cambiarla y distribuirla como parte de Muesca (LICENSE, punto 4).

## Requisitos

- Windows 10/11 x64.
- SDK de **.NET 9** (la versión exacta la fija `global.json`: 9.0.x, la banda más reciente instalada).
  `run.ps1` usa el SDK de `%USERPROFILE%\.dotnet` si existe y, si no, el `dotnet` del PATH.

## Estructura

| Proyecto                     | Qué es                                                              |
| ---------------------------- | ------------------------------------------------------------------- |
| `ClaudeBarWin.csproj`        | La app de bandeja (WinForms, `net9.0-windows10.0.19041.0`).         |
| `ClaudeBarWin.Helper`        | `claudebar-helper.exe`: hooks y statusline, compatible con NativeAOT. |
| `ClaudeBarWin.Tests`         | Tests xUnit de la app.                                              |
| `ClaudeBarWin.Helper.Tests`  | Tests xUnit del helper (incluye tests de proceso real).            |

Ajustes comunes en `Directory.Build.props`; versiones NuGet centralizadas en
`Directory.Packages.props` (los `.csproj` declaran `<PackageReference Include="..." />` **sin**
`Version`).

## Compilar y ejecutar

```powershell
.\run.ps1            # compila y ejecuta (Release)
.\run.ps1 build      # solo compila (Release)
.\run.ps1 -Test      # compila la solución y pasa todos los tests (también: .\run.ps1 test)
.\run.ps1 -Configuration Debug   # run/build/test en Debug (publish siempre es Release)
.\run.ps1 publish    # exe single-file self-contained en .\publish\
```

O directamente con el SDK:

```powershell
dotnet build ClaudeBarWin.sln -c Release
dotnet test  ClaudeBarWin.sln -c Release
```

`HelperProcessTests` lanza `claudebar-helper.exe` desde la carpeta de salida de los tests; el
`ProjectReference` lo copia allí al compilar la solución, así que basta con compilar antes de testear.

Cobertura (coverlet):

```powershell
dotnet test ClaudeBarWin.sln -c Release --collect "XPlat Code Coverage" --results-directory TestResults
```

## Tests

- Todo cambio de comportamiento lleva su test; los arreglos de bugs, un test de regresión que falle
  antes del arreglo.
- Nada de esperas fijas (`Thread.Sleep`/`Task.Delay` para «dar tiempo»): espera a una condición con
  timeout.
- Los tests que cambian estado global del proceso (variables de entorno como `CLAUDE_CONFIG_DIR`)
  van en una colección con `DisableParallelization = true`.
- Usa directorios temporales únicos (`Path.GetTempPath()` + GUID) y bórralos en `finally`/`Dispose`.

## Estilo

- `.editorconfig` define el formato: UTF-8, CRLF, 4 espacios, namespaces de archivo, campos privados
  `_camelCase`, tipos y miembros en PascalCase.
- **0 avisos**: en CI (`CI=true`) los avisos se tratan como errores.
- Comentarios y mensajes de commit en español, estilo
  [Conventional Commits](https://www.conventionalcommits.org/es/) (`feat:`, `fix:`, `test:`,
  `docs:`, `build:`, `ci:`, `chore:`…).
- Añade una línea en `CHANGELOG.md` (sección `Unreleased`) si el cambio lo nota el usuario.

## Nombre y marca

La app se llama **Muesca** y la publica **Bekay** (antes, *ClaudeBar for Windows*). Las reglas, en
[`docs/branding.md`](docs/branding.md):

- **Lo que ve el usuario dice «Muesca»**: textos de `Services/Localization.cs` y `Services/TutorialStrings.cs` en los 8
  idiomas, títulos de ventana, globos, instalador, README. Para el nombre en código, `Brand.Name`. `BrandingTextTests`
  falla si algún texto vuelve a decir «ClaudeBar».
- **Lo interno no cambia**: namespaces y ensamblado `ClaudeBarWin`, las carpetas `%APPDATA%\ClaudeBarWin` y
  `%LOCALAPPDATA%\ClaudeBarWin`, el `packId` de Velopack, `claudebar-helper.exe`, las variables `CLAUDEBAR_*`, los hooks
  y la clave de firma. Cambiarlos rompe instalaciones y hooks que ya existen.
- **«Claude» solo de forma descriptiva** («Muesca, el notch para Claude Code»). «Acerca de» y el README llevan «No
  afiliado a Anthropic. Claude es una marca de Anthropic.».
- **Colores**: el acento es el violeta de la marca (`Theme.Accent`, `Brand`); los de estado (aviso, crítico, éxito) no se
  tocan. Texto encima del acento, con `Theme.OnAccent`; como texto, `Theme.AccentText` (AA en claro y oscuro).
- **El logo** se pinta en vector con `UI/BrandMark.cs` (la fuente es `branding/muesca.svg`).

## Pull requests

1. Rama desde `main` con nombre descriptivo (`feat/...`, `fix/...`, `chore/...`).
2. `dotnet build` y `dotnet test` en verde y sin avisos nuevos.
3. Rellena la plantilla del PR. El CI de GitHub Actions debe pasar antes de fusionar.

Las publicaciones (versión, paquetes de Velopack con su manifiesto firmado y el puente para las instalaciones de Inno)
las hace el mantenedor con `scripts/release-velopack.ps1` (`docs/velopack-migracion.md`); no forman parte de un PR
normal. `scripts/release.ps1` (Inno + NetSparkle) queda de referencia y ya no publica.

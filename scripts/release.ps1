#requires -Version 7
# RETIRADO desde la 0.5: las releases salen con scripts/release-velopack.ps1 (Velopack; docs/velopack-migracion.md). Este
# flujo (instalador de Inno + appcast de NetSparkle) se queda de referencia mientras el asistente de perfiles se muda al
# notch. La app ya no lleva NetSparkle: una release publicada desde aquí dejaría sin actualizaciones a quien la instalara,
# así que -Publish se niega.
# Release de ClaudeBar: bump version -> publish -> instalador -> appcast firmado -> (release GitHub).
# Uso:  pwsh scripts/release.ps1 -Version 0.1.1
#       pwsh scripts/release.ps1 -Version 0.1.1 -Publish   (sube el release a GitHub; LÍNEA ROJA)
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string]$Version,
    [switch]$Publish   # sin este flag, NO sube nada a GitHub (dry-run local)
)
$ErrorActionPreference = 'Stop'
if ($Publish) { throw 'release.ps1 está retirado desde la 0.5: publica con scripts/release-velopack.ps1.' }
Write-Warning 'release.ps1 es el flujo de Inno + NetSparkle (hasta la 0.4). Desde la 0.5: scripts/release-velopack.ps1.'
$repo   = Split-Path -Parent $PSScriptRoot
$dotnet = "$env:USERPROFILE\.dotnet\dotnet.exe"
$env:DOTNET_ROOT = "$env:USERPROFILE\.dotnet"
$appcast = "$env:USERPROFILE\.dotnet\tools\netsparkle-generate-appcast.exe"
$csproj = Join-Path $repo "ClaudeBarWin.csproj"
$dist   = Join-Path $repo "dist"
$keys   = Join-Path $repo ".sparkle-keys"
$notesSrc = Join-Path $repo "installer\notes\$Version.md"

# Localizar ISCC (Inno Setup) en las rutas habituales (per-user o per-machine).
$iscc = @(
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
    "C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
    "C:\Program Files\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { throw "ISCC.exe (Inno Setup 6) no encontrado." }

# 0) Con -Publish, el binario tiene que salir EXACTAMENTE del commit que se etiqueta (v0.3.10): árbol
#    limpio y HEAD ya en origin/main. Antes `gh release create` sin --target etiquetaba el main REMOTO,
#    que podía no ser lo compilado. Y las notas de la versión son obligatorias (las muestra NetSparkle).
if ($Publish) {
    if (git -C $repo status --porcelain) { throw "Hay cambios sin commitear: la release no sería reproducible." }
    git -C $repo fetch origin main --quiet
    $head = (git -C $repo rev-parse HEAD).Trim()
    $remote = (git -C $repo rev-parse origin/main).Trim()
    if ($head -ne $remote) { throw "HEAD ($head) no es origin/main ($remote): haz push antes de publicar." }
    if (-not (Test-Path $notesSrc)) { throw "Faltan las notas installer/notes/$Version.md." }
    # El bump de versión tiene que estar YA commiteado y pusheado: si el script lo aplicara aquí, el
    # binario saldría de un csproj que no está en el commit etiquetado (CR-05 de la 2ª revisión de Codex).
    if ((Get-Content $csproj -Raw) -notmatch "<Version>$([regex]::Escape($Version))</Version>") {
        throw "El csproj no declara <Version>$Version</Version> en HEAD. Haz el bump, commit y push, y relanza."
    }
    Write-Host "Version $Version (commit $head)"
} else {
    # 1) Dry-run: bump local de <Version> en el csproj (queda sin commitear, a propósito).
    (Get-Content $csproj -Raw) -replace '<Version>[\d.]+</Version>', "<Version>$Version</Version>" |
        Set-Content $csproj -NoNewline
    Write-Host "Version -> $Version (dry-run: bump local sin commitear)"
}

# 2) Publish self-contained single-file
& $dotnet publish $csproj -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
    -o (Join-Path $repo "publish") --nologo -v minimal
if ($LASTEXITCODE) { throw "publish falló" }

# 2b) Helper nativo (NativeAOT, v0.4): hooks + tee de la statusline. Necesita las Build Tools de C++ de VS.
#     vcvarsall.bat de VS 18 llama a vswhere.exe por PATH y, si no está, su error se cuela en la salida que el
#     ILCompiler toma como ruta de link.exe ("vswhere.exe no se reconoce…") → se añade su carpeta al PATH.
$vsInstaller = Join-Path ${env:ProgramFiles(x86)} "Microsoft Visual Studio\Installer"
if (Test-Path $vsInstaller) { $env:PATH = "$vsInstaller;$env:PATH" }
& $dotnet publish (Join-Path $repo "ClaudeBarWin.Helper\ClaudeBarWin.Helper.csproj") -c Release -r win-x64 `
    -p:PublishAot=true -o (Join-Path $repo "publish") --nologo -v minimal
if ($LASTEXITCODE) { throw "publish del helper falló" }
if (-not (Test-Path (Join-Path $repo "publish\claudebar-helper.exe"))) { throw "falta publish\claudebar-helper.exe" }

# 3) Instalador Inno
New-Item -ItemType Directory -Force $dist | Out-Null
# Limpieza COMPLETA de artefactos: antes quedaban appcast.xml.signature y release-notes.md de la
# versión anterior y podían acabar subidos con la nueva.
Get-ChildItem "$dist\*.exe", "$dist\appcast.xml", "$dist\appcast.xml.signature", "$dist\release-notes.md" -ErrorAction SilentlyContinue |
    Remove-Item -Force
& $iscc (Join-Path $repo "installer\ClaudeBarWin.iss") "/DMyVersion=$Version"
if ($LASTEXITCODE) { throw "ISCC falló" }
$setup = Join-Path $dist "ClaudeBarWin-Setup-$Version.exe"

# 4) Appcast firmado (Ed25519). base-url = assets de la release del tag.
& $appcast `
    --binaries $dist `
    --ext exe `
    --base-url "https://github.com/Yovancas/claudebar-win/releases/download/v$Version/" `
    --key-path $keys `
    --appcast-output-directory $dist `
    --change-log-path (Join-Path $repo "installer\notes") `
    --file-version $Version
if ($LASTEXITCODE) { throw "generate-appcast falló" }
foreach ($f in @($setup, (Join-Path $dist "appcast.xml"), (Join-Path $dist "appcast.xml.signature"))) {
    if (-not (Test-Path $f)) { throw "Falta el artefacto $f tras generar la release." }
}
Write-Host "OK -> $setup  +  $dist\appcast.xml"

# 5) Publicar en GitHub (LÍNEA ROJA: solo con -Publish y tras OK de Yovan)
if ($Publish) {
    $notes = Join-Path $dist "release-notes.md"
    Copy-Item $notesSrc $notes -Force
    gh release create "v$Version" $setup (Join-Path $dist "appcast.xml") (Join-Path $dist "appcast.xml.signature") `
        --repo Yovancas/claudebar-win --title "v$Version" --notes-file $notes --target $head
    if ($LASTEXITCODE) { throw "gh release create falló (exit $LASTEXITCODE)" }
} else {
    Write-Host "DRY-RUN: nada subido. Revisa $dist y relanza con -Publish para release." -ForegroundColor Yellow
}

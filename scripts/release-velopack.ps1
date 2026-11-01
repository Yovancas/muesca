#requires -Version 7
# Release de Muesca (antes ClaudeBar) con Velopack (desde la 0.5): publish self-contained -> vpk pack -> manifiesto firmado -> (subida).
# Uso:  pwsh scripts/release-velopack.ps1 -Version 0.5.0                   (local: compila, empaqueta y firma; no sube nada)
#       pwsh scripts/release-velopack.ps1 -Version 0.5.0 -Bridge           (además, el puente para quien sigue en Inno)
#       pwsh scripts/release-velopack.ps1 -Version 0.5.0 -Bridge -Upload   (sube la release a GitHub; LÍNEA ROJA)
#       ... -NoBridge -Upload   (sin puente, a sabiendas: se publica pero NO se marca como la última, ver abajo)
#       ... -GhRepo Yovancas/muesca-test   (otro repositorio, para probar las actualizaciones con CLAUDEBAR_UPDATE_REPO)
# La firma usa la clave privada Ed25519 de $env:SPARKLE_PRIVATE_KEY (base64; la misma que firmaba NetSparkle). NUNCA en el
# repo. Pasos, riesgos y cómo probarlo: docs/velopack-migracion.md.
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string]$Version,
    [switch]$Bridge,   # instalador puente (Inno) + appcast de NetSparkle para las instalaciones de Inno (0.3.9 / 0.4.0)
    [switch]$Upload,   # sin este flag, NADA sale del equipo (dry-run local)
    # Subir sin puente, a propósito. Las versiones de Inno buscan releases/latest/download/appcast.xml: una release sin
    # puente marcada como la última las dejaría sin actualizaciones y sin enterarse. Con -NoBridge la release sale pero no
    # se marca como la última (las de Velopack la ven igual: miran la lista de releases, no la última).
    [switch]$NoBridge,
    # dueño/repo de las releases (SignedGithubSource.RepoUrl; también va en lo que se firma). El público se renombró de
    # Yovancas/claudebar-win a Yovancas/muesca: publica solo con el renombrado hecho (docs/branding.md). Ojo: PowerShell no
    # distingue mayúsculas en las variables, así que no puede llamarse $Repo (sería la misma que $repo, la carpeta del
    # repositorio).
    [string]$GhRepo = 'Yovancas/muesca',
    # Repositorio del puente: donde publicar el instalador puente y el appcast de NetSparkle. Las 0.3.9 / 0.4.0 leen
    # Yovancas/claudebar-win/releases/latest/download/appcast.xml, así que el puente va ahí aunque Muesca se publique en
    # otro repositorio. Vacío: el mismo que -GhRepo.
    [string]$BridgeRepo = '',
    # Remoto git donde vive el código (HEAD tiene que ser <remoto>/main al subir). Desde la 1.0 el código es privado
    # (LICENSE) y el repositorio público solo distribuye: se publica desde el remoto privado.
    [string]$SourceRemote = 'origin',
    # Commit del repositorio de las releases al que apunta el tag. Vacío: HEAD (cuando el código se publica en el mismo
    # repositorio). Si el público es una copia sin la historia del privado, el commit de esa copia.
    [string]$TargetCommitish = ''
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repo     = Split-Path -Parent $PSScriptRoot
if ($GhRepo -notmatch '^[\w.-]+/[\w.-]+$') { throw "Repositorio no válido: $GhRepo (dueño/repo)." }
$RepoUrl  = "https://github.com/$GhRepo"
if (-not $BridgeRepo) { $BridgeRepo = $GhRepo }
if ($BridgeRepo -notmatch '^[\w.-]+/[\w.-]+$') { throw "Repositorio del puente no válido: $BridgeRepo (dueño/repo)." }
$SeparateBridge = $BridgeRepo -ne $GhRepo
if ($SourceRemote -notmatch '^[\w.-]+$') { throw "Remoto no válido: $SourceRemote." }
# --packId da la carpeta de instalación (%LOCALAPPDATA%\ClaudeBar: aparte de %LOCALAPPDATA%\ClaudeBarWin, la del helper, porque
# Update.exe para todo lo que corre en la suya al actualizar) y --packTitle, el lanzador estable de la raíz (Muesca.exe:
# AppLocation.VelopackStubName), el nombre en «Aplicaciones» y el del menú Inicio. El packId es interno y se queda en
# ClaudeBar; el packTitle pasó a Muesca antes de la primera release con Velopack (docs/branding.md). Desde ella, NUNCA se
# cambian: son la instalación de todo el mundo. ReleaseScriptTests los vigila.
$PackId   = 'ClaudeBar'
$PackTitle = 'Muesca'
$MainExe  = 'ClaudeBarWin.exe'
# Quien publica: el «Editor» de «Aplicaciones».
$PackAuthors = 'Bekay'
$Channel  = 'win'
# Clave pública de la app (ManifestSignature.PublicKeyBase64): no se firma con una privada que no le corresponda.
$PublicKey = 'UxMaJjAdtg6sQZrydaUvC0N088ZIheKsFw+HjQCsLS8='
$ManifestName = "releases.$Channel.json"
$SignatureSuffix = '.sig'
# Lo que se firma: esta línea, el repositorio y los bytes del manifiesto (ManifestSignature.SignedMessage). Con el contexto,
# nada firmado con la misma clave para otra cosa (un appcast de NetSparkle, otro repositorio) vale como manifiesto de este.
$SigningContext = 'claudebar/velopack-manifest/v1'

$csproj     = Join-Path $repo 'ClaudeBarWin.csproj'
$props      = Join-Path $repo 'Directory.Packages.props'
$publishDir = Join-Path $repo 'publish\velopack'
$outDir     = Join-Path $repo 'dist\velopack'
$bridgeDir  = Join-Path $repo 'dist\bridge'
$notesSrc   = Join-Path $repo "installer\notes\$Version.md"
$tag        = "v$Version"

if ($Version -notmatch '^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$') { throw "Versión no válida: $Version (SemVer, p. ej. 0.5.0)." }
# Durante la transición cada release lleva el puente (docs/velopack-migracion.md): subir sin él se pide con -NoBridge.
if ($Bridge -and $NoBridge) { throw '-Bridge y -NoBridge a la vez: elige uno.' }
if ($Upload -and -not $Bridge -and -not $NoBridge) {
    throw 'Con -Upload hace falta -Bridge (las instalaciones de Inno se quedarían sin actualizaciones). Para subir sin puente a propósito: -NoBridge.'
}

# ---------------- Herramientas ----------------

$dotnet = @("$env:USERPROFILE\.dotnet\dotnet.exe") | Where-Object { Test-Path $_ } | Select-Object -First 1
if ($dotnet) { $env:DOTNET_ROOT = "$env:USERPROFILE\.dotnet" } else { $dotnet = (Get-Command dotnet).Source }

# vpk tiene que ir en la MISMA versión que el paquete Velopack de la app (Directory.Packages.props).
$velopackVersion = [regex]::Match((Get-Content $props -Raw), '<PackageVersion Include="Velopack" Version="([^"]+)"').Groups[1].Value
function Find-Tool([string]$Name) {
    $found = Get-Command $Name -ErrorAction SilentlyContinue | Select-Object -First 1 -ExpandProperty Source
    if (-not $found -and (Test-Path "$env:USERPROFILE\.dotnet\tools\$Name.exe")) { $found = "$env:USERPROFILE\.dotnet\tools\$Name.exe" }
    $found
}
$vpk = Find-Tool 'vpk'
if (-not $vpk) { throw "vpk no encontrado. Instálalo con: dotnet tool install -g vpk --version $velopackVersion" }
# vpk no tiene --version: se mira en la lista de herramientas globales.
$vpkListed = (& $dotnet tool list -g | Select-String '^\s*vpk\s+(\S+)' | Select-Object -First 1)
if (-not $vpkListed) {
    Write-Warning "No se pudo comprobar la versión de vpk: tiene que ser la $velopackVersion."
} elseif ($vpkListed.Matches[0].Groups[1].Value -ne $velopackVersion) {
    throw "vpk es la $($vpkListed.Matches[0].Groups[1].Value) y la app usa Velopack $velopackVersion. Iguálalos: dotnet tool update -g vpk --version $velopackVersion"
}

# Chaos.NaCl (Ed25519), la misma librería con la que la app verifica: la deja en la caché de NuGet el restore de la app.
function Import-NaCl {
    $naclVersion = [regex]::Match((Get-Content $props -Raw), '<PackageVersion Include="NetSparkleUpdater\.Chaos\.NaCl" Version="([^"]+)"').Groups[1].Value
    $packages = ((& $dotnet nuget locals global-packages --list) -replace '^global-packages:\s*', '').Trim()
    $dll = Join-Path $packages "netsparkleupdater.chaos.nacl\$naclVersion\lib\netstandard2.0\Chaos.NaCl.dll"
    if (-not (Test-Path $dll)) { throw "No está $dll (¿falta el restore de la app?)." }
    Add-Type -Path $dll
}

# La privada de $env:SPARKLE_PRIVATE_KEY: 32 bytes (la semilla, lo que guarda netsparkle-generate-appcast) o 64 (semilla +
# pública, formato NaCl). Tiene que corresponder a $PublicKey. Nunca se escribe ni se imprime.
function Get-SigningKey {
    $b64 = $env:SPARKLE_PRIVATE_KEY
    if ([string]::IsNullOrWhiteSpace($b64)) { return $null }
    $raw = [Convert]::FromBase64String($b64.Trim())
    if ($raw.Length -eq 64) { $raw = [byte[]]$raw[0..31] }
    elseif ($raw.Length -ne 32) { throw 'SPARKLE_PRIVATE_KEY no es una clave Ed25519 (32 o 64 bytes en base64).' }
    $public = [Chaos.NaCl.Ed25519]::PublicKeyFromSeed($raw)
    if ([Convert]::ToBase64String($public) -ne $PublicKey) {
        throw "SPARKLE_PRIVATE_KEY no corresponde a la clave pública de la app ($PublicKey): no se firma nada."
    }
    [pscustomobject]@{ Expanded = [Chaos.NaCl.Ed25519]::ExpandedPrivateKeyFromSeed($raw); Public = $public }
}

# «claudebar/velopack-manifest/v1\n<dueño/repo en minúsculas>\n» + los bytes del manifiesto TAL CUAL (sin tocar el JSON).
function Get-SignedMessage([byte[]]$Bytes) {
    $header = [Text.Encoding]::UTF8.GetBytes("$SigningContext`n$($GhRepo.ToLowerInvariant())`n")
    $message = [byte[]]::new($header.Length + $Bytes.Length)
    [Array]::Copy($header, 0, $message, 0, $header.Length)
    [Array]::Copy($Bytes, 0, $message, $header.Length, $Bytes.Length)
    , $message   # la coma: que PowerShell no lo desenrolle en bytes sueltos
}

# Firma el manifiesto y deja <manifiesto>.sig: la firma en base64, sin salto de línea.
function Protect-Manifest([string]$Path, $SigningKey) {
    $message = Get-SignedMessage ([IO.File]::ReadAllBytes($Path))
    $signature = [Chaos.NaCl.Ed25519]::Sign($message, $SigningKey.Expanded)
    if (-not [Chaos.NaCl.Ed25519]::Verify($signature, $message, $SigningKey.Public)) { throw "La firma recién hecha de $Path no verifica." }
    $out = "$Path$SignatureSuffix"
    [IO.File]::WriteAllText($out, [Convert]::ToBase64String($signature))
    $out
}

# Lo mismo que ManifestSignature.Verify en la app, con la clave pública de la app.
function Test-ManifestSignature([string]$Path, [string]$SignaturePath) {
    try {
        $signature = [Convert]::FromBase64String(([IO.File]::ReadAllText($SignaturePath)).Trim())
        $message = Get-SignedMessage ([IO.File]::ReadAllBytes($Path))
        $signature.Length -eq 64 -and [Chaos.NaCl.Ed25519]::Verify($signature, $message, [Convert]::FromBase64String($PublicKey))
    } catch { $false }
}

# Lo que se va a firmar tiene que ser exactamente esta versión: los paquetes de $outDir (nombre, tamaño y SHA-256), y nada
# más. Así no se firma otra cosa aunque alguien con permisos cambiara el manifiesto del borrador mientras se sube.
function Assert-ManifestMatchesBuild([string]$Path) {
    $assets = @(([IO.File]::ReadAllText($Path) | ConvertFrom-Json).Assets)
    $local = @(Get-ChildItem $outDir -Filter "$PackId-$Version-*.nupkg")
    if ($assets.Count -eq 0 -or $assets.Count -ne $local.Count) {
        throw "El $ManifestName subido no es el de esta versión ($($assets.Count) paquetes; aquí hay $($local.Count)): no se firma."
    }
    foreach ($a in $assets) {
        $file = $local | Where-Object { $_.Name -eq $a.FileName } | Select-Object -First 1
        if (-not $file -or $a.PackageId -ne $PackId -or "$($a.Version)" -ne $Version -or [long]$a.Size -ne $file.Length -or
            "$($a.SHA256)" -ne (Get-FileHash $file.FullName -Algorithm SHA256).Hash) {
            throw "El $ManifestName subido no cuadra con $($a.FileName) de ${outDir}: no se firma."
        }
    }
}

# vpk download github no verifica nada, y la base del delta tiene que ser el paquete de la release anterior TAL CUAL se
# firmó: con otra, el delta marcaría como «sin cambios» archivos que sí cambian, y los clientes (que parchean su copia, la
# buena) se quedarían con los viejos. $true si cuadra; $false si no se pudo comprobar (sin red); lanza si no cuadra.
function Test-SignedBase([string]$Package) {
    $fileName = Split-Path $Package -Leaf
    $m = [regex]::Match($fileName, "^$([regex]::Escape($PackId))-(.+)-full\.nupkg$")
    if (-not $m.Success) { return $false }
    $baseTag = "v$($m.Groups[1].Value)"
    $dir = Join-Path ([IO.Path]::GetTempPath()) "claudebar-base-$([guid]::NewGuid().ToString('N'))"
    New-Item -ItemType Directory -Force $dir | Out-Null
    try {
        foreach ($f in @($ManifestName, "$ManifestName$SignatureSuffix")) {
            try { Invoke-WebRequest "https://github.com/$GhRepo/releases/download/$baseTag/$f" -OutFile (Join-Path $dir $f) }
            catch { Write-Warning "No se pudo bajar $f de ${baseTag}: $($_.Exception.Message)"; return $false }
        }
        $baseManifest = Join-Path $dir $ManifestName
        if (-not (Test-ManifestSignature $baseManifest "$baseManifest$SignatureSuffix")) {
            throw "El $ManifestName de $baseTag no tiene una firma válida: algo no cuadra en la release anterior. Revísala antes de publicar."
        }
        $entry = @(([IO.File]::ReadAllText($baseManifest) | ConvertFrom-Json).Assets) |
            Where-Object { $_.FileName -eq $fileName } | Select-Object -First 1
        if (-not $entry -or [long]$entry.Size -ne (Get-Item $Package).Length -or
            "$($entry.SHA256)" -ne (Get-FileHash $Package -Algorithm SHA256).Hash) {
            throw "$fileName no es el paquete firmado de ${baseTag}: alguien lo ha cambiado en GitHub. Revisa la release antes de publicar."
        }
        $true
    } finally {
        Remove-Item $dir -Recurse -Force -ErrorAction SilentlyContinue
    }
}

# El repositorio de destino tiene que existir YA y con ESE nombre: va en lo que se firma (Get-SignedMessage) y la app solo
# acepta manifiestos firmados para su SignedGithubSource.RepoUrl (Yovancas/muesca, el público ya renombrado). gh repo view
# sigue los renombrados (el nombre viejo redirige al nuevo), así que el nombre que devuelve también tiene que ser $GhRepo.
# Con -Upload es obligatorio; en local (solo si se va a firmar), sin gh o sin red se avisa y sigue, pero un repositorio que
# no existe para igual.
function Assert-TargetRepo([bool]$Mandatory) {
    if (-not (Get-Command gh -ErrorAction SilentlyContinue)) {
        if ($Mandatory) { throw "Falta gh (GitHub CLI) para comprobar que existe el repositorio de destino $GhRepo." }
        Write-Warning "Sin gh: no se comprueba que exista $GhRepo (se comprueba al subir)."
        return
    }
    $ErrorActionPreference = 'Continue'   # gh escribe el error en stderr: se lee aquí, no corta el script
    $view = gh repo view $GhRepo --json nameWithOwner,isArchived 2>&1
    $failed = $LASTEXITCODE
    $ErrorActionPreference = 'Stop'
    $detail = ($view | Out-String).Trim()
    if ($failed) {
        if ($Mandatory -or $detail -match 'Could not resolve to a Repository') {
            throw "El repositorio de destino $GhRepo no existe o no se puede leer ($detail). ¿Está hecho el renombrado (docs/branding.md)?"
        }
        Write-Warning "No se pudo comprobar el repositorio de destino $GhRepo ($detail)."
        return
    }
    $found = $detail | ConvertFrom-Json
    # GitHub no distingue mayúsculas en los nombres (y lo firmado va en minúsculas): -ne compara sin ellas.
    if ($found.nameWithOwner -ne $GhRepo) {
        throw "$GhRepo redirige a $($found.nameWithOwner) (un nombre viejo): la firma no valdría para la app. Usa -GhRepo $($found.nameWithOwner)."
    }
    if ($found.isArchived) { throw "El repositorio de destino $GhRepo está archivado: no admite releases." }
    Write-Host "Repositorio de destino: $($found.nameWithOwner)"
}

# El borrador que acaba de crear vpk, y uno solo con ese tag: con dos, gh no sabría cuál coger.
function Get-DraftReleaseId {
    $all = gh api "repos/$GhRepo/releases?per_page=100" | ConvertFrom-Json
    if ($LASTEXITCODE) { throw 'No se pudo leer la lista de releases.' }
    $same = @($all | Where-Object { $_.tag_name -eq $tag })
    if ($same.Count -ne 1 -or -not $same[0].draft) {
        throw "Tiene que haber un solo borrador con el tag $tag, y hay $($same.Count) release(s) con él."
    }
    $same[0].id
}

# En el borrador solo puede estar lo nuestro, todo, y los paquetes con el tamaño de aquí.
function Assert-ReleaseAssets($Id) {
    $release = gh api "repos/$GhRepo/releases/$Id" | ConvertFrom-Json
    if ($LASTEXITCODE) { throw "No se pudo leer el borrador $tag." }
    $buildFiles = @(([IO.File]::ReadAllText((Join-Path $outDir "assets.$Channel.json")) | ConvertFrom-Json) |
        ForEach-Object { Split-Path $_.RelativeFileName -Leaf })
    $expected = $buildFiles + @($ManifestName, "$ManifestName$SignatureSuffix", 'RELEASES')
    if ($Bridge -and -not $SeparateBridge) { $expected += @((Split-Path $bridgeExe -Leaf), 'appcast.xml', 'appcast.xml.signature') }
    $remote = @($release.assets)
    foreach ($a in $remote) {
        if ($expected -notcontains $a.name) { throw "El borrador $tag tiene un asset que no es nuestro: $($a.name)." }
        if ($buildFiles -contains $a.name -and [long]$a.size -ne (Get-Item (Join-Path $outDir $a.name)).Length) {
            throw "$($a.name) del borrador $tag no es el de aquí."
        }
    }
    foreach ($n in $expected) {
        if (-not ($remote | Where-Object { $_.name -eq $n })) { throw "Falta $n en el borrador $tag." }
    }
}

# ---------------- 0) Comprobaciones ----------------

# Con -Upload, el binario tiene que salir EXACTAMENTE del commit que se etiqueta: árbol limpio, HEAD en origin/main, el bump
# ya commiteado y las notas de la versión escritas. Y la clave, sí o sí: una release sin firmar no la instala nadie.
$head = (git -C $repo rev-parse HEAD).Trim()
if ($Upload) {
    if (git -C $repo status --porcelain) { throw 'Hay cambios sin commitear: la release no sería reproducible.' }
    git -C $repo fetch $SourceRemote main --quiet
    if ($LASTEXITCODE) { throw "git fetch falló: no se puede comprobar que HEAD sea $SourceRemote/main." }
    $remote = (git -C $repo rev-parse "$SourceRemote/main").Trim()
    if ($head -ne $remote) { throw "HEAD ($head) no es $SourceRemote/main ($remote): haz push antes de publicar." }
    if ((Get-Content $csproj -Raw) -notmatch "<Version>$([regex]::Escape($Version))</Version>") {
        throw "El csproj no declara <Version>$Version</Version> en HEAD. Haz el bump, commit y push, y relanza."
    }
    if (-not (Test-Path $notesSrc)) { throw "Faltan las notas installer/notes/$Version.md." }
    if ([string]::IsNullOrWhiteSpace($env:SPARKLE_PRIVATE_KEY)) { throw 'Falta SPARKLE_PRIVATE_KEY: sin firma no se sube nada.' }
    if (-not (Get-Command gh -ErrorAction SilentlyContinue)) { throw 'Falta gh (GitHub CLI) para firmar y publicar la release.' }
}
# Antes de compilar y de firmar nada: el repositorio que va en la firma existe con ese nombre (Assert-TargetRepo).
if ($Upload -or -not [string]::IsNullOrWhiteSpace($env:SPARKLE_PRIVATE_KEY)) { Assert-TargetRepo $Upload.IsPresent }
Write-Host "Muesca $Version (commit $head)$(if ($Upload) { ' — SE SUBE' } else { ' — local' })"

# ---------------- 1) Publish self-contained, sin single-file ----------------

# Sin single-file: así los parches delta solo llevan los archivos que cambian (el runtime casi nunca).
if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }
& $dotnet publish $csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -p:Version=$Version `
    -o $publishDir --nologo -v minimal
if ($LASTEXITCODE) { throw 'publish falló' }

# Helper nativo (NativeAOT, v0.4): hooks + tee de la statusline. Necesita las Build Tools de C++ de VS (ver release.ps1:
# vcvarsall.bat de VS 18 busca vswhere.exe en el PATH).
$vsInstaller = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer'
if (Test-Path $vsInstaller) { $env:PATH = "$vsInstaller;$env:PATH" }
& $dotnet publish (Join-Path $repo 'ClaudeBarWin.Helper\ClaudeBarWin.Helper.csproj') -c Release -r win-x64 `
    -p:PublishAot=true -p:Version=$Version -o $publishDir --nologo -v minimal
if ($LASTEXITCODE) { throw 'publish del helper falló' }
foreach ($exe in @($MainExe, 'claudebar-helper.exe')) {
    if (-not (Test-Path (Join-Path $publishDir $exe))) { throw "falta $exe en $publishDir" }
}
Get-ChildItem $publishDir -Filter *.pdb -Recurse | Remove-Item -Force

# ---------------- 2) vpk pack ----------------

Import-NaCl
if (Test-Path $outDir) { Remove-Item $outDir -Recurse -Force }
New-Item -ItemType Directory -Force $outDir | Out-Null
# La release anterior (solo lectura) para que vpk saque el parche delta contra ella, comprobada contra su manifiesto firmado
# (Test-SignedBase). Si no hay (la primera) o no se puede comprobar, solo el paquete completo.
& $vpk download github --repoUrl $RepoUrl --channel $Channel --outputDir $outDir
if ($LASTEXITCODE) {
    Write-Warning 'No se pudo bajar la release anterior: esta saldrá sin parche delta (solo el paquete completo).'
} else {
    foreach ($base in @(Get-ChildItem $outDir -Filter "$PackId-*-full.nupkg")) {
        if (-not (Test-SignedBase $base.FullName)) {
            Write-Warning "No se pudo comprobar $($base.Name): esta versión saldrá sin parche delta."
            Get-ChildItem $outDir | Remove-Item -Recurse -Force
            break
        }
    }
}

$packArgs = @(
    'pack', '--packId', $PackId, '--packVersion', $Version, '--packDir', $publishDir, '--mainExe', $MainExe,
    '--packTitle', $PackTitle, '--packAuthors', $PackAuthors, '--runtime', 'win-x64', '--channel', $Channel,
    '--outputDir', $outDir,
    # Solo el menú Inicio: «Iniciar con Windows» lo lleva la app (StartupManager), y un acceso directo de Velopack en Inicio
    # volvería en cada actualización aunque el usuario lo quitara.
    '--shortcuts', 'StartMenuRoot',
    '--noPortable'
)
if (Test-Path $notesSrc) { $packArgs += @('--releaseNotes', $notesSrc) }
# El icono de Setup.exe y de «Aplicaciones»: el de Muesca (el mismo que el del exe, ApplicationIcon del csproj).
$icon = Join-Path $repo 'branding\muesca.ico'
if (Test-Path $icon) { $packArgs += @('--icon', $icon) } else { Write-Host 'Sin branding/muesca.ico: Setup.exe con el icono por defecto.' }
# Pantalla de carga de Setup.exe. Velopack admite GIF animado (todos los fotogramas, con un retardo medio común), escalado
# por DPI y como mucho al 70 % del monitor. En silencio (--silent, el puente) no se enseña.
$splash = Join-Path $repo 'installer\assets\splash.gif'
if (Test-Path $splash) { $packArgs += @('--splashImage', $splash) } else { Write-Host 'Sin installer/assets/splash.gif: Setup.exe sin pantalla de carga.' }
& $vpk @packArgs
if ($LASTEXITCODE) { throw 'vpk pack falló' }

$setupExe = Join-Path $outDir "$PackId-$Channel-Setup.exe"
$fullPkg  = Join-Path $outDir "$PackId-$Version-full.nupkg"
$manifest = Join-Path $outDir $ManifestName
foreach ($f in @($setupExe, $fullPkg, $manifest)) { if (-not (Test-Path $f)) { throw "Falta $f tras vpk pack." } }

# ---------------- 3) Firma del manifiesto local ----------------

# OJO: el releases.win.json que sirve GitHub NO es este: `vpk upload github` lo regenera al subir (solo con los paquetes de
# esta versión). Con -Upload se firma el subido (paso 5); este sirve para comprobar la clave y probar en local.
$key = Get-SigningKey
if ($key) {
    $sig = Protect-Manifest $manifest $key
    if (-not (Test-ManifestSignature $manifest $sig)) { throw "La firma de $manifest no verifica con la clave de la app." }
    Write-Host "Firmado $manifest -> $sig"
} else {
    Write-Warning 'Sin SPARKLE_PRIVATE_KEY: el manifiesto local queda sin firmar (y la app no instalaría nada de aquí).'
}

# ---------------- 4) Puente desde Inno (opcional) ----------------

if ($Bridge) {
    $iscc = @(
        "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
        'C:\Program Files (x86)\Inno Setup 6\ISCC.exe',
        'C:\Program Files\Inno Setup 6\ISCC.exe'
    ) | Where-Object { Test-Path $_ } | Select-Object -First 1
    if (-not $iscc) { throw 'ISCC.exe (Inno Setup 6) no encontrado.' }
    $appcastTool = Find-Tool 'netsparkle-generate-appcast'
    if (-not $appcastTool) { throw 'netsparkle-generate-appcast no encontrado (dotnet tool install -g NetSparkleUpdater.Tools.AppCastGenerator).' }
    if (-not $key) { throw 'El puente necesita SPARKLE_PRIVATE_KEY: NetSparkle (0.3.9/0.4.0) solo instala lo firmado.' }

    if (Test-Path $bridgeDir) { Remove-Item $bridgeDir -Recurse -Force }
    New-Item -ItemType Directory -Force $bridgeDir | Out-Null
    & $iscc (Join-Path $repo 'installer\bridge\ClaudeBarWin-Bridge.iss') "/DMyVersion=$Version" "/DSetupExe=$setupExe" "/O$bridgeDir"
    if ($LASTEXITCODE) { throw 'ISCC (puente) falló' }
    $bridgeExe = Join-Path $bridgeDir "ClaudeBarWin-Bridge-$Version.exe"
    if (-not (Test-Path $bridgeExe)) { throw "Falta $bridgeExe" }

    # El appcast que leen las versiones de Inno (releases/latest/download/appcast.xml), firmado con la misma clave. La
    # pública va por variable: la herramienta la pide junto a la privada.
    $env:SPARKLE_PUBLIC_KEY = $PublicKey
    & $appcastTool --binaries $bridgeDir --ext exe `
        --base-url "https://github.com/$BridgeRepo/releases/download/$tag/" `
        --appcast-output-directory $bridgeDir `
        --change-log-path (Join-Path $repo 'installer\notes') `
        --file-version $Version
    if ($LASTEXITCODE) { throw 'generate-appcast falló' }
    foreach ($f in @('appcast.xml', 'appcast.xml.signature')) {
        if (-not (Test-Path (Join-Path $bridgeDir $f))) { throw "Falta $f del puente." }
    }
    Write-Host "Puente -> $bridgeExe + appcast.xml"
}

# ---------------- 5) Subida (LÍNEA ROJA: solo con -Upload) ----------------

if (-not $Upload) {
    Write-Host "DRY-RUN: nada subido. Revisa $outDir$(if ($Bridge) { " y $bridgeDir" }) y relanza con -Upload." -ForegroundColor Yellow
    return
}

$token = $env:GITHUB_TOKEN
if ([string]::IsNullOrWhiteSpace($token)) { $token = (gh auth token 2>$null) }
if ([string]::IsNullOrWhiteSpace($token)) { throw 'Hace falta un token de GitHub (GITHUB_TOKEN o gh auth login).' }

# a) vpk crea la release como BORRADOR (sin --publish): nadie la ve hasta que esté firmada.
& $vpk upload github --repoUrl $RepoUrl --token $token --channel $Channel --outputDir $outDir `
    --tag $tag --releaseName $tag --targetCommitish $(if ($TargetCommitish) { $TargetCommitish } else { $head })
if ($LASTEXITCODE) { throw 'vpk upload github falló' }

$tmp = Join-Path ([IO.Path]::GetTempPath()) "claudebar-release-$([guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory -Force $tmp | Out-Null
try {
    $releaseId = Get-DraftReleaseId

    # b) Se firma el manifiesto que se ha subido (vpk lo regenera al subir), después de comprobar que es exactamente el de
    #    esta versión, y se sube su firma.
    gh release download $tag --repo $GhRepo --pattern $ManifestName --dir $tmp --clobber
    if ($LASTEXITCODE) { throw "No se pudo bajar el $ManifestName subido." }
    $uploaded = Join-Path $tmp $ManifestName
    Assert-ManifestMatchesBuild $uploaded
    $uploadedSig = Protect-Manifest $uploaded $key
    gh release upload $tag $uploadedSig --repo $GhRepo --clobber
    if ($LASTEXITCODE) { throw 'No se pudo subir la firma del manifiesto.' }

    # c) El puente para las instalaciones de Inno.
    if ($Bridge -and -not $SeparateBridge) {
        gh release upload $tag $bridgeExe (Join-Path $bridgeDir 'appcast.xml') (Join-Path $bridgeDir 'appcast.xml.signature') `
            --repo $GhRepo --clobber
        if ($LASTEXITCODE) { throw 'No se pudo subir el puente.' }
    }

    # d) Comprobación final: el mismo borrador, solo con lo nuestro, y manifiesto y firma, otra vez desde GitHub, como los
    #    verificará la app.
    if ((Get-DraftReleaseId) -ne $releaseId) { throw "El borrador de $tag ha cambiado mientras se subía." }
    Assert-ReleaseAssets $releaseId
    $check = Join-Path $tmp 'check'
    New-Item -ItemType Directory -Force $check | Out-Null
    gh release download $tag --repo $GhRepo --pattern $ManifestName --pattern "$ManifestName$SignatureSuffix" --dir $check --clobber
    if ($LASTEXITCODE -or -not (Test-ManifestSignature (Join-Path $check $ManifestName) (Join-Path $check "$ManifestName$SignatureSuffix"))) {
        throw 'La firma subida no verifica.'
    }
    Assert-ManifestMatchesBuild (Join-Path $check $ManifestName)

    # e) Publicar ESE borrador (por su id) y marcarlo como la última release: las versiones de Inno leen
    #    releases/latest/download/appcast.xml. Sin puente (-NoBridge) no se marca: la última sigue siendo la que lo lleva.
    #    Con el puente en otro repositorio, aquí no lo lee nadie con NetSparkle: esta se marca siempre como la última.
    $makeLatest = if ($Bridge -or $SeparateBridge) { 'true' } else { 'false' }
    gh api -X PATCH "repos/$GhRepo/releases/$releaseId" -F draft=false -f make_latest=$makeLatest | Out-Null
    if ($LASTEXITCODE) { throw 'No se pudo publicar el borrador.' }

    # f) El puente en su propio repositorio (el viejo, el que leen las 0.3.9 / 0.4.0), como la última release de ese
    #    repositorio. Va después de publicar la de Muesca: el puente instala una versión que ya está publicada.
    if ($Bridge -and $SeparateBridge) {
        $bridgeNotes = "Muesca ${Version}: ClaudeBar ahora se llama Muesca y se publica en https://github.com/$GhRepo. " +
            "Esta release solo lleva el instalador puente que actualiza solas las instalaciones de ClaudeBar 0.3.9 / 0.4.0."
        gh release create $tag $bridgeExe (Join-Path $bridgeDir 'appcast.xml') (Join-Path $bridgeDir 'appcast.xml.signature') `
            --repo $BridgeRepo --title "Muesca $Version (puente)" --notes $bridgeNotes --latest
        if ($LASTEXITCODE) { throw "No se pudo publicar el puente en $BridgeRepo (la release de $GhRepo ya está publicada)." }
        $bridgeCheck = Join-Path $tmp 'bridge'
        New-Item -ItemType Directory -Force $bridgeCheck | Out-Null
        gh release download $tag --repo $BridgeRepo --pattern 'appcast.xml' --dir $bridgeCheck --clobber
        if ($LASTEXITCODE -or ((Get-FileHash (Join-Path $bridgeCheck 'appcast.xml')).Hash -ne (Get-FileHash (Join-Path $bridgeDir 'appcast.xml')).Hash)) {
            throw "El appcast publicado en $BridgeRepo no es el de aquí."
        }
        Write-Host "Puente publicado en $BridgeRepo ($tag)."
    }
} catch {
    Write-Host "La release $tag se queda en BORRADOR en GitHub (no la ve nadie). Revísala; para empezar de nuevo: gh release delete $tag --repo $GhRepo --yes" -ForegroundColor Yellow
    throw
} finally {
    Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue
}
Write-Host "Publicada $tag." -ForegroundColor Green

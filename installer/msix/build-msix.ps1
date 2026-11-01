#requires -Version 7
# Empaqueta Muesca para la Microsoft Store (MSIX): publish self-contained (x64 y arm64) -> makepri -> makeappx pack ->
# makeappx bundle -> .msixupload. Decisiones, riesgos y pruebas: docs/msix.md; subida: docs/store-submission.md.
#
# Uso (Windows, con el Windows SDK 10.0.19041 o posterior y las Build Tools de C++ para el helper NativeAOT):
#   pwsh installer/msix/build-msix.ps1 -Version 1.0.0 -IdentityName <Package/Identity/Name> -Publisher '<CN=...>'
#       Lo que se sube a Partner Center: dist\msix\Muesca_1.0.0.0.msixupload (sin firmar: la Store firma al publicar).
#       -IdentityName y -Publisher salen de Partner Center › Product identity (también de $env:MUESCA_IDENTITY_NAME y
#       $env:MUESCA_PUBLISHER).
#   pwsh installer/msix/build-msix.ps1 -Version 1.0.0 -TestSign
#       Además (o, sin identidad de Partner Center, en su lugar), una copia firmada con un certificado de prueba para
#       instalarla en este equipo: dist\msix\prueba\. El script dice cómo confiar en el certificado e instalar.
#   ... -Arch x64        Solo una arquitectura (el helper de arm64 necesita las Build Tools de C++ para ARM64).
#   ... -TestSign -Wack  Pasa el Windows App Certification Kit a la copia firmada (pide administrador).
#
# Nada sale del equipo: el script no sube nada.
[CmdletBinding()]
param(
    # Versión de la app (SemVer sin sufijo, 3 partes). El MSIX la lleva en 4 partes con la última a 0 (reservada a la Store)
    # y la primera no puede ser 0: la Store no admite 0.x.
    [Parameter(Mandatory)] [string]$Version,
    [string]$IdentityName = $env:MUESCA_IDENTITY_NAME,
    [string]$Publisher = $env:MUESCA_PUBLISHER,
    [ValidateSet('x64', 'arm64')] [string[]]$Arch = @('x64', 'arm64'),
    [switch]$TestSign,
    [switch]$Wack
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repo      = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$msixSrc   = $PSScriptRoot
$template  = Join-Path $msixSrc 'Package.appxmanifest'
$assets    = Join-Path $msixSrc 'Assets'
$appProj   = Join-Path $repo 'ClaudeBarWin.csproj'
$helperProj = Join-Path $repo 'ClaudeBarWin.Helper\ClaudeBarWin.Helper.csproj'
$workDir   = Join-Path $repo 'publish\msix'
$outDir    = Join-Path $repo 'dist\msix'
$MainExe   = 'ClaudeBarWin.exe'           # AppLocation.ExeName
$HelperExe = 'claudebar-helper.exe'       # HelperDeployer.ExeName (el alias muesca-helper.exe apunta aquí)
# Identidad de pruebas (solo con -TestSign y sin la de Partner Center): instala en local, no vale para subir.
# (PowerShell no distingue mayúsculas en las variables: estos nombres no pueden repetirse con otra caja.)
$DevIdentityName = 'Bekay.Muesca.Dev'
$DevPublisher    = 'CN=Muesca Dev'

# ---------------- Versión e identidad ----------------

function ConvertTo-MsixVersion([string]$Value) {
    if ($Value -notmatch '^\d+\.\d+\.\d+(\.0)?$') { throw "Versión no válida: $Value (1.0.0; la cuarta parte, si va, es 0)." }
    $parts = @($Value.Split('.') | ForEach-Object { [int]$_ })
    if ($parts[0] -lt 1) { throw "La Store no admite versiones 0.x (la primera parte no puede ser 0): usa 1.0.0 o mayor." }
    if (@($parts | Where-Object { $_ -gt 65535 }).Count) { throw "Versión no válida: $Value (cada parte, de 0 a 65535)." }
    '{0}.{1}.{2}.0' -f $parts[0], $parts[1], $parts[2]
}
# El WACK instala el paquete para probarlo, y uno sin firmar no se instala: hace falta la copia firmada de prueba.
if ($Wack -and -not $TestSign) { throw '-Wack necesita -TestSign: el kit instala el paquete y uno sin firmar no se puede instalar.' }
$msixVersion = ConvertTo-MsixVersion $Version
$appVersion  = ($msixVersion -split '\.')[0..2] -join '.'

$usingDevIdentity = -not $IdentityName -and -not $Publisher
if ($usingDevIdentity) {
    if (-not $TestSign) {
        throw 'Faltan -IdentityName y -Publisher (Partner Center › Product identity). Para probar en local sin ellos: -TestSign.'
    }
    Write-Warning "Sin la identidad de Partner Center: se usa la de pruebas ($DevIdentityName, $DevPublisher). Ese paquete no se puede subir."
    $IdentityName = $DevIdentityName
    $Publisher = $DevPublisher
} elseif (-not $IdentityName -or -not $Publisher) {
    throw 'Hacen falta los dos: -IdentityName y -Publisher (o ninguno, con -TestSign).'
}
# Package/Identity/Name: 3 a 50 caracteres, letras, números, punto y guion. Publisher: un nombre distinguido (CN=...).
if ($IdentityName -notmatch '^[A-Za-z0-9.-]{3,50}$') { throw "IdentityName no válido: $IdentityName" }
if ($Publisher -notmatch '^CN=[^"<>&]+$') { throw "Publisher no válido: $Publisher (tiene que empezar por CN=, como en Partner Center)." }

# ---------------- Herramientas ----------------

$dotnet = @("$env:USERPROFILE\.dotnet\dotnet.exe") | Where-Object { Test-Path $_ } | Select-Object -First 1
if ($dotnet) { $env:DOTNET_ROOT = "$env:USERPROFILE\.dotnet" } else { $dotnet = (Get-Command dotnet).Source }

# makeappx, makepri y signtool vienen con el Windows SDK: el del PATH o el más nuevo de Windows Kits\10\bin.
function Find-SdkTool([string]$Name) {
    $found = Get-Command $Name -ErrorAction SilentlyContinue | Select-Object -First 1 -ExpandProperty Source
    if ($found) { return $found }
    $kits = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin'
    $hostArch = if ($env:PROCESSOR_ARCHITECTURE -eq 'ARM64') { 'arm64' } else { 'x64' }
    if (Test-Path $kits) {
        $found = Get-ChildItem $kits -Directory |
            Where-Object { $_.Name -match '^\d+\.\d+\.\d+\.\d+$' } |
            Sort-Object { [version]$_.Name } -Descending |
            ForEach-Object { Join-Path $_.FullName "$hostArch\$Name" } |
            Where-Object { Test-Path $_ } |
            Select-Object -First 1
        if ($found) { return $found }
    }
    throw "No se encuentra $Name. Instala el Windows SDK (10.0.19041 o posterior) o añade su carpeta bin al PATH."
}

function Invoke-Tool([string]$Exe, [string[]]$Arguments) {
    Write-Host "> $(Split-Path $Exe -Leaf) $($Arguments -join ' ')" -ForegroundColor DarkGray
    & $Exe @Arguments
    if ($LASTEXITCODE) { throw "$(Split-Path $Exe -Leaf) falló (código $LASTEXITCODE)." }
}

$makeappx = Find-SdkTool 'makeappx.exe'
$makepri  = Find-SdkTool 'makepri.exe'

# ---------------- Manifiesto ----------------

function New-Manifest([string]$Architecture) {
    $xml = Get-Content $template -Raw -Encoding utf8
    foreach ($marker in '__IDENTITY_NAME__', '__IDENTITY_PUBLISHER__', '__VERSION__', '__ARCH__') {
        if (-not $xml.Contains($marker)) { throw "Falta el marcador $marker en $template" }
    }
    $xml = $xml.Replace('__IDENTITY_NAME__', [Security.SecurityElement]::Escape($IdentityName)).
        Replace('__IDENTITY_PUBLISHER__', [Security.SecurityElement]::Escape($Publisher)).
        Replace('__VERSION__', $msixVersion).
        Replace('__ARCH__', $Architecture)
    if ($xml -match '__[A-Z_]+__') { throw "Queda un marcador sin rellenar: $($Matches[0])" }
    [xml]$xml | Out-Null   # que siga siendo XML válido
    $xml
}

# ---------------- Compilar y empaquetar ----------------

foreach ($dir in $workDir, $outDir) { if (Test-Path $dir) { Remove-Item $dir -Recurse -Force } }
$packagesDir = Join-Path $workDir 'packages'
New-Item -ItemType Directory -Force -Path $packagesDir, $outDir | Out-Null

# resources.pri: el manifiesto nombra los logos sin calificador (Assets\Square44x44Logo.png) y Windows elige la escala o
# el targetsize por el índice de recursos. Se indexa una carpeta con solo el manifiesto y Assets (no los binarios).
$priConfig = Join-Path $workDir 'priconfig.xml'
Invoke-Tool $makepri @('createconfig', '/cf', $priConfig, '/dq', 'en-US', '/pv', '10.0.0', '/o')

foreach ($a in $Arch) {
    Write-Host "== $a ==" -ForegroundColor Cyan
    $layout = Join-Path $workDir "layout-$a"

    # Self-contained y sin single-file, como la edición de GitHub; el helper, NativeAOT (arranca en ~20 ms por evento).
    Invoke-Tool $dotnet @('publish', $appProj, '-c', 'Release', '-r', "win-$a", '--self-contained', 'true',
        '-p:PublishSingleFile=false', "-p:Version=$appVersion", '-o', $layout, '--nologo', '-v', 'minimal')
    Invoke-Tool $dotnet @('publish', $helperProj, '-c', 'Release', '-r', "win-$a", '-p:PublishAot=true',
        "-p:Version=$appVersion", '-o', $layout, '--nologo', '-v', 'minimal')
    foreach ($exeName in $MainExe, $HelperExe) {
        if (-not (Test-Path (Join-Path $layout $exeName))) { throw "Falta $exeName en $layout" }
    }
    Get-ChildItem $layout -Filter *.pdb -Recurse | Remove-Item -Force

    Copy-Item $assets (Join-Path $layout 'Assets') -Recurse
    $manifest = New-Manifest $a
    Set-Content (Join-Path $layout 'AppxManifest.xml') $manifest -Encoding utf8NoBOM -NoNewline

    $priRoot = Join-Path $workDir "pri-$a"
    New-Item -ItemType Directory -Force -Path $priRoot | Out-Null
    Copy-Item $assets (Join-Path $priRoot 'Assets') -Recurse
    Set-Content (Join-Path $priRoot 'AppxManifest.xml') $manifest -Encoding utf8NoBOM -NoNewline
    Invoke-Tool $makepri @('new', '/pr', $priRoot, '/cf', $priConfig, '/mn', (Join-Path $priRoot 'AppxManifest.xml'),
        '/of', (Join-Path $layout 'resources.pri'), '/o')

    Invoke-Tool $makeappx @('pack', '/d', $layout, '/p', (Join-Path $packagesDir "Muesca_${msixVersion}_$a.msix"), '/o')
}

# El bundle (todas las arquitecturas) y el .msixupload, que es lo que recomienda subir Partner Center: un zip con el bundle.
$bundle = Join-Path $outDir "Muesca_$msixVersion.msixbundle"
Invoke-Tool $makeappx @('bundle', '/d', $packagesDir, '/p', $bundle, '/bv', $msixVersion, '/o')

$upload = $null
if (-not $usingDevIdentity) {
    $upload = Join-Path $outDir "Muesca_$msixVersion.msixupload"
    $zip = "$upload.zip"
    Compress-Archive -Path $bundle -DestinationPath $zip -Force
    Move-Item $zip $upload -Force
}

# ---------------- Firma de prueba (opcional) ----------------

$testBundle = $null
$cer = $null
if ($TestSign) {
    $signtool = Find-SdkTool 'signtool.exe'
    $testDir = Join-Path $outDir 'prueba'
    $testPackages = Join-Path $workDir 'packages-prueba'
    New-Item -ItemType Directory -Force -Path $testDir, $testPackages | Out-Null

    # El sujeto del certificado tiene que ser EXACTAMENTE el Publisher del manifiesto. Se reutiliza si ya existe.
    $cert = Get-ChildItem Cert:\CurrentUser\My |
        Where-Object { $_.Subject -eq $Publisher -and $_.HasPrivateKey -and $_.NotAfter -gt (Get-Date).AddDays(1) } |
        Sort-Object NotAfter -Descending | Select-Object -First 1
    if (-not $cert) {
        Write-Host "Creando un certificado de prueba ($Publisher) en CurrentUser\My"
        $cert = New-SelfSignedCertificate -Type Custom -Subject $Publisher -KeyUsage DigitalSignature `
            -FriendlyName 'Muesca (firma de prueba)' -CertStoreLocation 'Cert:\CurrentUser\My' `
            -TextExtension @('2.5.29.37={text}1.3.6.1.5.5.7.3.3', '2.5.29.19={text}')
    }
    $cer = Join-Path $testDir 'Muesca-prueba.cer'
    Export-Certificate -Cert $cert -FilePath $cer | Out-Null

    # Cada paquete firmado antes de agruparlo, y luego el bundle.
    foreach ($msix in Get-ChildItem $packagesDir -Filter *.msix) {
        $copy = Join-Path $testPackages $msix.Name
        Copy-Item $msix.FullName $copy
        Invoke-Tool $signtool @('sign', '/fd', 'SHA256', '/sha1', $cert.Thumbprint, '/s', 'My', $copy)
        Copy-Item $copy (Join-Path $testDir $msix.Name)
    }
    $testBundle = Join-Path $testDir "Muesca_${msixVersion}_prueba.msixbundle"
    Invoke-Tool $makeappx @('bundle', '/d', $testPackages, '/p', $testBundle, '/bv', $msixVersion, '/o')
    Invoke-Tool $signtool @('sign', '/fd', 'SHA256', '/sha1', $cert.Thumbprint, '/s', 'My', $testBundle)
}

# ---------------- Windows App Certification Kit (opcional) ----------------

if ($Wack) {
    $appcert = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\App Certification Kit\appcert.exe'
    if (-not (Test-Path $appcert)) { throw "No se encuentra el Windows App Certification Kit ($appcert)." }
    $report = Join-Path $outDir 'wack-report.xml'
    Invoke-Tool $appcert @('reset')
    Invoke-Tool $appcert @('test', '-appxpackagepath', $testBundle, '-reportoutputpath', $report)
    Write-Host "Informe del WACK: $report"
}

# ---------------- Resumen ----------------

Write-Host ''
Write-Host "Muesca $msixVersion ($($Arch -join ', ')) para $IdentityName / $Publisher" -ForegroundColor Green
Write-Host "  Bundle:            $bundle"
if ($upload) { Write-Host "  Para Partner Center: $upload" }
else { Write-Host '  Sin .msixupload: la identidad es la de pruebas (pasa -IdentityName y -Publisher de Partner Center).' }
if ($testBundle) {
    Write-Host "  Firmado de prueba: $testBundle"
    Write-Host ''
    Write-Host 'Para instalarlo en este equipo (una vez, en una consola de administrador):' -ForegroundColor Yellow
    Write-Host "  Import-Certificate -FilePath '$cer' -CertStoreLocation Cert:\LocalMachine\TrustedPeople"
    Write-Host 'Y después, sin administrador:'
    Write-Host "  Add-AppxPackage -Path '$testBundle'"
    Write-Host 'Para quitarlo: Get-AppxPackage *Muesca* | Remove-AppxPackage   (y el certificado, desde certlm.msc › Personas de confianza).'
}

<#
  Build & run helper for ClaudeBarWin.
  Uses the user-local .NET SDK at %USERPROFILE%\.dotnet (no admin install required).

  Usage:
    .\run.ps1                  # build + run (Release)
    .\run.ps1 build            # build only (Release)
    .\run.ps1 test             # build the solution + run all tests (Release)
    .\run.ps1 -Test            # same as "test"
    .\run.ps1 publish          # self-contained single-file exe -> .\publish\ClaudeBarWin.exe
    .\run.ps1 -Configuration Debug          # build + run in Debug
    .\run.ps1 test -Configuration Debug     # tests in Debug
#>
param(
    [ValidateSet("run", "build", "test", "publish")]
    [string]$task = "run",
    [switch]$Test,
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
if ($Test) { $task = "test" }

$dotnet = Join-Path $env:USERPROFILE ".dotnet\dotnet.exe"
if (-not (Test-Path $dotnet)) {
    if (Get-Command dotnet -ErrorAction SilentlyContinue) { $dotnet = "dotnet" }
    else { throw "No se encontró dotnet. Instala el SDK .NET 9." }
}

$proj = Join-Path $PSScriptRoot "ClaudeBarWin.csproj"
$sln  = Join-Path $PSScriptRoot "ClaudeBarWin.sln"

switch ($task) {
    "build"   { & $dotnet build $proj -c $Configuration }
    "test"    {
        # Se compila la solución entera: HelperProcessTests necesita claudebar-helper.exe en su salida.
        & $dotnet build $sln -c $Configuration
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
        & $dotnet test $sln -c $Configuration --no-build
    }
    "publish" {
        & $dotnet publish $proj -c Release -r win-x64 --self-contained true `
            -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
            -o (Join-Path $PSScriptRoot "publish")
        Write-Host "`nExe: $(Join-Path $PSScriptRoot 'publish\ClaudeBarWin.exe')"
    }
    default   { & $dotnet run --project $proj -c $Configuration }
}
exit $LASTEXITCODE

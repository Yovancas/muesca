# tap-e2e.ps1 — prueba de punta a punta del enganche de la statusline (v0.4), SIN tocar la config real.
#
# Ejecuta el comando que escribiría Muesca (el «tee» resistente delante de <original>) igual que lo hace
# Claude Code en Windows — por Git Bash (bash -c) y por PowerShell (-Command) — con un JSON de statusline
# con tildes y emoji por stdin, y comprueba que:
#   (a) la salida es IDÉNTICA byte a byte a la del statusline original solo,
#   (b) el helper dejó el snapshot de la sesión,
#   (c) el sobrecoste del enganche (mediana) no pasa de -MaxOverheadMs (50 ms por defecto),
#   (d) sin el helper (Muesca desinstalada sin apagar el enganche), la salida sigue siendo IDÉNTICA a la del original.
# Para rutas del helper sin espacios y con espacios + apóstrofo ("John O'Brien").
#
# Uso: pwsh scripts/tap-e2e.ps1 -App bin\Debug\net9.0-windows10.0.19041.0\ClaudeBarWin.exe -Helper publish\claudebar-helper.exe
param(
    [Parameter(Mandatory)] [string]$App,
    [Parameter(Mandatory)] [string]$Helper,
    [int]$Runs = 20,
    # Presupuesto de la spec §3: el enganche no puede añadir más de 50 ms (mediana) a la statusline.
    [int]$MaxOverheadMs = 50
)
$ErrorActionPreference = 'Stop'
$App = (Resolve-Path $App).Path
$Helper = (Resolve-Path $Helper).Path

$root = Join-Path ([IO.Path]::GetTempPath()) ("cb-e2e-" + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force $root | Out-Null
$env:CLAUDEBAR_DATA_DIR = Join-Path $root 'data'

# Statusline «original» falsa: lee el JSON de stdin e imprime campos con tildes/emoji (así se nota cualquier
# pérdida de codificación en el camino).
$fake = Join-Path $root 'fake-sl.mjs'
@'
let d = '';
process.stdin.setEncoding('utf8');
process.stdin.on('data', c => d += c).on('end', () => {
  const j = JSON.parse(d);
  process.stdout.write(`${j.cwd} | ${j.model.display_name} | 5h ${j.rate_limits.five_hour.used_percentage}%`);
});
'@ | Set-Content -Path $fake -Encoding utf8
$original = "node " + $fake.Replace('\', '/')

$bash = @(
    $env:CLAUDE_CODE_GIT_BASH_PATH,
    (Join-Path $env:ProgramFiles 'Git\bin\bash.exe'),
    (Join-Path $env:LOCALAPPDATA 'Programs\Git\bin\bash.exe')
) | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1
$shells = [ordered]@{}
if ($bash) { $shells['GitBash'] = @{ Exe = $bash; Args = { param($c) @('-c', $c) } } }
$shells['PowerShell'] = @{ Exe = (Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'); Args = { param($c) @('-NoProfile', '-NonInteractive', '-Command', $c) } }

function Invoke-Shell([string]$exe, [string[]]$argv, [byte[]]$stdin) {
    $psi = [Diagnostics.ProcessStartInfo]::new($exe)
    foreach ($a in $argv) { $psi.ArgumentList.Add($a) }
    $psi.UseShellExecute = $false
    $psi.RedirectStandardInput = $true
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $psi.CreateNoWindow = $true
    $p = [Diagnostics.Process]::Start($psi)
    $ms = [IO.MemoryStream]::new()
    $copy = $p.StandardOutput.BaseStream.CopyToAsync($ms)
    $err = $p.StandardError.ReadToEndAsync()
    $p.StandardInput.BaseStream.Write($stdin, 0, $stdin.Length)
    $p.StandardInput.Close()
    if (-not $p.WaitForExit(20000)) { $p.Kill($true); throw "timeout: $exe" }
    $copy.Wait(); $p.WaitForExit()
    [pscustomobject]@{ Out = $ms.ToArray(); Err = $err.Result; Code = $p.ExitCode }
}

$variants = [ordered]@{
    'ruta sin espacios' = Join-Path $root 'bin'
    "ruta con espacios y apóstrofo" = Join-Path $root "John O'Brien\bin"
}

$results = @()
$fail = $false
foreach ($vName in $variants.Keys) {
    $dir = $variants[$vName]
    New-Item -ItemType Directory -Force $dir | Out-Null
    $h = Join-Path $dir 'claudebar-helper.exe'
    Copy-Item $Helper $h -Force

    foreach ($sName in $shells.Keys) {
        $sh = $shells[$sName]
        $cmd = (& $App --tap-wrap $h $sName $original | Out-String).Trim()
        $sid = "e2e-$([guid]::NewGuid().ToString('N').Substring(0, 8))"
        $json = @{ session_id = $sid; cwd = 'C:\Proyectos\español 🚀'; model = @{ display_name = 'Opus <5> & «ñ»' };
                   rate_limits = @{ five_hour = @{ used_percentage = 23.5; resets_at = 1759020000 };
                                    seven_day = @{ used_percentage = 41.2; resets_at = 1759500000 } } } | ConvertTo-Json -Depth 5 -Compress
        $bytes = [Text.UTF8Encoding]::new($false).GetBytes($json)

        $a = Invoke-Shell $sh.Exe (& $sh.Args $original) $bytes
        $b = Invoke-Shell $sh.Exe (& $sh.Args $cmd) $bytes
        $same = [Convert]::ToBase64String($a.Out) -eq [Convert]::ToBase64String($b.Out) -and $a.Out.Length -gt 0
        $snap = Test-Path (Join-Path $env:CLAUDEBAR_DATA_DIR "ratelimits\$sid.json")

        # Mediana de ejecuciones alternas (original / enganchado) para que el ruido del equipo afecte igual a ambas.
        $ta = [Collections.Generic.List[double]]::new(); $tb = [Collections.Generic.List[double]]::new()
        foreach ($i in 1..$Runs) {
            $sw = [Diagnostics.Stopwatch]::StartNew(); Invoke-Shell $sh.Exe (& $sh.Args $original) $bytes | Out-Null; $ta.Add($sw.Elapsed.TotalMilliseconds)
            $sw = [Diagnostics.Stopwatch]::StartNew(); Invoke-Shell $sh.Exe (& $sh.Args $cmd) $bytes | Out-Null; $tb.Add($sw.Elapsed.TotalMilliseconds)
        }
        $medA = ($ta | Sort-Object)[[int][Math]::Floor($Runs / 2)]
        $medB = ($tb | Sort-Object)[[int][Math]::Floor($Runs / 2)]
        $delta = $medB - $medA
        $fastOk = $delta -le $MaxOverheadMs
        # Sin statusline previa: la línea compacta propia del helper (lleva «·», no-ASCII) debe llegar intacta.
        $cmdS = (& $App --tap-wrap $h $sName '' | Out-String).Trim()
        $c = Invoke-Shell $sh.Exe (& $sh.Args $cmdS) $bytes
        $compact = [Text.Encoding]::UTF8.GetString($c.Out).Trim()
        $compactOk = $compact -eq '5h 24% · 7d 41%'

        # (d) Sin el helper: el enganche resistente le pasa el JSON directo a la statusline del usuario.
        $away = "$h.away"
        Move-Item $h $away -Force
        try { $d = Invoke-Shell $sh.Exe (& $sh.Args $cmd) $bytes } finally { Move-Item $away $h -Force }
        $missingOk = $d.Code -eq 0 -and [Convert]::ToBase64String($a.Out) -eq [Convert]::ToBase64String($d.Out)

        $ok = $same -and $snap -and $b.Code -eq 0 -and $compactOk -and $fastOk -and $missingOk
        if (-not $ok) { $fail = $true }
        $results += [pscustomobject]@{
            Shell = $sName; Ruta = $vName; Resultado = $(if ($ok) { 'OK' } else { 'FAIL' })
            'Salida igual' = $same; Snapshot = $snap; 'Línea propia' = $compactOk; 'Sin helper' = $missingOk
            'Original p50 ms' = [int]$medA; 'Enganchado p50 ms' = [int]$medB; 'Sobrecoste ms' = [int]$delta
        }
        if (-not $ok) {
            Write-Host "--- $sName / $vName" -ForegroundColor Red
            Write-Host "comando:   $cmd"
            Write-Host "original:  $([Text.Encoding]::UTF8.GetString($a.Out))"
            Write-Host "enganche:  $([Text.Encoding]::UTF8.GetString($b.Out))"
            Write-Host "propia:    [$cmdS] -> [$compact]"
            Write-Host "sin helper: $([Text.Encoding]::UTF8.GetString($d.Out)) (código $($d.Code)) $($d.Err)"
            if ($b.Err) { Write-Host "stderr:    $($b.Err)" }
        }
    }
}

$results | Format-Table -AutoSize | Out-String -Width 200 | Write-Host
Remove-Item Env:CLAUDEBAR_DATA_DIR
try { Remove-Item $root -Recurse -Force } catch { }
if ($fail) { Write-Host "E2E: FALLOS" -ForegroundColor Red; exit 1 }
Write-Host "E2E: todo OK" -ForegroundColor Green

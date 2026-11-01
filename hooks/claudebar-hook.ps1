# ClaudeBar live-sessions hook (v2, ClaudeBar 0.3.10). Lee el evento de Claude Code de stdin (JSON)
# y lo reenvia como una linea JSON al Named Pipe \\.\pipe\claudebar. Fire-and-forget:
# si ClaudeBar no esta corriendo (pipe inexistente) o tarda, sale en silencio (exit 0)
# y NUNCA bloquea ni rompe la sesion de Claude. No escribe a stdout/stderr ni devuelve decision.
# ClaudeBar lo reescribe solo al arrancar si cambia (no editar a mano).
try {
  # Claude Code manda UTF-8; la consola de Windows PowerShell usa la pagina OEM por defecto y
  # rompia rutas/proyectos con tildes (v0.3.10).
  [Console]::InputEncoding = [System.Text.Encoding]::UTF8
  $raw = [Console]::In.ReadToEnd()
  if ([string]::IsNullOrWhiteSpace($raw)) { exit 0 }
  $in = $raw | ConvertFrom-Json

  # Derivar status a partir del nombre del evento del hook.
  $ev = "$($in.hook_event_name)"
  $ntype = "$($in.notification_type)"
  switch ($ev) {
    'PreToolUse'        { $status = 'running_tool' }   # instalaciones antiguas (<= 0.3.9)
    'PostToolUse'       { $status = 'processing' }
    'PermissionRequest' { $status = 'waiting_for_approval' }
    'Notification'      {
      # El mismo dialogo de permiso dispara PermissionRequest Y Notification(permission_prompt): antes
      # esta ultima lo pisaba con "esperando input". Las notificaciones informativas no cambian la fase.
      switch ($ntype) {
        'permission_prompt'  { $status = 'waiting_for_approval' }
        'idle_prompt'        { $status = 'waiting_for_input' }
        'agent_needs_input'  { $status = 'waiting_for_input' }
        'elicitation_dialog' { $status = 'waiting_for_input' }
        ''                   { $status = 'waiting_for_input' }
        default              { $status = 'noop' }
      }
    }
    'Stop'              { $status = 'waiting_for_input' }
    'SubagentStop'      { $status = 'processing' }        # instalaciones antiguas (<= 0.3.9)
    'UserPromptSubmit'  { $status = 'processing' }
    'PreCompact'        { $status = 'compacting' }
    'SessionStart'      { $status = 'starting' }
    'SessionEnd'        { $status = 'ended' }
    default             { $status = 'noop' }
  }

  $payload = [ordered]@{
    session_id        = "$($in.session_id)"
    cwd               = "$($in.cwd)"
    pid               = $PID
    event             = $ev
    status            = $status
    tool              = "$($in.tool_name)"
    tool_use_id       = "$($in.tool_use_id)"
    notification_type = $ntype
    ts                = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()
  }
  $json = ($payload | ConvertTo-Json -Compress)

  $pipe = New-Object System.IO.Pipes.NamedPipeClientStream('.', 'claudebar', [System.IO.Pipes.PipeDirection]::Out)
  $pipe.Connect(100)   # timeout corto; si no hay servidor, lanza y caemos al catch
  $sw = New-Object System.IO.StreamWriter($pipe, (New-Object System.Text.UTF8Encoding($false)))
  $sw.AutoFlush = $true
  $sw.WriteLine($json)
  $sw.Dispose()
  $pipe.Dispose()
} catch {
  # ClaudeBar cerrado / pipe ocupado / timeout: ignorar por completo.
}
exit 0

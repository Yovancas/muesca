# Política de seguridad

## Versiones con soporte

Solo la última versión publicada recibe arreglos de seguridad. La auto-actualización la entrega a
todas las instalaciones: desde la 0.5 con Velopack, con el manifiesto de cada release firmado con
Ed25519 (hasta la 0.4, NetSparkle con el appcast firmado con la misma clave).

| Versión            | Soporte |
| ------------------ | ------- |
| Última publicada   | Sí      |
| Anteriores         | No      |

## Cómo reportar una vulnerabilidad

**No abras un issue público.** Usa el reporte privado de GitHub:

1. Ve a la pestaña **Security** del repositorio público
   ([Yovancas/muesca](https://github.com/Yovancas/muesca/security)).
2. Pulsa **Report a vulnerability** y describe:
   - versión de Muesca (Ajustes → Acerca de, o `claudebar-helper.exe --version`) y de Windows;
   - pasos para reproducirlo y el impacto que ves;
   - si afecta a credenciales, a `settings.json` de Claude Code o a la cadena de actualización.

No incluyas tokens, credenciales ni transcripts reales en el reporte: con rutas y fragmentos
anonimizados basta. Intentaremos responder en un plazo de 7 días y acordar contigo la publicación
una vez haya arreglo.

## Qué datos lee y escribe la app

Muesca es una app local. Lo único que manda sin que lo pidas es un contador anónimo de uso (abajo,
y en [PRIVACY.es.md](PRIVACY.es.md)), que se apaga con un interruptor. Esto es todo lo que toca:

### Contador anónimo de uso (activado por defecto, apagable)
- **Qué manda.** Como mucho una vez al día local, un `POST` a
  `https://claudebar-stats.claudebar-stats-worker.workers.dev/v1/ping` con este cuerpo y nada más:
  `{"v":"0.5.0","e":"free","l":"es","d":1,"m":1,"n":0}` (versión, edición `free|pro|github`, idioma de
  la interfaz y tres casillas: primer aviso del día, del mes y de una instalación nueva). Sin ningún
  identificador, ni nombre de equipo, ni versión de Windows, ni datos de Claude.
- **Cómo.** `HttpClient` propio: solo HTTPS a ese host, timeout de 10 s, sin cookies, sin
  redirecciones, User-Agent `ClaudeBar` a secas. Primera comprobación 2-5 min tras arrancar y luego
  cada hora, fuera del hilo de UI. Sin red, 5xx o 429 no guarda nada y lo intenta en la siguiente
  comprobación (sin colas); un 400 no se repite ese día.
- **Cuándo no.** Nunca antes de la bienvenida; quien actualiza ve un aviso una sola vez y no sale nada
  hasta 10 min después. Apagado en *Ajustes → Sistema → Contador anónimo de uso*, en la bienvenida o
  con `CLAUDEBAR_NO_STATS=1`; en compilaciones Debug solo con `CLAUDEBAR_STATS_DEBUG=1`.
- **El servidor** (`server/stats-worker/`, Cloudflare Workers + D1) suma casillas por día: no guarda
  IPs ni registros (observabilidad desactivada). Cloudflare procesa la IP en tránsito para entregar la
  petición y para un límite de 20 avisos por minuto que no se almacena.
- En `config.json` solo queda `UsageStatsEnabled` y `UsageStats` (último día y mes avisados, si ya
  hubo un primer aviso y si el aviso ya se vio): ningún identificador.

### Privacidad y datos: el token OAuth de Claude Code
Por defecto, Muesca lee tu cuota y tu plan con **tu** sesión de Claude Code. Es el ajuste **«Leer la
cuota con tu sesión de Claude Code (no oficial)»** de *Ajustes → Origen de la cuota*: viene activado y
se puede apagar cuando quieras.

- **Qué lee.** De `~/.claude/.credentials.json` (o `%CLAUDE_CONFIG_DIR%\.credentials.json`) solo saca
  `claudeAiOauth.accessToken` y `expiresAt`, para pedir la cuota. Del plan, solo los campos no secretos
  (`subscriptionType` y `rateLimitTier`), como respaldo. El `refreshToken` y el resto del archivo se
  saltan sin convertirlos nunca en texto, y los bytes leídos se ponen a cero al terminar.
- **Cuándo.** Justo antes de cada petición de cuota o de plan, nunca en cada tick de la interfaz. Con
  un token caducado o rechazado (401/403), el archivo no se vuelve a abrir hasta que Claude Code lo
  reescriba, o pasados 30 min. Durante una espera por límite de peticiones (429) tampoco. Entre dos
  peticiones de cuota pasan al menos 55 s, sea cual sea la frecuencia de refresco.
- **A dónde va.** Solo a `https://api.anthropic.com/api/oauth/usage` y `/api/oauth/profile`:
  - solo HTTPS al puerto 443, y el host se comprueba antes de enviar y otra vez sobre la respuesta;
  - no sigue redirecciones: un 3xx es un error y la cabecera nunca va al `Location`;
  - timeout de 10 s y respuesta de 64 KB como máximo;
  - usa el proxy del sistema;
  - el User-Agent es `ClaudeBarWin/<versión>`: no se hace pasar por Claude Code.
- **Qué guarda.** Nada.
  - El token solo vive en memoria mientras dura la petición, y se quita de la petición en cuanto
    llega la respuesta.
  - No se escribe en `config.json`, ni en cachés, ni en logs, ni en volcados.
  - Si alguien intenta imprimirlo, sale `Creds(***)`.
  - El log de diagnóstico tapa además todo lo que parezca un token (`Bearer …`, `sk-ant-…`,
    `"accessToken"`/`"refreshToken"`).
  - Para saber si el archivo cambió se miran su fecha y su tamaño, nada derivado del token.
- **El archivo en claro.** `.credentials.json` lo escribe Claude Code y lo guarda sin cifrar.
  Muesca **no lo cifra, ni puede ni debe hacerlo**: Claude Code dejaría de poder leerlo y tendrías
  que volver a iniciar sesión. Tampoco lo escribe ni hace copias de él. Su protección depende de tu
  cuenta de Windows (los permisos de tu carpeta de usuario).
- **No oficial.** Anthropic no documenta estos endpoints para terceros, así que pueden cambiar o
  dejar de funcionar sin aviso.
- **Con el ajuste apagado:**
  - Muesca no abre `.credentials.json` ni llama a esos endpoints. El plan es el último que dio la
    API de perfil (guardado en `plan.json`) o «Plan desconocido».
  - La cuota sale solo de la statusline (abajo). Sin ninguna sesión abierta se enseña «Última
    lectura hace X min».
  - Se pierden el desglose de Opus y Sonnet, el indicador de uso extra, el uso hecho fuera de Claude
    Code y la etiqueta exacta del plan.
- **Renovar con la CLI.** «Forzar renovación con la CLI (gasta un mensaje)» viene **apagado**. Con el
  token caducado lanzaría `claude -p .`, que envía un prompt de verdad y gasta cuota. Apagado,
  Muesca dice «Sesión caducada — abre Claude Code» y sigue con la statusline.
- Desde la 0.3.10, Muesca **no renueva el token por su cuenta ni escribe** `.credentials.json`.

### Hooks de Claude Code (sesiones en vivo, opt-in)
- Al activarlo, añade sus entradas a `settings.json` de Claude Code (con copia de seguridad previa) y
  las quita al desactivarlo o desinstalar, sin tocar hooks ajenos.
- Si el helper ya no existe (Muesca desinstalada sin apagarlo), Claude Code no se bloquea: un hook que falla es un
  error no bloqueante (la entrada nunca lleva `onFailure: "block"`) y, en los permisos, sale su diálogo de siempre.
- Cada evento lo procesa `claudebar-helper.exe hook`, que reenvía por una *named pipe* local
  (`\\.\pipe\claudebar`) solo: id de sesión, directorio de trabajo, PID, nombre del evento, estado,
  nombre de herramienta, id de la llamada, tipo de notificación y marca de tiempo. No reenvía el
  contenido de prompts, respuestas ni argumentos de herramientas.

### Statusline de Claude Code (cuota desde Claude Code, opt-in)
- Al activarlo, `claudebar-helper.exe statusline --tee` se coloca delante de tu statusline: guarda las
  ventanas de cuota (`rate_limits`) que Claude Code le pasa en
  `%LOCALAPPDATA%\ClaudeBarWin\ratelimits\<sesión>.json` y reenvía la entrada intacta a tu
  statusline original. Al desactivarlo se restaura el comando exacto que tenías.
- El comando comprueba antes que el helper existe (`[ -e … ]` en Git Bash, `Test-Path` en PowerShell): si no está
  (Muesca desinstalada sin apagarlo), tu statusline recibe el JSON directamente y sigue funcionando igual.

### Otros archivos locales
- **Transcripts** `~/.claude/projects/**/*.jsonl`: solo lectura, para estimar el gasto por modelo
  (tokens y modelo; no se envía nada).
- `%APPDATA%\ClaudeBarWin\`: `config.json`, caché de uso y de plan, historial, tarifas y
  `claudebar.log` (log de diagnóstico sin tokens).

### Conexiones de red
- `api.anthropic.com` — cuota y plan, con tu token (solo con «Leer la cuota con tu sesión de Claude
  Code» activado, que es lo predeterminado).
- `status.claude.com` — estado público del servicio (sin datos tuyos).
- `models.dev` — tarifas públicas, GET anónimo; se puede desactivar en Ajustes → Sistema.
- `claudebar-stats.claudebar-stats-worker.workers.dev` — el contador anónimo de uso (arriba); se puede
  desactivar en Ajustes → Sistema.
- `api.github.com` y `github.com/Yovancas/muesca/releases` — la lista de releases y las
  descargas de actualización. Cada release trae `releases.win.json` (tamaño y SHA-256 de cada
  paquete) firmado con Ed25519 en `releases.win.json.sig`: la app verifica la firma antes de leerlo
  y Velopack comprueba cada paquete contra él; una release que no verifica se descarta. Detalles en
  `docs/velopack-migracion.md`.

## Alcance

Entran en alcance: fuga del token o de datos locales (también que viaje a un host distinto de
`api.anthropic.com` o tras una redirección), escritura indebida en `settings.json` o
`.credentials.json`, inyección a través de la *named pipe* o de la entrada de hooks/statusline, y
cualquier forma de saltarse la verificación de firma de las actualizaciones.

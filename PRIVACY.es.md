# Política de privacidad

Muesca (antes ClaudeBar for Windows) es una app local: tus credenciales de Claude Code, tus transcripts, tus
prompts y tus cifras de uso nunca salen de tu equipo. Lo único que manda por su cuenta es un
**contador anónimo de uso**, para saber más o menos cuánta gente lo usa. Esta página explica
exactamente qué es.

## Qué se manda

Como mucho **una vez al día** (según tu fecha local), Muesca manda una petición HTTPS a
`https://claudebar-stats.claudebar-stats-worker.workers.dev/v1/ping` con este cuerpo y nada más:

```json
{"v":"0.5.0","e":"free","l":"es","d":1,"m":1,"n":0}
```

| Campo | Qué es |
|-------|--------|
| `v` | Versión de la app |
| `e` | Edición: `free` (Microsoft Store), `pro` (Pro comprado) o `github` (edición de GitHub / winget) |
| `l` | Idioma de la interfaz (`en`, `es`, `de`…) |
| `d` | Siempre `1`: primer aviso de hoy |
| `m` | `1` si es el primer aviso del mes |
| `n` | `1` solo en el primer aviso de una instalación nueva (`0` si ya usabas Muesca y has actualizado) |

La petición no lleva cookies y su User-Agent es solo `ClaudeBar` (el nombre viejo, a propósito: ver [docs/branding.md](docs/branding.md)).

## Qué no se manda

Ningún identificador (ni de instalación, ni cuenta, ni correo, ni ningún código aleatorio), ni el
nombre del equipo o del usuario, ni la versión de Windows, ni datos del hardware, ni nada de tu uso
de Claude Code, tu cuota, tus proyectos o tus archivos. Cada aviso es igual que cualquier otro con
los mismos seis valores.

## Qué guarda el servidor

El servidor solo suma las casillas **por día, versión, edición e idioma** (por ejemplo, «el
2026-10-30 hubo 120 personas activas con 0.5.0/free/es»). Ninguna fila representa a una persona.
**No guarda direcciones IP ni registros de las peticiones.**

Cloudflare, que ejecuta el servidor, procesa necesariamente tu IP en tránsito para entregar la
petición, y la usa para un límite de 20 avisos por minuto que vive en memoria y no se almacena.

El código del servidor es público en este repositorio: [`server/stats-worker/`](server/stats-worker/).

## Cuándo no se manda nada

- Antes de que hayas visto la bienvenida de una instalación nueva.
- Si ya usabas Muesca, la primera vez que arranca la versión nueva enseña un aviso una sola vez
  y no manda nada durante al menos 10 minutos.
- Con el contador apagado.
- Si la petición falla (sin red, error del servidor o límite), no se encola nada: lo vuelve a
  intentar en la siguiente comprobación, que es cada hora.

## Cómo apagarlo

Cualquiera de estas lo para al momento:

- **Ajustes › Sistema › Contador anónimo de uso**, en el panel de Muesca.
- El interruptor de la bienvenida.
- La variable de entorno `CLAUDEBAR_NO_STATS=1`.

Las demás conexiones de Muesca (cuota y plan de Anthropic, estado del servicio, tarifas
públicas, actualizaciones) se describen en [SECURITY.md](SECURITY.md).

## Letras (opcional, Pro)

Con las letras sincronizadas activadas, Muesca manda a [lrclib.net](https://lrclib.net) el
**título y el artista** de la canción que suena para bajar su letra. No manda nada más: ni cuenta,
ni identificador, ni nada de Claude Code. Como cualquier servidor, lrclib.net ve tu IP al recibir
la petición. Las letras que encuentra se guardan solo en una caché de tu equipo.

Las letras son una función del Pro y solo se piden con la música y las letras encendidas. Se
apagan en **Ajustes › Notch › Letras de LRCLIB**; apagadas, Muesca no hace ninguna petición a
lrclib.net.

## Edición de Microsoft Store

La edición de la Store comprueba la licencia del Pro y hace las compras con Microsoft Store
(`Windows.Services.Store`): el pago lo gestiona Microsoft, con su
[declaración de privacidad](https://privacy.microsoft.com/es-es/privacystatement). Muesca nunca ve
ni pide tus datos de pago.

No busca actualizaciones en GitHub: la actualiza Microsoft Store.

## Contacto

Dudas o peticiones: abre un issue en
[github.com/Yovancas/muesca/issues](https://github.com/Yovancas/muesca/issues).

English version: [PRIVACY.md](PRIVACY.md).

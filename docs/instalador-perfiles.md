# Instalador: perfiles y ajustes recomendados

> **Desde la 0.5 el instalador es Velopack** (sin asistente; ver `docs/velopack-migracion.md`) y el asistente de perfiles
> se muda a la app, dentro del notch. Este instalador de Inno y su `--apply-setup` se quedan como referencia hasta que ese
> asistente esté fusionado.

El instalador (`installer/ClaudeBarWin.iss`, Inno Setup 6) pregunta para qué vas a usar Muesca, propone los
ajustes que mejor le van al equipo y, al terminar, `ClaudeBarWin.exe --apply-setup <json>` los aplica a
`%APPDATA%\ClaudeBarWin\config.json`. Sigue siendo por usuario y sin permisos de administrador.

## Páginas

1. **Bienvenida.**
2. **Tu configuración actual**: solo en una actualización con `config.json` previo. «Mantener mi configuración»
   viene marcado: si se deja así, no se enseñan los perfiles ni se toca la configuración.
3. **¿Para qué vas a usar Muesca?**: los cinco perfiles, con el recomendado marcado. Se preselecciona el que
   se eligió la última vez.
4. **Qué se activa**: casillas con los valores del perfil. En una actualización, lo que el perfil no fija sale
   con el valor actual.
5. **Ajustes recomendados**: idioma, monitor del notch (solo con 2 o más monitores), tamaño, cómo se abre,
   e iniciar con Windows, cada uno con una línea que lo explica.
6. **Claude Code**: solo si está instalado y el perfil usa las sesiones en vivo. Incluye activar las sesiones
   en vivo y contestar los permisos desde el notch (opcional y desactivado por defecto). Las sesiones en vivo
   vienen marcadas (y recomendadas) solo con «Programo con Claude Code»; con los demás perfiles hay que marcarlas.
   Debajo, una línea explica que instalan hooks en `~/.claude/settings.json` y que se quitan desde la bandeja.
7. **Listo para instalar**: el resumen de todo lo anterior.

Las páginas 4 y 5 tienen un botón **Restablecer recomendados**.

## Perfiles

«—» significa que el perfil no lo toca: se queda el valor que había, o el de por defecto en una
instalación nueva.

| Ajuste (`AppConfig`)                 | Solo cuota | Programo con Claude Code | Notch completo | Minimalista / juego |
| ------------------------------------ | :--------: | :----------------------: | :------------: | :-----------------: |
| Notch (`NotchEnabled`)               | no         | sí                       | sí             | sí                  |
| Raya en reposo (`NotchMinimal`)      | —          | no                       | no             | sí                  |
| Ocultar en pantalla completa         | —          | —                        | —              | sí                  |
| Sonidos (`NotchSound`)               | —          | sí                       | sí             | no                  |
| Reducir movimiento                   | —          | —                        | —              | sí                  |
| Música                               | —          | sí                       | sí             | —                   |
| Letras sincronizadas                 | —          | sí                       | sí             | no                  |
| Letra en el notch pequeño            | —          | no                       | sí (con carátula y anillo) | no      |
| Reloj y gato en reposo               | —          | no                       | sí             | no                  |
| Tareas y recordatorios               | —          | —                        | sí             | —                   |
| Sesiones en vivo (hooks)             | no         | se ofrecen, marcadas     | se ofrecen     | no                  |

- **Personalizado** no fija nada: se aplica lo que marques. En una instalación nueva, las casillas parten del
  perfil recomendado.
- **Sesiones en vivo**: tocan la configuración global de Claude Code, y desde la bandeja el mismo cambio pide
  confirmación con «No» por defecto. Por eso solo vienen marcadas con «Programo con Claude Code», que es su razón de
  ser. Si ya estaban instaladas, la casilla sale marcada y deshabilitada.
- **Permisos desde el notch**: ningún perfil los activa. Se marcan aparte en la página de Claude Code. Necesitan el
  notch y las sesiones en vivo; sin hooks, `--apply-setup` los deja apagados.
- **Reducir movimiento** es de accesibilidad: solo lo enciende Minimalista y ningún perfil lo apaga.
- La **bandeja de archivos** va siempre con el notch; no tiene interruptor.

La tabla vive en `Config/SetupProfiles.cs`. El `[Code]` del instalador tiene una copia literal (`ProfileTable`), y
`InstallerScriptTests` comprueba que coinciden.

## Recomendaciones según el equipo

Están en `SetupRecommendations`, y el instalador usa las mismas reglas:

- **Perfil**: «Programo con Claude Code» si existe `~/.claude` (o `CLAUDE_CONFIG_DIR`); si no, «Solo cuota», con un
  aviso de que Muesca necesita Claude Code para leer la cuota.
- **Monitor**: solo se pregunta con 2 o más monitores. Se recomienda «El principal», que sigue al principal
  aunque cambie. Los nombres siguen la misma regla que el menú de la bandeja: «Monitor 2 · 2560×1440 · arriba».
- **Tamaño**: «Auto», indicando cuánto será en esa pantalla según su ancho lógico (ancho ÷ escala DPI):
  130 % desde 3000 px, 115 % desde 2400 px y 100 % por debajo.
- **Abrir el notch**: con un clic si hay otro monitor justo encima del notch (el ratón lo cruzaría al subir) o si la
  pantalla tiene menos de 1440 px lógicos de ancho; si no, al pasar el ratón por encima.
- **Idioma**: «Como Windows». **Iniciar con Windows**: sí.

## `--apply-setup`

```json
{
  "version": 1,
  "profile": "coder",
  "liveSessions": true,
  "startWithWindows": true,
  "overrides": { "Language": "system", "NotchOpenOnClick": false, "NotchPermissions": false }
}
```

- `profile`: `quota`, `coder`, `full`, `minimal` o `custom`.
- `overrides`: solo los ajustes de la lista blanca de `SetupSettings`. Una clave desconocida o un valor fuera de rango
  se ignora y se anota en `claudebar.log`.
- Orden de aplicación: primero el perfil; después lo recomendado para el equipo, solo si no había config; y por último
  las respuestas.
- **Con config previa**: antes de tocarla se copia a `config.json.bak-setup-<fecha>` y solo cambia lo elegido. En una
  actualización, el instalador solo manda lo que cambió respecto a lo que enseñó.
- `liveSessions`: instala los hooks con el mismo `HookInstaller` que la bandeja, y con copia de `settings.json`. Solo
  lo hace si el perfil los usa y Claude Code está instalado; si ya estaban, no se reescribe nada. Se instalan
  después de guardar `config.json`; si fallan, la config se vuelve a guardar sin sesiones en vivo ni permisos.
- `startWithWindows`: si no viene, no se toca.
- Si `config.json` existe pero no se puede leer en ese momento (antivirus, OneDrive…), o no se puede guardar, no se
  aplica nada: antes se guardaba el perfil encima de los valores por defecto.
- Códigos de salida: `0` aplicado, `1` aplicado pero fallaron los hooks, `2` no existe el JSON, `3` JSON no válido
  (no parsea, versión o perfil desconocidos), `4` no se pudo copiar, leer o guardar `config.json`. Con 2, 3 y 4 no
  se toca nada.
- El instalador lo lanza desde `[Code]` (`CurStepChanged(ssPostInstall)`), antes de arrancar la app, y mira el
  código: con `1` avisa de que las sesiones en vivo no se activaron; con `2` a `4`, de que no se aplicó la
  configuración (con el código y la ruta de `claudebar.log`). Si la app no llega a arrancar (error de Windows) o se
  cae a medias (cualquier otro código), lo dice con su propio aviso.
- Al acabar se borra el JSON, solo si se llama `claudebar-setup*.json` y está directamente en una carpeta `is-*.tmp`
  (la `{tmp}` de Inno) de la carpeta temporal del usuario. Un archivo pasado a mano no se borra nunca.

## Instalación silenciosa y actualizaciones automáticas

Con `/SILENT` o `/VERYSILENT` (NetSparkle, winget) el instalador no pregunta nada, no escribe el JSON ni ejecuta
`--apply-setup`. Se comporta como antes: instala y vuelve a lanzar la app.

# Capturas de la ficha de la Store

Diez capturas por idioma (el máximo de escritorio es 10 y el mínimo, 1), en este orden. Salen de `OnboardingShotsTests`,
que pinta con los painters de verdad y datos de ejemplo (nunca los del usuario), y se componen a 2560 × 1440 con
`store/compose-screenshots.py`. Cada una lleva su pie en el apartado «Títulos de las capturas» de la ficha de su idioma
(`store/listing/<idioma>.md`), en el mismo orden.

Requisitos de Partner Center: PNG, de 1366 × 768 o más (admite hasta 3840 × 2160) y de menos de 50 MB; pies de 200
caracteres como mucho ([Screenshots and images](https://learn.microsoft.com/en-us/windows/apps/publish/publish-your-app/msix/screenshots-and-images)).
Las del notch solo miden 1400 × 544 a 2x: por eso se componen todas en un lienzo de 2560 × 1440, centradas a su tamaño,
con las esquinas redondeadas y sombra sobre el negro y el violeta de la marca.

| # | Archivo de `OnboardingShotsTests` (`{lang}` = es, en, nl, fr, de, ja, ko, zh-Hant) | Qué enseña | Edición |
| - | ------------------------------------------------------------------------------- | ---------- | ------- |
| 1 | `tutorial-{lang}-02-sesiones.png` | El notch con las sesiones de Claude Code (trabajando, esperando, terminada) | Gratis |
| 2 | `tutorial-{lang}-03-permisos-armandose-pro.png` | Permitir / Denegar un permiso desde el notch, con la vista previa del comando | Pro |
| 3 | `uso-{lang}-5-cuota-hover.png` | La página Uso: cuota 5 h con el detalle al pasar el ratón | Pro (la página es gratis; el hover, Pro) |
| 4 | `tutorial-{lang}-05-musica-letra-hecho.png` | Música y letra sincronizada | Pro |
| 5 | `tutorial-{lang}-06-tareas-recordatorio.png` | Tareas con recordatorio | Pro |
| 6 | `tutorial-{lang}-07-bandeja-arrastrando.png` | La bandeja de archivos, soltando un archivo | Pro |
| 7 | `foco-{lang}-1-en-marcha.png` | El pomodoro de la página Foco | Gratis |
| 8 | `uso-{lang}-1-gasto.png` | Gasto estimado por modelo | Gratis |
| 9 | `onboarding-{lang}-2-perfiles.png` | El asistente de bienvenida: los perfiles | Gratis |
| 10 | `pro-{lang}-4-tarjeta-store.png` | La tarjeta del Pro de la edición de la Store (precio de la Store y prueba) | Pro |

La primera es la que más se ve (la Store la enseña en los resultados): es lo que hace la app gratis y lo que la distingue.
Las Pro llevan «Pro:» en el pie (política 10.1: que nadie crea que lo Pro viene gratis).

## Cómo sacarlas (Windows)

```powershell
$env:SHOTS_DIR = "C:\shots"
$env:SHOTS_LANGS = "all"          # los 8 idiomas (sin la variable, solo es y en)
dotnet test ClaudeBarWin.Tests -c Release --filter OnboardingShotsTests
pip install pillow                # una vez
python store/compose-screenshots.py C:\shots C:\shots\store
```

Quedan en `C:\shots\store\<idioma>\01-sesiones.png` … `10-pro.png`. Súbelas en ese orden en la ficha de cada idioma
(*Store listings › <idioma> › Screenshots › Desktop*) y pega su pie.

## Antes de subirlas

- **El nombre.** Las capturas se pintan con los textos de la app: tienen que salir ya con «Muesca» (lo cambia la rama del
  nuevo nombre). Si alguna dice «ClaudeBar», no se sube.
- **La tarjeta del Pro (10).** Pinta un precio de ejemplo (`9,99 €`): en otros idiomas y mercados la Store cobra en la
  moneda local. Si molesta, quitar esa captura (quedan 9) o recortar el precio.
- **Datos de ejemplo.** Ninguna enseña datos reales; los nombres de proyecto y las rutas son inventados. Revisarlas igual.
- **Contraste.** El fondo compuesto es oscuro: comprobar que el borde del notch se distingue en la miniatura de la Store.
- Las capturas son de la misma versión que se sube (política 10.1: deben reflejar la app de verdad).

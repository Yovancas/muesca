# Ficha de Muesca en la Microsoft Store

Un archivo por idioma de la app (`en.md`, `es.md`, `nl.md`, `fr.md`, `de.md`, `ja.md`, `ko.md`, `zh-Hant.md`), con los
mismos apartados, para copiar y pegar en Partner Center (*Store listings*, un listado por idioma). Lo que no depende del
idioma está aquí. Paso a paso de la subida: [`docs/store-submission.md`](../../docs/store-submission.md); riesgos de
política: [`docs/msix.md`](../../docs/msix.md). `StoreListingTests` comprueba que están los 8 y que se respetan los límites.

Tono: honesto. Es una app **para** Claude Code, **no afiliada a Anthropic**; la dependencia va en la primera línea de la
descripción (política 10.2.4) y lo Pro va marcado como Pro (10.1, 10.8.4).

## Idiomas

| Archivo | Idioma en Partner Center | `Resource Language` del manifiesto |
| ------- | ------------------------ | ---------------------------------- |
| `en.md` | English (United States) | `en-US` (el predeterminado) |
| `es.md` | Spanish (Spain) | `es-ES` |
| `nl.md` | Dutch (Netherlands) | `nl-NL` |
| `fr.md` | French (France) | `fr-FR` |
| `de.md` | German (Germany) | `de-DE` |
| `ja.md` | Japanese | `ja-JP` |
| `ko.md` | Korean | `ko-KR` |
| `zh-Hant.md` | Chinese (Traditional, Taiwan) | `zh-TW` |

Partner Center pide una ficha por cada idioma que declara el paquete (`<Resources>` del manifiesto; política 10.7).

## Apartados de cada archivo y límites de Partner Center

| Apartado | Campo de Partner Center | Límite |
| -------- | ----------------------- | ------ |
| Nombre | *Product name* (el nombre reservado) | — |
| Subtítulo | No hay campo propio: es la primera línea de las capturas y la frase de la descripción corta. | 50 caracteres (nuestro) |
| Descripción corta | *Short description* | Mejor menos de 270 (el campo admite 1000) |
| Descripción | *Description* | 10 000 caracteres, texto plano |
| Novedades de esta versión | *What's new in this version* | 1500. **En la primera publicación se deja vacío** («first time you're submitting your app, leave this field blank»): el texto es para la siguiente. |
| Funciones | *Product features* | 20 como mucho, 200 caracteres cada una |
| Palabras clave | *Search terms* | 7 como mucho, 40 caracteres cada una y 21 palabras en total. Sin nombres de otros productos (ni «Claude» ni «Anthropic») ni términos de precio (10.1.3). |
| Requisitos | Va en la descripción (primera línea). *Additional system requirements* se deja vacío: es solo de hardware. | — |
| Complemento Pro: título | Título del listado del complemento | 100 |
| Complemento Pro: descripción | Descripción del listado del complemento | 200 |
| Complemento Pro: palabras clave | *Keywords* de las propiedades del complemento | 10, de 30 caracteres |
| Títulos de las capturas | El pie de cada captura, en el orden de [`store/screenshots.md`](../screenshots.md) | 200 cada uno |

Fuentes: [Store listing](https://learn.microsoft.com/en-us/windows/apps/publish/publish-your-app/msix/add-and-edit-store-listing-info),
[palabras clave](https://learn.microsoft.com/en-us/windows/apps/publish/publish-your-app/pwa/add-and-edit-store-listing-info),
[complementos](https://learn.microsoft.com/en-us/windows/apps/publish/publish-your-app/add-on/create-app-store-listing),
[políticas 10.1.3, 10.2.4 y 10.7](https://learn.microsoft.com/en-us/windows/apps/publish/store-policies).

## Igual en todos los idiomas

- **Nombre:** Muesca. **Editor:** Bekay (el nombre de la cuenta de Partner Center).
- **Política de privacidad:** https://github.com/Yovancas/muesca/blob/main/PRIVACY.md
- **Soporte:** https://github.com/Yovancas/muesca/issues
- **Sitio web:** https://github.com/Yovancas/muesca
- **Copyright:** © 2026 Bekay
- **Categoría:** Herramientas para desarrolladores (*Developer tools*); secundaria, Productividad.
- **Precio de la app:** gratis, sin prueba de la app (la prueba es la del Pro, abajo).

## Complemento «Pro»

| Dato | Valor |
| ---- | ----- |
| Tipo | Duradero (*Durable*). No se puede cambiar después. |
| Product ID | `Pro` (exacto: la app lo busca por su `InAppOfferToken`, `ProCatalog.AddOnToken`). No se puede cambiar después. |
| Duración | Para siempre (*Forever*), la predeterminada. |
| Precio | **9,99 €** de lanzamiento; después, **12,99 €**. |
| Prueba | **7 días**, la gestiona la app: empieza sola el primer arranque de la edición de la Store. Partner Center no la configura: la prueba gratis solo existe para los complementos de suscripción («Not applicable to other product types»). |

**Cómo poner el precio.** En Partner Center se elige un precio base (por niveles) y se ajusta por mercado si hace falta.
Para el lanzamiento: precio base del nivel de 9,99 € y, cuando acabe el lanzamiento, una nueva publicación del
complemento con el de 12,99 €. **No** se recomienda publicar a 12,99 € con una oferta a 9,99 €: la Store enseñaría
12,99 € tachado sin que nadie lo haya pagado nunca, y eso es un precio de referencia ficticio (política 10.8.7, la guía
de la FTC contra precios engañosos y, en la UE, la regla de los 30 días que la propia guía de ofertas de Partner Center
recuerda para Francia). Por lo mismo, las descripciones dicen «precio de lanzamiento» sin cifras ni tachados: la Store
enseña el precio del complemento en la moneda de cada mercado.

## Cuestionario de edad (IARC)

Respuestas sugeridas (el cuestionario se rellena en *Age ratings*; la calificación es de quien publica, política 11.11):

| Pregunta | Respuesta | Por qué |
| -------- | --------- | ------- |
| Categoría | Utilidad, productividad, comunicación u otra (no es un juego, ni una red social, ni un navegador) | Herramienta de escritorio |
| Violencia, miedo, sexo, desnudos, drogas, alcohol, tabaco, juego con dinero, lenguaje soez, humor grosero | No | La app no trae ese contenido |
| ¿Los usuarios pueden comunicarse o compartir contenido con otros usuarios? | No | No hay chat, perfiles ni contenido compartido |
| ¿Comparte la ubicación del usuario? | No | No la lee |
| ¿Acceso sin restricciones a internet (navegador)? | No | Solo habla con los servicios de la tabla de datos de abajo |
| ¿Compras digitales dentro de la app? | Sí | El complemento «Pro» |
| ¿Contenido de terceros que no controla el desarrollador? | Responder con lo que diga el cuestionario sobre letras de canciones: la app enseña la letra de la canción que **el usuario** está escuchando (de lrclib.net) | Es lo único que llega de fuera |

Calificación esperada: la más baja (IARC 3+, PEGI 3, ESRB Everyone) con el elemento interactivo «Compras dentro de la app».
Es una estimación: la da IARC con las respuestas.

## Declaración de datos

Partner Center no tiene un formulario de datos aparte: en *Support info* pregunta si el producto accede a datos personales,
los recoge o los transmite, y con «Sí» exige la URL de la política de privacidad. Las apps Win32 / Desktop Bridge **siempre**
necesitan política de privacidad (política 10.5.1). Respuesta: **Sí**, con la URL de arriba.

Lo que hace la app, para las notas de certificación y para comprobar que `PRIVACY.md` lo cuenta todo:

| Qué | A dónde | Cuándo | Se puede apagar |
| --- | ------- | ------ | --------------- |
| Token OAuth de Claude Code (de `~/.claude/.credentials.json`, solo lectura, en memoria) | `api.anthropic.com` (cuota y plan del propio usuario) | Antes de cada consulta de cuota | Sí: Ajustes › Origen de la cuota (entonces la cuota sale de la statusline) |
| Contador anónimo de uso: versión, edición, idioma y tres casillas; sin identificadores | `claudebar-stats…workers.dev` | Una vez al día como mucho | Sí: Ajustes › Sistema, la bienvenida o `CLAUDEBAR_NO_STATS=1` |
| Título y artista de la canción que suena | `lrclib.net` | Con las letras sincronizadas (Pro) encendidas | Sí: el interruptor de las letras en los ajustes del notch |
| Estado público del servicio | `status.claude.com` | De vez en cuando | — (sin datos del usuario) |
| Tarifas públicas de los modelos | `models.dev` | De vez en cuando, GET anónimo | Sí: Ajustes › Sistema |
| Licencia y compra del Pro | Microsoft Store (`Windows.Services.Store`) | Al arrancar y al comprar | — (la gestiona Windows) |

La edición de la Store **no** busca actualizaciones en GitHub (las da la Store). Nada de los transcripts, los prompts ni el
código del usuario sale del equipo.

**Pendiente antes de enviar:** `PRIVACY.md` no nombra todavía lrclib.net ni la licencia de la Store. Texto propuesto en
[`docs/msix.md`](../../docs/msix.md#privacidad-lo-que-falta-en-privacymd).

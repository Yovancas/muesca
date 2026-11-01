## Qué cambia

<!-- Resumen breve del cambio y por qué. Enlaza el issue si lo hay (Closes #123). -->

## Cómo se ha probado

- [ ] `dotnet build ClaudeBarWin.sln -c Release` sin avisos nuevos
- [ ] `dotnet test ClaudeBarWin.sln -c Release` en verde
- [ ] Probado a mano en la app (si toca UI o integración con Claude Code)

## Checklist

- [ ] Tests nuevos o actualizados para el cambio de comportamiento
- [ ] `CHANGELOG.md` actualizado en `Unreleased` (si lo nota el usuario)
- [ ] Sin tokens, credenciales ni datos personales en el diff, logs o capturas
- [ ] Lo que ve el usuario dice «Muesca» (nunca «ClaudeBar») y los nombres internos no cambian (`docs/branding.md`)
- [ ] Si toca `settings.json`, hooks, statusline o credenciales: revisado contra `SECURITY.md`

## Capturas

<!-- Si cambia la interfaz, antes/después. -->

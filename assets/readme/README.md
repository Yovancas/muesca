# Capturas del README

Las genera `OnboardingShotsTests.Writes_the_readme_shots` con los painters de verdad (a 2x, datos de ejemplo del
tutorial). En Windows:

```powershell
$env:SHOTS_DIR = "C:\shots"; dotnet test ClaudeBarWin.Tests --filter OnboardingShotsTests
Copy-Item C:\shots\readme\* assets\readme -Recurse -Force
```

`en/` va en README.md y `es/` en README.es.md, con los mismos nombres: `welcome`, `tutorial`, `notch-idle`,
`notch-sessions`, `notch-permission`, `notch-music`, `notch-tasks`, `notch-shelf`, `notch-usage`, `notch-focus` y `pro`.

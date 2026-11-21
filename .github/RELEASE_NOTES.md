**OZPanorama {VERSION}** - now with an installer.

### Downloads
- **OZPanorama_Setup_{VERSION}.exe** - the normal installer (English, Português, Español, Français). Installs for your user only, no administrator rights, with Start menu and optional desktop shortcut. Uninstalling never deletes the backups in `Documents\OZPanorama\backups`.
- **OZPanorama-{VERSION}.zip** - portable version: extract and open `OZPanorama.exe`, no install.

Both are **not digitally signed yet**: SmartScreen may show "Windows protected your PC" > "More info" > "Run anyway". Only do it if the SHA-256 below matches.

### In this Beta
- Simulators: MSFS 2024, ETS2, ATS, iRacing and **RaceRoom Racing Experience** (triple screen multiview from your rig measurements).
- Side angle from the distance between the outer ends of the left and right monitors (works with only the diagonal too); top view drawn as you type.

### Verify the files (SHA-256)
```
{SHA256SUMS}
```
In PowerShell: `Get-FileHash .\OZPanorama_Setup_{VERSION}.exe -Algorithm SHA256` - the value must match the one above.

Tested on the author's rig only. See [known limitations](https://github.com/OptimusZuka/OZPanorama/blob/main/docs/LIMITATIONS.md) and use **Report a problem** in the app.

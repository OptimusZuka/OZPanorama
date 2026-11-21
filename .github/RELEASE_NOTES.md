**OZPanorama {VERSION}** - monitor detection fix for NVIDIA Surround.

### Fixed
- Detect monitors with NVIDIA Surround on: the same monitor no longer appears several times (one entry per physical monitor, read from its EDID), and monitors inside Surround are no longer marked "not connected now?". The three Surround monitors are placed automatically again.

Also includes everything from 0.1.3 (fixes from the full code audit).

### Downloads
- **OZPanorama_Setup_{VERSION}.exe** - installer (English, Português, Español, Français), for your user only, no administrator rights. Uninstalling never deletes the backups in `Documents\OZPanorama\backups`.
- **OZPanorama-{VERSION}.zip** - portable version.

Both are **not digitally signed yet**: SmartScreen may show "Windows protected your PC" > "More info" > "Run anyway". Only do it if the SHA-256 below matches.

### Verify the files (SHA-256)
```
{SHA256SUMS}
```
In PowerShell: `Get-FileHash .\OZPanorama_Setup_{VERSION}.exe -Algorithm SHA256` - the value must match the one above.

Tested on the author's rig only. See [known limitations](https://github.com/OptimusZuka/OZPanorama/blob/main/docs/LIMITATIONS.md) and use **Report a problem** in the app.

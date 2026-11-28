**OZPanorama {VERSION}** - Assetto Corsa, Le Mans Ultimate (experimental) and the recommended NVIDIA bezel correction.

### New
- **Assetto Corsa** (tested with Content Manager): writes `triple_screen.ini` with the position of each screen and turns on `MODE=TRIPLE` in `video.ini`, the same setting Content Manager uses. With Surround bezel correction, each third of the image includes the hidden pixels, so the image stays aligned across the bezels.
- **Le Mans Ultimate** (experimental): turns on the three views (Multiview) and writes `ViewParams`, `LeftView` and `RightView` in `Config_DX11.ini`, fitted to the game's own screen model so that every visible pixel points in the real direction. Finds the game in your Steam libraries.
- **Recommended NVIDIA bezel correction**: from the case and visible widths of your monitors, the app shows how many pixels the Surround should hide per join, and warns you when your Surround hides a different amount. The angle and the eye distance do not change this value.

### Changed
- `.ini` settings can now be changed only inside their own section (for example `MODE` in `[CAMERA]`), so a key with the same name in another section is never touched.

Before using Assetto Corsa or Le Mans Ultimate, read the [known limitations](https://github.com/OptimusZuka/OZPanorama/blob/main/docs/LIMITATIONS.md).

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

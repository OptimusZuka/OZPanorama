**OZPanorama {VERSION}** - side angle and top view fixes.

### Fixed
- The distance between the outer ends of the left and right monitors can now be typed in centimetres too (131 is read as 131 cm = 1310 mm). Before, a value in cm only showed "This distance does not fit these monitors".
- A distance saved without an angle is calculated again when the app opens.
- The top view now appears as soon as the side angle is known, with the head drawn at 650 mm until you enter your eye distance. Without the angle, it says what is missing.

Also includes 0.1.5 (own app window, new icon, monitor detection with NVIDIA Surround).

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

**OZPanorama {VERSION}** - its own app window, new icon and a monitor detection fix for NVIDIA Surround.

### New
- OZPanorama now opens in its own window (Microsoft WebView2, already part of Windows 10/11) instead of a browser window, with its own icon on the taskbar. No extra files are installed. If WebView2 is missing, it falls back to the Edge app window, as before.
- Sharper app icon at every size (16 to 256 px).

### Fixed
- With NVIDIA Surround on, the same monitor no longer appears several times. Windows keeps old entries for every port a monitor was ever plugged into, and some of them carry a shorter EDID. Monitors are now grouped by their EDID identity (maker, model, serial, date, size), and with Surround on one copy is counted per port where that model was seen, so three identical monitors still count as three.
- Monitors hidden inside Surround are shown as "state unknown", never as "not connected now?".

Also includes everything from 0.1.4 and 0.1.3.

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

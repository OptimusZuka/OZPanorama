**OZPanorama {VERSION}**

### New
- **RaceRoom Racing Experience**: triple screen (multiview) set up from your rig measurements - distance, screen sizes, bezels and side angles written to `graphics_options.xml`, with preview, checked backup and restore. Tested on the author's rig with NVIDIA Surround.

### Fixed
- The side angle from the outer-ends distance now works with only the monitor diagonal (no exact widths needed).
- The top view is drawn as soon as there are sizes, angles and distance, even before the resolutions are filled in.

### Install
Download **OZPanorama-{VERSION}.zip**, extract it to a folder of your own and open `OZPanorama.exe`. No install needed.
The exe is **not digitally signed yet**: SmartScreen may show "Windows protected your PC" > "More info" > "Run anyway". Only do it if the SHA-256 below matches.

### Verify the file (SHA-256)
```
{SHA256SUMS}
```
In PowerShell: `Get-FileHash .\OZPanorama-{VERSION}.zip -Algorithm SHA256` - the value must match the one above.

Tested on the author's rig only. See [known limitations](https://github.com/OptimusZuka/OZPanorama/blob/main/docs/LIMITATIONS.md) and use **Report a problem** in the app.

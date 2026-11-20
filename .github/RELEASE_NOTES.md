**First public Beta of OZPanorama.** Triple monitors done right in MSFS 2024, ETS2, ATS and iRacing.

- Calculation from your real rig measurements, preview of every change, checked backup, atomic write and restore.
- Detect monitors (also with NVIDIA Surround on), bezels from Surround or the manufacturers' sheets.
- Side angle from the distance between the outer ends of the left and right monitors.
- Top view to scale with a head in real proportions.
- NVIDIA Surround on/off with one click (driver profiles, confirmation, automatic rollback in 20 s).
- English, Português (Brasil), Español, Français.

### Install
Download **OZPanorama-{VERSION}.zip**, extract it to a folder of your own and open `OZPanorama.exe`. No install needed.
The exe is **not digitally signed yet**: SmartScreen may show "Windows protected your PC" > "More info" > "Run anyway". Only do it if the SHA-256 below matches.

### Verify the file (SHA-256)
```
{SHA256SUMS}
```
In PowerShell: `Get-FileHash .\OZPanorama-{VERSION}.zip -Algorithm SHA256` - the value must match the one above.

Tested on the author's rig only. See [known limitations](https://github.com/OptimusZuka/OZPanorama/blob/main/docs/LIMITATIONS.md) and use **Report a problem** in the app.

**OZPanorama {VERSION}** - update notice.

### New
- **Update notice**: when it opens, OZPanorama checks GitHub for a newer version. If there is one, a notice in the side panel shows it, with "See what's new" (opens the version's page) and "Not now" (hides it for that version). Nothing is downloaded or installed by itself.
- **What is sent**: one request to GitHub's public API asking for the list of versions. No data about you, your PC or your rig. Without internet nothing is shown and nothing fails.
- **Turn it off**: untick "Check for updates when opening" in the side panel.

Versions 0.1.8 and earlier don't have the notice: install this one to be told about the next ones.

Everything from 0.1.8 is still here: two monitors in ETS2, ATS and MSFS 2024, game artwork, Le Mans Ultimate tested. See [known limitations](https://github.com/OptimusZuka/OZPanorama/blob/main/docs/LIMITATIONS.md).

### Downloads
- **OZPanorama_Setup_{VERSION}.exe** - installer (English, Português, Español, Français), for your user only, no administrator rights. Uninstalling never deletes the backups in `Documents\OZPanorama\backups`.
- **OZPanorama-{VERSION}.zip** - portable version.

Both are **not digitally signed yet**: SmartScreen may show "Windows protected your PC" > "More info" > "Run anyway". Only do it if the SHA-256 below matches.

### Verify the files (SHA-256)
```
{SHA256SUMS}
```
In PowerShell: `Get-FileHash .\OZPanorama_Setup_{VERSION}.exe -Algorithm SHA256` - the value must match the one above.

Tested on the author's rig only. Use **Report a problem** in the app.

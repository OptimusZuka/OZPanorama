**OZPanorama {VERSION}** - two monitors in ETS2/ATS and fixes from the technical audit.

### New
- **Two monitors in Euro Truck Simulator 2 and American Truck Simulator** (experimental): choose which monitor is the main one. With the main on the right, the left monitor shows the left window; with the main on the left, the right monitor shows the right window. Uses the game's custom mode (`r_multimon_mode "4"`), the only one that accepts two monitors.

- **Game artwork**: each simulator now has its own picture in the list, in the app's black and yellow style with a color strip per game.

- **Le Mans Ultimate is now "Tested"**: validated on the author's rig (three views, alignment and Surround resolution).

### Fixed
- **MSFS 2024**: with "Hide the image behind the bezels" on, the calculated values (above 90) were refused and nothing could be applied.
- **MSFS 2024 installed on Steam and on the Microsoft Store**: the app now changes the one used last and says which one.
- **Change detected before writing**: if the game saved its file between your confirmation and the write, the app now says that nothing was written. Before, it suggested restoring a backup, which would undo the game's change.
- **Restore that fails half-way** (two files): the files already restored go back automatically to how they were before the restore.
- **Interrupted apply**: the backup is marked before the first write, so an interruption (power cut, app closed) shows in "Backups and restore".
- **A failure only saving the backup record** no longer undoes a correct, verified apply.
- **Detect monitors**: a single 4K or super-ultrawide monitor is no longer taken for NVIDIA Surround. Two monitors in NVIDIA Surround or AMD Eyefinity are detected and choose the two-monitor mode.
- **Symbolic links**: a configuration file that is a link is refused instead of being replaced by a normal file.
- Eye height above the screen center now has limits, and request bodies sent without a size are limited too.

Before using two monitors, read the [known limitations](https://github.com/OptimusZuka/OZPanorama/blob/main/docs/LIMITATIONS.md).

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

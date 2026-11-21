**OZPanorama {VERSION}** - fixes from a full code audit (security, data safety and robustness).

### Fixed
- Applying: if another program changes a game file in the middle of an apply, the automatic recovery no longer writes the original over that change.
- Surround: the 60 s time limit now always works, the window stays responsive while Surround is switching, only one switch runs at a time, and the "Keep?" window is in your language.
- Detect monitors: three monitors of the same model are three (and are placed automatically); large TVs are no longer hidden.
- Side angle from the outer ends: monitors in a straight line (0°) are accepted.
- iRacing: a warning when the left and right angles differ (iRacing uses one angle).
- ETS2/ATS: the multimon_config.sii is checked line by line before it is written.
- Restore: only accepts the game's own files from a backup record.
- Problem report: the user folder is hidden whatever its letter case.
- The Edge window profile and the Surround helper moved to %LOCALAPPDATA%\OZPanorama (not synced by OneDrive); the log rotates at 1 MB.
- Build: GitHub Actions pinned by commit; Inno Setup version pinned.

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

# OZPanorama Beta 0.1 — known limitations

- **Tested on one rig only**: three 27" 16:9 monitors (curved 1500R center), NVIDIA, Windows 11. Ultrawide, 4K, mixed monitors, AMD and Windows 10 have not been tested.
- **Not tested on other computers yet**: until there are testers, other Documents folders (OneDrive, another drive) are not confirmed.
- **MSFS 2024**: you must first create the two extra windows in the simulator (one per monitor, full screen). The app does not create windows or change monitor numbers; it asks which window is on the left. MSFS uses the same FOV in every window, so the outer edges of the side screens keep a small perspective error. The recommended zoom has to be set by hand in developer mode. MSFS 2020 is not supported.
- **ETS2 / ATS**: need Surround (or Eyefinity) turned on. The app does **not** change the game resolution; if it differs from the Surround area, it warns you and you choose it in the game options.
- **iRacing**: only accepts three equal monitors and the same angle on both sides. The 7 keys must already exist in `rendererDX11Monitor.ini` (iRacing creates it on first run).
- **Assetto Corsa and ACC**: not in this Beta.
- **NVIDIA Surround on/off**: uses profiles saved by the NVIDIA driver itself (you save them once). After switching, the app asks you to confirm and puts back the previous setup after 20 s without an answer. Tested on the author's rig only; AMD Eyefinity is not supported.
- **Monitor detection (EDID)**: sizes come in whole millimetres or centimetres, and on a curved monitor it is usually the chord. With Surround on, monitors are read from the Windows registry and old monitors may also appear. A tape measure is more accurate.
- **Unsigned executable**: SmartScreen and some antivirus programs may warn (see README).
- **Window**: opens in Microsoft Edge app mode; without Edge, it opens in the default browser.

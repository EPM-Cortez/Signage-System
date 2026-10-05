# Chromium and Edge kiosk player

1. Create a dedicated, non-administrator OS account for the display.
2. Open `https://signage.example.edu/player/` in current Chromium or Edge.
3. Keep the six-digit code visible and approve it from Admin → Pair a display, choosing the intended screen group.
4. Confirm the player downloads and starts the package, then restart the browser and test with the network disconnected.
5. Configure the browser to launch after sign-in in kiosk/full-screen mode.

Examples:

```text
msedge.exe --kiosk https://signage.example.edu/player/ --edge-kiosk-type=fullscreen --no-first-run
chromium --kiosk --noerrdialogs --disable-session-crashed-bubble https://signage.example.edu/player/
```

Use an OS policy to suppress sleep, screen blanking, update prompts, password saving, and unrelated navigation. Allow browser security updates and schedule controlled reboots. Do not use `--ignore-certificate-errors`; deploy a certificate trusted by the device.

The player requests a screen wake lock when supported. It reports the playing content ID, item index, browser summary, storage estimate, and last error in heartbeats. Revocation clears the server-side token hash immediately; the player may continue the already-cached package but cannot fetch assignments or content again.

For recovery, clear site data only when re-pairing is intended. Clearing it removes the credential and offline package. A normal browser restart should retain both.

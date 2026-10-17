# Lunar127 0.3.1 TIFF-GIF-HOLO — HOTFIX 4

This revision targets the post-build **starts, then disappears before the virtual console becomes usable** failure mode.

- GUI harness is console-attached so startup/fatal diagnostics stay visible.
- Direct console mode is the default; loopback is opt-in.
- Loopback client callbacks are exception-contained.
- Image execution starts only after the WinForms shell is visible.
- PNG asset failures become logged placeholders instead of fatal boot failures.
- `DIAGNOSE.cmd` performs both container verification and semantic runtime/asset smoke loading for GIF and TIFF.
- The batch shell enters a persistent `cmd /k` prompt after exit/failure.

The TIFF/GIF processable-hologram carriers are unchanged.

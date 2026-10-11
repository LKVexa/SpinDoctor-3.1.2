# Lunar127 0.3.1 TIFF-GIF-HOLO — Hotfix 2

Hotfix 2 addresses the Windows shell/evaluator disappearing immediately after startup and makes startup failures diagnosable rather than transient.

## Changes

1. `BOOT.cmd` no longer uses `start ...` followed by `exit /b`. The evaluator runs in the foreground, so the command shell remains open for the entire GUI session.
2. The boot shell now performs a carrier verification before GUI startup and always pauses after evaluator exit, including normal exit.
3. Runtime diagnostics are written to `workspace\logs\runtime.log`.
4. WinForms UI-thread and AppDomain exception handlers are installed before the GUI message loop.
5. Rendering, frame timer, resize/show callbacks, and cross-thread UI notifications are guarded so a recoverable UI fault cannot silently terminate the process.
6. Loopback TCP startup is now non-fatal. If Windows blocks the ephemeral loopback listener, the GUI stays up in direct-console mode.
7. `BOOT_DIRECT.cmd` starts the same image machine with loopback disabled from the outset.
8. Runtime quantum faults stop the automatic quantum timer and remain visible in the GUI instead of repeatedly faulting in the background.
9. Hotfix 1's public `IDisposable.Dispose()` correction remains present.

## Start

Normal GIF carrier:

```bat
BOOT.cmd reset
```

TIFF encoding:

```bat
BOOT.cmd tiff reset
```

If Windows networking policy interferes with local loopback:

```bat
BOOT_DIRECT.cmd reset
```

The command window is now deliberately persistent. After the GUI exits it shows the evaluator exit code and, on failure, prints the runtime log before waiting for a key press.

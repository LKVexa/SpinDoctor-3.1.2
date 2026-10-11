# Lunar127 0.3.1 TIFF-GIF-HOLO — HOTFIX 4 validation

## Scope

Hotfix 4 addresses the Windows **post-build / pre-usable-console exit** path. It does not change the TIFF/GIF processable-hologram carrier bytes or the image-resident Lunar127 machine.

## Harness/boot corrections

- The GUI harness now builds as a **console-attached** .NET executable (`/target:exe`) rather than `winexe`, so fatal startup output remains visible in the invoking shell.
- `BOOT.cmd` defaults to **direct** mode and only enables the optional `127.0.0.1` listener when `loopback` is explicitly requested.
- Loopback client callbacks are wrapped in `try/catch`; a dropped/malformed connection can no longer escape a ThreadPool callback and terminate the process.
- The image quantum timer is no longer started in `RuntimeEngine`'s constructor. It starts only after the WinForms shell receives `Shown`, preventing image execution/writeback from racing the GUI's initial construction.
- Each image-resident PNG is decoded independently. A GDI+ decode problem for one asset is logged and replaced with a placeholder instead of aborting startup.
- `--smoke` constructs the semantic runtime snapshot and decodes the image-resident presentation assets without starting the GUI.
- `BOOT.cmd` performs both `--verify` and `--smoke` before launching the virtual console.
- Startup emits five explicit `BOOT-STAGE` markers, and the shell falls into `cmd /k` after a failure/exit instead of disappearing.
- Previous fixes remain: `IDisposable.Dispose()` is public and `ObjectDisposedException` is caught before `InvalidOperationException`.

## Carrier integrity

The unchanged 0.3.1 carriers independently decode to the same logical image-resident archive:

- GIF: 2,678,940 bytes, SHA-256 `96d813f3b316bfa91a96b12dc7c42ccf59cbfe0e8015d3b808add8aeef01f87d`
- TIFF: 2,825,984 bytes, SHA-256 `af3cf16feb21e8cc0315945cca0b646254d4d5e0df60f85c6bcd88da020839c8`
- Each has 9 visible frames/pages total, 8 processable data pages, 31 embedded entries, tick 0.
- Reconstructed logical archive SHA-256 for both: `cf2c906a6bca8ffeb29db1f76154111e6edc791e8b4d294b86031e3496604cc3`.

## Validation boundary

The C# source received structural/static checks in this Linux build environment, and the TIFF/GIF payloads were independently decoded and verified. This environment does **not** provide the Windows .NET Framework compiler or WinForms runtime, so native Windows compilation and GUI boot cannot truthfully be marked passed here. `DIAGNOSE.cmd` is therefore part of the Windows acceptance path: it compiles the harness, verifies both carriers, and performs semantic runtime/asset smoke loads before the GUI is attempted.

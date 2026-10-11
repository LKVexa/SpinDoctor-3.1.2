# Lunar127 0.3.1 TIFF-GIF-HOLO — Hotfix 2 validation

## Defect addressed

The prior `BOOT.cmd` launched the GUI with `start` and then immediately executed `exit /b 0`. When the package was started by double-clicking the command file, the command shell therefore disappeared by design as soon as the GUI process was spawned. If the GUI then encountered a startup/runtime exception, there was no persistent console left to show the failure.

The prior GUI path also had several termination-risk points: loopback listener construction was unguarded; there was no process-level WinForms `ThreadException` capture; and paint/timer/cross-thread UI callbacks were not isolated from recoverable exceptions.

## Hotfix 2 controls

- `BOOT.cmd` runs the GUI evaluator in the foreground and waits for it to exit.
- `BOOT.cmd` always pauses after exit or startup failure.
- `BOOT.cmd` rebuilds the evaluator every boot so an older compiled Hotfix 1 EXE cannot be reused after an overlay update.
- The active TIFF/GIF is verified with `TIFFGifHoloTool.exe --verify` before the GUI starts.
- Runtime diagnostics are written to `workspace\logs\runtime.log`.
- WinForms UI-thread and AppDomain unhandled-exception hooks are installed before `Application.Run`.
- Paint, frame-timer, resize/show, and cross-thread notification paths catch/log recoverable exceptions and keep the shell alive.
- 127.0.0.1 listener startup is non-fatal. The evaluator falls back to direct on-screen console mode if loopback creation fails.
- `BOOT_DIRECT.cmd` disables loopback intentionally for systems with restrictive local networking policy.
- The Hotfix 1 `public IDisposable.Dispose()` correction is retained.

## Static verification performed in this environment

- C# delimiter/structure scan: PASS.
- `RuntimeEngine.Dispose()` is public: PASS.
- GUI exception-mode hook present: PASS.
- guarded loopback fallback present: PASS.
- guarded render path present: PASS.
- batch launch no longer uses `start` followed by immediate `exit`: PASS.
- command scripts normalized to Windows CRLF: PASS.

## Carrier regression validation

The processable image carriers are unchanged by this shell hotfix.

- GIF SHA-256: `96d813f3b316bfa91a96b12dc7c42ccf59cbfe0e8015d3b808add8aeef01f87d`
- TIFF SHA-256: `af3cf16feb21e8cc0315945cca0b646254d4d5e0df60f85c6bcd88da020839c8`

The independent reference evaluator was rerun against both carriers. Both formats decoded, executed one complete image transaction, reread the committed state exactly, continued deterministically to modeled touchdown, and rejected deliberate processable-frame corruption. Cross-format continuation remained exact.

## Platform qualification

This build environment does not provide Windows/.NET Framework or a Windows desktop session, so the native C# GUI could not be compiled and launched here. Hotfix 2 is designed specifically so that the next Windows run will retain the shell and write a persistent runtime log if another platform-specific fault remains.

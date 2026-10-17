# SpinDoctor 3.1.2

This is the complete Windows package restored from the supplied `SpinDoctor-3.1.2.zip`, including the TIFF/GIF carrier images required to start the application. All 615 files from that package are included unchanged. The original ZIP SHA-256 is `d4045efc6660352c367e4ec198692e9b0fa89a34599d80225bf11ed242ae8cd2`.

## Download and run

1. Choose **Code > Download ZIP** on this repository, then extract the entire ZIP to a local folder.
2. Open the extracted `SpinDoctor` folder and double-click **BOOT.cmd**.
3. Leave the console open while using the terminal in your browser.

Requirements: Windows 10/11, Windows PowerShell 5.1, .NET Framework 4.8, and a modern browser. GPU verification additionally needs a compatible FP64 OpenCL GPU driver. The CPU calculator works without GPU verification; use `verify=false` for folder runs when no compatible GPU is available.

SpinDoctor runs a local C# simulation service. Opening the HTML directly provides an offline preview; running the calculator requires `BOOT.cmd`.

This supplied version supports circuits of up to **29 qubits**. It replaces the previously published source-only copy containing a separate 298-qubit update. That prior version remains available in Git history and on the preservation branch linked in `RESTORATION.md`.

## First circuit

Enter these commands in the terminal one at a time:

```text
q.circuit new bell 2
q.circuit add h 0
q.circuit add cx 0 1
q.circuit run shots 1024 basis z
```

The histogram should contain approximately equal counts for `00` and `11`.

## Package verification

From the extracted repository root, run:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\VERIFY_PACKAGE.ps1
```

This checks all 614 file hashes in the original manifest, then verifies both carrier images with the supplied harness. `BOOT.cmd verify` in the `SpinDoctor` folder also verifies the default TIFF carrier.

The original package's `PACKAGE_TOOLS.ps1` uses an older carrier basename by default. If using that tool, set `SPINDOCTOR_PACKAGE_ROOT` to the absolute `SpinDoctor` folder and pass the absolute path to `cartridge\SpinDoctor-3.1.2.tiff` or `.gif` as `-Target`. `BOOT.cmd` and `VERIFY_PACKAGE.ps1` use the delivered filenames.

## Contents and documentation

- `SpinDoctor/`: runnable launcher, harness, carriers, dashboards and documentation.
- `source/`: matching original source and sealed integrity manifests.
- `validation/`: supplied release tools, regression tests and historical validation records.
- [HANDGUIDE.pdf](HANDGUIDE.pdf), [HANDGUIDE.md](HANDGUIDE.md): user guide.
- [CSV-Input-Guide.txt](CSV-Input-Guide.txt), `Examples/`, `Reports/`: examples and sample reports.
- [RESTORATION.md](RESTORATION.md): diagnosed failure, exact restoration scope and current checks.

Git preserves the package bytes without line-ending conversion so its checksums remain valid. Runtime workspaces, generated sessions and build outputs are ignored.

## License

Copyright (C) 2026 RUSSELL PHILIP SMITHSON. SpinDoctor is licensed under GPL-3.0-or-later; see [LICENSE](LICENSE), [COPYRIGHT](COPYRIGHT), and [NOTICE](NOTICE). Bundled UltraWide preview material retains its own supplied license and notices.

SpinDoctor is a classical simulator. It does not provide quantum hardware or quantum speed-up.

# SpinDoctor 3.1.2 — Quantum Virtual Terminal

SpinDoctor is a quantum virtual terminal for Windows: a browser-based command terminal backed by a classical FP64 C# simulation engine (.NET Framework 4.8, compiled in-process by Windows PowerShell 5.1), with OpenCL GPU sampling for verification. You build and run quantum circuits, query states, run 241 ready-made presets, process whole folders of input files, and get TXT, PDF and CSV reports for every command.

This repository holds the source code, launch and build scripts, preset catalogue, documentation, examples and validation suite of **SpinDoctor 3.1.2**, including the **298-qubit update** (`3.1.2-298-20261010`).

> SpinDoctor is a classical simulator. It is not a quantum computer and gives no quantum speed-up. See [Honest limits](#honest-limits).

## Features

- **Circuits** of 1 to 298 logical qubits (`q.circuit new/add/run/state/spin/export`), with up to 59 qubits per coherent joint factor, exact global basis indices and the `mcx7` gate.
- **Queries** on the last run: probabilities, amplitudes, Bloch vectors, histograms.
- **Tranche registers** and a **procedural grid**: a very large described model (for example 1000 × 1000 addresses) whose states are computed on demand (`q.prepare`, `q.migrate`, `q.gate`, `q.state`, `q.measure`, `q.verify`).
- **Math calculators**, physics models, lessons, noise, error correction, compilation, algorithms and resource planning.
- **241 presets** (`qc-001` … `qc-241`) in 17 categories. Shot-based presets default to 1,024 shots.
- **Reports for every command**: TXT (the exact record), PDF (charts, circuit diagrams, histograms, Bloch spheres) and CSV.
- **Folder ingestion** (`q.folder`): runs every supported file in a folder as an ordered sequence, with optional automatic CPU/OpenCL GPU verification.
- **CSV equation and circuit inputs** (see `CSV-Input-Guide.txt` and `Examples/`).
- **Carrier-contained runtime**: the program ships inside a verified TIFF/GIF carrier image. `BOOT.cmd` verifies it, materializes the program and opens the terminal. No separately compiled executable is shipped.

## Repository layout

| Path | Contents |
|---|---|
| `source/mvp/` | Application source: C# engine (`quantum/`, `runtime/`), bootstrap harness, terminal HTML/JS UI, native Python/OpenCL helpers (`native/`), presets (`SPINDOCTOR_PRESETS.json`), command catalogue, launcher scripts and release notes |
| `source/integrity/immutable.json` | Sealed SHA-256 manifest of the release payload |
| `SpinDoctor/` | Runtime launcher: `BOOT.cmd` and `harness/TIFFGifHoloHarness.cs` (see `SpinDoctor/RUNTIME_LAYOUT.md`) |
| `validation/` | Release tools (`release-tools/`: carrier builder, `release.py`, test suite in `tests/`) and validation records for 3.1.1, 3.1.2 and the 298 update |
| `Examples/` | CSV examples and the 11-file 298-qubit folder run |
| `Audit/`, `AUDIT_3.1.2.md` | Audit records |
| `HANDGUIDE.md` | The user hand guide |
| `CHANGELOG.md`, `Release-Notes.txt`, `UPDATE-298.txt`, `START_HERE.txt` | Release documentation |
| `SHA256SUMS.txt`, `SpinDoctor-3.1.2.zip.sha256` | Checksums of the byte-exact release package |

### Not stored in this repository

Binary and generated files are kept out of git (see `.gitignore`): the TIFF/GIF carriers (`SpinDoctor/cartridge/`, about 33 MB), the release ZIP (about 48 MB), `HANDGUIDE.pdf`, the title-card artwork, binary sample payloads under `source/mvp/` (`.btf`, `.brw`, `.brimg`, `.bin`, provenance TIFF/GIF), generated sample reports in `Reports/`, the run-time `SpinDoctor/workspace/`, and build logs. The UltraWide Appliance preview material under `source/mvp/ultrawide-appliance/` uses its own preview license and is also not included (see `NOTICE`).

Because of that, a clone of this repository is not a complete, runnable package. To run SpinDoctor, use the release package `SpinDoctor-3.1.2.zip` (SHA-256 `d4045efc6660352c367e4ec198692e9b0fa89a34599d80225bf11ed242ae8cd2`).

## Requirements

- Windows 10 or 11 with Windows PowerShell 5.1 and .NET Framework 4.8 (both built into Windows).
- A modern web browser.
- Optional: an OpenCL-capable GPU driver for `verify=true` and the GPU checks. Python 3 with `numpy==2.3.5` only for the optional native/Python tests.

No other installs are needed.

## Run (Windows)

1. Extract `SpinDoctor-3.1.2.zip` to a local folder (not a network drive).
2. Open the `SpinDoctor` folder and run:

```bat
BOOT.cmd
```

`BOOT.cmd` verifies the carrier `cartridge\SpinDoctor-3.1.2.tiff`, compiles the generic C# harness in-process with Windows PowerShell (`Add-Type`, no temporary EXE), materializes the program into `workspace\projection\mvp` and opens the terminal in your browser. Leave the console window open while you work.

Other launcher modes:

```bat
BOOT.cmd gif          :: load from the GIF carrier instead of the TIFF
BOOT.cmd verify       :: verify the carrier only, do not launch
BOOT.cmd inspect      :: print the carrier inventory
```

Package tools (run from `workspace\projection\mvp`; set `SPINDOCTOR_PACKAGE_ROOT` to the folder containing `BOOT.cmd` when the projection is elsewhere):

```bat
VERIFY.cmd [gif|tiff]     :: check the TIFF/GIF carriers
INSPECT.cmd [gif|tiff]    :: print the carrier inventory
BUILD_HARNESS.cmd         :: confirm the generic harness compiles
DIAGNOSE.cmd              :: integrity and payload checks, no simulation
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\PACKAGE_TOOLS.ps1 -Mode Smoke
```

## Build check from source

The production C# sources compile with the .NET Framework compiler that ships with Windows. From the repository root:

```bat
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
set REFS=/r:System.dll,System.Core.dll,System.Web.Extensions.dll,System.Numerics.dll,System.Drawing.dll,System.Windows.Forms.dll

cd source\mvp\quantum
%CSC% /nologo /t:library /optimize+ %REFS% /out:%TEMP%\SpinDoctorEngine.dll QuantumCore.cs SpinDoctorTranches.cs SpinDoctorTrancheTerminal.cs SpinDoctorCircuits.cs SpinDoctorInputs.cs SpinDoctorReports.cs SpinDoctorPdfInputs.cs SpinDoctorCalculators.cs SpinDoctorAdvanced.cs SpinDoctorSyntax.cs SpinDoctorSequences.cs SpinDoctorCommandReports.cs QuantumTerminal.cs
cd ..\..\..

%CSC% /nologo /t:library %REFS% /out:%TEMP%\Harness.dll SpinDoctor\harness\TIFFGifHoloHarness.cs
%CSC% /nologo /t:library %REFS% /out:%TEMP%\TenQKernel.dll source\mvp\runtime\TenQKernel.cs
```

Tests run against fresh scratch folders and never touch a live workspace. For example:

```bat
powershell -NoProfile -ExecutionPolicy Bypass -File validation\release-tools\tests\engine_regressions.ps1 -SourceRoot source\mvp -TestRoot <new-scratch-folder>
powershell -NoProfile -ExecutionPolicy Bypass -File validation\release-tools\tests\detailed_report_regressions.ps1 -SourceRoot source\mvp -TestRoot <new-scratch-folder> -PdfChecks
```

See `validation/release-tools/README.txt` for the full test and release procedure (sealing, carrier build, assembly and packaging). Rebuilding the carriers needs the baseline carrier images from the release package.

## Quick start

In the terminal, type one line at a time:

```
q.circuit new bell 2
q.circuit add h 0
q.circuit add cx 0 1
q.circuit run shots 1000 basis z
```

The histogram shows roughly half `00` and half `11`: a Bell pair. Every command saves a report; use the report buttons to download TXT, PDF or CSV, and `q.history list` to see recent reports.

A 298-qubit example:

```
q.circuit new wide_demo 298
q.circuit add h 297
q.circuit add cx 297 0
q.circuit run shots 10240 basis z
```

## Presets

- `q.preset list` lists all 241 presets by category. `q.preset show qc-097` shows one.
- `q.preset run qc-097` runs a preset as a sequence. Many accept overrides; see `q.preset show`.
- The catalogue is `source/mvp/SPINDOCTOR_PRESETS.json`.

## Reports

- Every command produces a report automatically, including errors and status checks.
- **TXT** is the exact, complete record. **PDF** is a readable version with figures. **CSV** is a typed table of the same data.
- Native archives stay in `SpinDoctor\workspace` (`quantum-command-reports`, `sequence-reports`, `sequence-jobs`). Exporting never recalculates; it reads the saved result.
- PDF exports are limited to 512 pages, 4 MiB printable text and 32 MiB output.

## Folder ingestion (`q.folder`)

```
q.folder run path="C:/path/to/inputs" downloads=false verify=false
q.folder status
q.folder cancel
```

- Supported files: `.txt`, `.csv`, `.json`, `.qasm`, `.qasm2`, `.circuit`, `.pdf`. Other files (such as `.md`) are skipped and listed in `skippedFiles`.
- Top level only, in strict filename order. 1–128 supported files per run, 4 MiB per file, 1–128 commands per file, 4,096 characters per command, UTF-8 text.
- The first failing file stops the job.
- `verify=true` (the default) runs an automatic CPU/OpenCL GPU sample check after each file and needs a working OpenCL GPU; `verify=false` gives clearly labelled unverified receipts.
- Local drives only; links and junctions are refused. OneDrive "online-only" files may be refused, so mark the folder **Always keep on this device**.
- `Examples/298-native-run/` is a ready-made 11-file folder run for the 298-qubit update.

## Documentation

- **[HANDGUIDE.md](HANDGUIDE.md)**: the user hand guide (quick start, concepts, every command family, presets, reports, folders, input formats, limits, errors, troubleshooting). The PDF edition ships in the release package.
- [UPDATE-298.txt](UPDATE-298.txt): the 298-qubit update and how to activate it.
- [CHANGELOG.md](CHANGELOG.md), [Release-Notes.txt](Release-Notes.txt), [AUDIT_3.1.2.md](AUDIT_3.1.2.md).
- [CSV-Input-Guide.txt](CSV-Input-Guide.txt) and `source/mvp/SPINDOCTOR_COMMAND_CATALOGUE.txt`.

## Honest limits

- **Classical simulation.** SpinDoctor runs on your CPU (and GPU for verification). It is not quantum hardware and gives no quantum speed-up.
- **Exponential scaling.** A general dense n-qubit state needs 2ⁿ complex numbers (29 qubits ≈ 8 GiB). Wide registers work for structured states: 298 logical qubits, at most 59 qubits per coherent joint factor, subject to memory. A full dense 2²⁹⁸ state is never allocated. Circuits of 30–298 qubits use basis z only.
- **The procedural grid is a described model.** Its very large address space is computed on demand from a formula, not stored or measured physically.
- **Sampling has noise.** Shot histograms vary statistically.
- **Fits are not laws.** CSV equation fitting is classical least squares.
- **Verification has a scope.** `q.verify` checks sampled points of the procedural model on CPU and GPU; it does not certify every answer or unsampled state.
- **qc-198 (`q.compile synthesize`) is unavailable by design.** It needs a Clifford+T synthesizer (such as gridsynth) that SpinDoctor does not include, so it stops with "Preflight: Preset is unavailable" instead of inventing a result.
- `.md` is not a folder input format.

## License

Copyright (C) 2026 RUSSELL PHILIP SMITHSON

SpinDoctor is free software: you can redistribute it and/or modify it under the terms of the GNU General Public License as published by the Free Software Foundation, either version 3 of the License, or (at your option) any later version. It is distributed WITHOUT ANY WARRANTY. See [LICENSE](LICENSE), [NOTICE](NOTICE) and [COPYRIGHT](COPYRIGHT).

SPDX-License-Identifier: GPL-3.0-or-later

## Author

**RUSSELL PHILIP SMITHSON** — russellsmithson77@outlook.com

# SpinDoctor 3.1.2 – End-to-end results

Copyright (C) 2026 RUSSELL PHILIP SMITHSON <russellsmithson77@outlook.com>. SPDX-License-Identifier: GPL-3.0-or-later.

All runs: Windows 11, Windows PowerShell 5.1, .NET Framework 4.8 `csc.exe` that ships with Windows (no installs).
Every test used a fresh scratch folder; no live workspace was touched.

## 1. Pre-publish run: fresh copy of the staged repository (2026-10-10, ~8:00–8:10 PM PT)

The staged repository (413 files) was extracted into a new, empty folder and only the README steps were followed.

| Step | Command (from README / test README) | Result |
|---|---|---|
| Build engine | `csc /t:library /optimize+ ... QuantumCore.cs ... QuantumTerminal.cs` | PASS, exit 0 (SpinDoctorEngine.dll 814,080 bytes) |
| Build harness | `csc /t:library ... SpinDoctor\harness\TIFFGifHoloHarness.cs` | PASS, exit 0 (Harness.dll 92,672 bytes) |
| Build TenQ kernel | `csc /t:library ... source\mvp\runtime\TenQKernel.cs` | PASS, exit 0 (TenQKernel.dll 29,184 bytes) |
| 3.1.2 regressions | `v312_regressions.ps1` | PASS, 16 assertions |
| Engine regressions | `engine_regressions.ps1` | PASS after test fix (below): 13 groups, 46,960 assertions |
| Detailed report + PDF checks | `detailed_report_regressions.ps1 -PdfChecks` | PASS, 194 cumulative assertions (same as the shipped 3.1.2 baseline) |
| Presets, one per category (17) | `run_presets.ps1 -IdList qc-001,qc-010,qc-022,qc-062,qc-097,qc-117,qc-132,qc-154,qc-168,qc-180,qc-190,qc-200,qc-212,qc-220,qc-228,qc-238,qc-241` | 17/17 ok, phase=completed (qc-238 35 steps, 19.7 s) |
| Preset reports | TXT + PDF per preset and per step | 121 TXT (none empty), 104 PDF, 0 invalid (%PDF- header, %%EOF, startxref, ≥1 page) |
| Small `q.folder run` | `validation/3.1.2/handguide-inputs` (txt, circuit, csv, json, qasm, qasm2 + notes.md) | phase=completed, 7 files run, 1 skipped (notes.md, reported as expected); TXT 103,956 chars, PDF 549,717 bytes, valid |
| Carrier launch smoke (release package) | `PACKAGE_TOOLS.ps1 -Mode Build` then `-Mode Smoke` on a scratch copy of the 3.1.2 runtime with its TIFF/GIF carriers | PASS: harness compiled in-process; `PASS MVP-SMOKE`, entry mvp/RUN_SPINDOCTOR.cmd, 293 entries, exit 0 |

Categories covered: Procedural grid; Tranche controls; Single-qubit gates; Two-qubit gates and entanglement; Multi-qubit circuits;
Algorithm demonstrations; State and matrix mathematics; Joint-state analysis; Noise and mitigation; Error-correction demonstrations;
Compilation and gate synthesis; Memory and resource estimates; Alternative computation models; Guided lessons and article corrections;
Circuit presets and controls; Large structured-register demonstrations; Algorithm extensions.

### Fix applied during E2E

`engine_regressions.ps1` failed with `IndexOutOfRangeException` in `EngineRegressions.ReferenceGate`. This also fails on the
author's updated 3.1.2 folder itself, so the cause is not the repository staging. The 3.1.2 update adds the gate `mcx7`
(seven controls + target, 8 operands), which has no explicit matrix by design (`GateMatrix` refuses more than four operands).
The old test's 3-qubit reference model cannot represent it.

Fix (test code only; no production source changed): `validation/release-tools/tests/engine_regressions.cs` now
- checks `mcx7` separately: `GateMatrix("mcx7")` must be refused, and on a 9-qubit register (two operand orders) the native
  gate flips the target only when all seven controls are 1 (8 basis states × 512 amplitudes per order);
- limits the 3-qubit reference/composition loops to gates of at most three operands.

### Not covered from the git repository alone

`BOOT.cmd` needs the binary carriers `cartridge/SpinDoctor-3.1.2.tiff/.gif`, which are not stored in git. The interactive
browser terminal therefore runs from the release package; the carrier smoke above covers that path without opening the UI.

## 2. Post-push run: fresh download of `main` from GitHub (2026-10-10, 8:27–8:31 PM PT)

`main` of LKVexa/SpinDoctor-3.1.2 at commit `97722b9188e68047d7d426d21305911eff53078a` was downloaded as
`https://github.com/LKVexa/SpinDoctor-3.1.2/archive/refs/heads/main.zip` (2,999,717 bytes) into a new, empty folder and
extracted (415 files: the 414 published files plus the author's own upload `SpinDoctor_3.1.2_Command_Language_Guide.pdf`).
The `.gitattributes` rules restored CRLF line endings for `.cmd` scripts (`SpinDoctor/BOOT.cmd` checked).

Tree check before the run: all 413 planned files and `E2E_RESULTS.md` are on `main`, and every blob matches the staged copy.
GitHub stores text with LF line endings, and nine UTF-8 files lost their byte-order mark on upload. Neither changes their content.

| Step | Command | Result |
|---|---|---|
| Build engine | `csc /t:library /optimize+ ... QuantumCore.cs ... QuantumTerminal.cs` | PASS, exit 0 (SpinDoctorEngine.dll 814,080 bytes, identical size to pre-publish) |
| Build harness | `csc /t:library ... SpinDoctor\harness\TIFFGifHoloHarness.cs` | PASS, exit 0 (Harness.dll 92,672 bytes) |
| Build TenQ kernel | `csc /t:library ... source\mvp\runtime\TenQKernel.cs` | PASS, exit 0 (TenQKernel.dll 29,184 bytes) |
| 3.1.2 regressions | `v312_regressions.ps1` | PASS, 16 assertions |
| Engine regressions | `engine_regressions.ps1` (with the mcx7 test fix) | PASS, 13 groups, 46,960 assertions |
| Detailed report + PDF checks | `detailed_report_regressions.ps1 -PdfChecks` | PASS, 194 cumulative assertions |
| Presets, one per category (17) | `run_presets.ps1 -IdList qc-001,qc-010,qc-022,qc-062,qc-097,qc-117,qc-132,qc-154,qc-168,qc-180,qc-190,qc-200,qc-212,qc-220,qc-228,qc-238,qc-241` | 17/17 ok, phase=completed (qc-238 35 steps, 14.8 s) |
| Preset reports | TXT + PDF per preset and per step | 121 TXT (none empty), 104 PDF, 0 invalid |
| Small `q.folder run` | `validation/3.1.2/handguide-inputs` | phase=completed, 7 files run, 1 skipped (notes.md, expected); TXT 103,959 chars, PDF 549,692 bytes, valid |

Result: the published repository builds and passes the same suites as the pre-publish copy. No failures.

# SpinDoctor 3.1.1 Candidate → 3.1.2 audit

Prepared 2026-10-10 (PT). Source audited: `<WORK>\2026-10-10\can\outputs\SpinDoctor 3.1.1 Candidate`. It was treated as read-only, and all work was done in `<WORK>\spindoctor312\`.
Build toolchain: the existing .NET Framework `csc.exe` 4.8.9221.0 and Windows PowerShell 5.1 `Add-Type`, the same ones the project uses. Python 3.14.8 was already on the PC and was used only for `release.py`. Nothing was installed on the PC.

## 1. Inventory: 3.1.1 vs 3.0.4 (and 3.0.3)

- **Releases between 3.0.4 and 3.1.1:**
  - 3.0.5 streamed folder reports (removed the 32 MiB combined-report failure), gave folders their own progress, and hardened input reads (opened-handle path check, links refused, writers denied, 4 MiB checked before allocation).
  - 3.0.6 added automatic CPU/OpenCL-GPU `q.verify` sampling after each folder file (`verify=true` default; samples 1..16,384, later 1..16,384,000,000).
  - 3.0.7 added bounded numeric previews.
  - 3.0.8 put folder PDFs with the child command answers.
  - 3.0.9 added PDF Bloch/state figures and changed preset shot defaults.
  - 3.1.0 made `q.circuit preset` run automatically (`run=false` to only define), set the 1,024-shot defaults, and separated definition from execution reports.
  - 3.1.1 added CSV report companions and measurement-CSV equation fitting (`q.input table|fit`).
- **3.0.3 → 3.0.4** was input/PDF hardening only (see the earlier diff). The 241 preset IDs and commands are unchanged from 3.0.3 through 3.1.1.
- **Source size vs 3.0.4** (changed diff lines): QuantumTerminal +535, SpinDoctorReports +2,127, SpinDoctorSequences +1,557, SpinDoctorInputs +327, SpinDoctorCircuits +203, CommandReports +81, Calculators +9, Tranches +10. Core/Advanced/Syntax/PdfInputs/TrancheTerminal are unchanged.
- **Build/release tooling:** `validation/release-tools` (`release.py` seal/assemble/package, `build_carrier.ps1`, artwork update, 30 test harnesses). There is no shipped executable: `BOOT.cmd` compiles the carrier-contained source in-process.

## 2. Build and test results

| Check | 3.1.1 as shipped | 3.1.2 |
|---|---|---|
| csc /warn:4 /optimize+ (13 production sources) | 0 errors, 0 warnings | 0 errors, 0 warnings |
| Candidate's own source tests (17 runnable harnesses) | 16/17 pass. `native_verification_preview_regressions` FAILS ("client forged verification metadata replaced by native receipt evidence") | 17/17 pass |
| New `v312_regressions.ps1` | n/a | PASS (16 assertions) |
| All 241 presets (TXT+PDF per preset and per step) | 240 completed, qc-198 preflight-unavailable | identical outcome for all 241 presets (`preset_compare_311_312.csv`) |
| Preset report files | 1,534 PDFs (all `%PDF-…%%EOF`), 1,775 TXT, none empty | same counts, 0 bad |
| Folder chug series (3.0.4 series, 40 folders run, verify=false) | 102/103 rows as 3.0.4-expected; the one difference is 05c2 (128 commands) now completing, i.e. finding (a) already fixed | 103/103 rows match `EXPECTED_3.1.2.csv` |
| HANDGUIDE example commands | n/a | 81/81 succeed, plus 8 example input files (all formats) completed in one folder run |

Not run, with reasons:
- `04f_128_files_x_120_cmds_HEAVY` (left for a manual stress run).
- `full_folder_integration`, `model_sampling_folder`, `accounting_report_regressions`, `native_pdf_hydration_regressions` and `actual_verification_pdf`. They need input packs or captured archives that the candidate does not ship.
- Carrier and materialize tests run as part of the carrier build (`build_carrier.ps1` enforces every round-trip flag).
- Genuine GPU work: `native_spin_preview_regressions` performed 44 real CPU/OpenCL-GPU comparisons on this PC in both versions.

## 3. Findings

| # | Severity | Finding | Action |
|---|---|---|---|
| A | (3.0.4: High) | 32 MiB combined report made 128 commands/file unreachable | **Already fixed upstream (3.0.5, streamed archives).** Verified: 128-command file completes in 3.1.1 and 3.1.2. Regression test added. |
| B | Medium | `q.folder` silently skipped `.md`, `.qasm2`, `.circuit` and other files | **Fixed.** Skipped files are listed in `skippedFiles`/`skippedFileCount`, and the no-supported-files error names one. `.qasm2` and `.circuit` are now accepted, since the parser already supported them. `.md` stays unsupported (no documented Markdown command format) and is now reported. |
| C | Low | Empty or prose-only `.txt` files reported "Text circuit requires qubits N…" | **Fixed.** New messages: "Text input is empty…" and "Text input line N is neither a q.* command nor a known gate ('word')…". Genuine text circuits are unchanged. |
| D | Medium (disk/perf) | Every `q.folder status` archived a full session snapshot: 155 KB average, 944 KB maximum per poll in 3.1.1 (about 1.8 MB in 3.0.4) | **Fixed.** `q.folder status`/`q.sequence status` archives replace only the repeated full snapshot with an `archiveCompacted` note (3.7 KB measured). The live response is unchanged, and `q.status` still archives everything. |
| E | Info | qc-198 / `q.compile synthesize` unavailable | **Deferred (by design).** Needs a number-theoretic Clifford+T synthesizer (for example gridsynth) with independent error verification. SpinDoctor refuses rather than fabricating. Documented in HANDGUIDE. |
| F | Low | Shipped test `native_verification_preview_regressions` fails on 3.1.1: it expects `receiptReady=false` for a plain sequence, but the 3.1.x owner-bound receipt policy marks plain sequences `receiptReady=true, receiptKind=unverified`. Forged client fields are still discarded. | **Fixed (test drift).** The assertion accepts the documented `unverified` receipt kind and still requires `verificationRequired=false` and phase ≠ passed. |
| G | Low | `q.folder help` did not state supported extensions, limits, or the first-failure rule | **Fixed.** |
| H | Medium (disk/perf) | Per-command sequence reports embed the job snapshot, so long folder runs grow roughly quadratically. The full chug series wrote about 1.5 GB (`quantum-command-reports` 706 MB, `sequence-reports` 835 MB, largest file 57.8 MB). | **Deferred.** Receipts, PDF hydration and archive identity checks depend on the current archive shape, so this needs a designed format change. Documented in HANDGUIDE troubleshooting. |
| I | Low | Folder runs default to `verify=true`, which requires a working OpenCL GPU. Without one, success receipts are withheld. | **Documented** (HANDGUIDE section 6/10). No code change; this is the intended safety gate. |
| J | Info | Inputs under OneDrive: online-only placeholders may be refused as reparse points | **Documented.** The Desktop test folders on this PC were fully local (no reparse attributes). |
| K | Info | Release tooling and test strings were pinned to 3.1.1 | **Updated** to 3.1.2 (`release.py` STEM/VERSION, test header expectations, example filenames). |

### Code review notes (no new defects found)
- **Path handling:** folder paths must match `^[A-Za-z]:[\\/]` and UNC is refused. Reparse points are refused on every ancestor directory and on each file. The opened handle's final path is re-validated. Files are opened `FileShare.Read`, and the 4 MiB bound is checked before allocation. Enumeration is non-recursive.
- **Limits:** 128 files, 4 MiB/file, 128 commands/file, 4,096 chars/command, strict UTF-8 (throwing decoder). JSON MaxJsonLength is 32 MiB with recursion limit 100. PDF export is bounded to 512 pages, 4 MiB text and 32 MiB output. PDF record resolution is bounded to 64 MiB and 5,000 records.
- **PDF input parser:** unchanged since 3.0.4 (bounded objects, decoded-bytes budget, regex timeouts, no OCR or guessing).
- **Receipts:** client-supplied verification and receipt fields are stripped and replaced from native archives, which is confirmed by the fixed test above.
- **Minor:** folder enumeration walks every directory entry even when most are skipped. It is harmless at normal sizes, and the skipped-name list is capped at 64.

## 4. Version bump
3.1.2 is now set everywhere it appears:
- `TerminalSession.ProductVersion`, `VERSION.txt`, `MODEL.json`, `meta/ENTRYPOINT.json` (parent 3.1.1-candidate), the preset catalogue release, UI mirrors (embedded preset hash re-bound by `release.py seal`).
- `BOOT.cmd` carrier name, `cartridge/SpinDoctor-3.1.2.tiff|gif` (rebuilt and round-trip verified, with the cover footer updated to 3.1.2), README/START_HERE/notes headers.
- New `SpinDoctor-3.1.2-Release-Notes.txt`, `CHANGELOG.md` and `HANDGUIDE.md`/`.pdf`.

## 5. Evidence files (validation/3.1.2/)
Includes:
- `suite_3.1.1.json` and `suite_3.1.2.json`
- `presets311_run_log.jsonl`, `presets312_run_log.jsonl` and `preset_compare_311_312.csv`
- `chug311.json`, `chug312.json` and their check outputs, `EXPECTED_3.1.2.csv`, and the harness scripts
- `handguide-examples.json` and `seal312.json`
- carrier build proofs (see `validation/build-validation.json` and `artwork-validation.json`)

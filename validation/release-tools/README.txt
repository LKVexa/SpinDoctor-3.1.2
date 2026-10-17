SPINDOCTOR 3.1.0 CANDIDATE RELEASE TOOLS

3.0.9 focused checks (fresh isolated paths required):
  tests/preset_shot_regressions.ps1 -SourceRoot <source/mvp> -OutputRoot <scratch/presets>
    Validates all 241 preset identities, actual defaults/ranges, collapse and
    resource-estimate semantics without running full preset workloads.
  tests/native_spin_preview_regressions.ps1 -SourceRoot <source/mvp> -OutputRoot <scratch/native>
    Runs small native spin/restore checks and actual CPU/OpenCL GPU verification.
    Requires a working OpenCL device; does not substitute a mock GPU result.
    Add -ReplayPdf -PdfOutputRoot <fresh-scratch/pdf> to replay the captured
    immutable five-record example without new calculations or shot draws.
  tests/detailed_report_regressions.ps1 and tests/ui_regressions.js also check
    circuit source labels, zero/north/depth figures and preset shot controls.
The stress/full-presets workload remains skipped at the user's request.

Run source tests in separate scratch folders. Never run a test against the live
user workspace. All production C# files compile under Windows PowerShell 5.1.
The primary terminal has no separately shipped executable: BOOT compiles its
verified generic reader and materializes the carrier-contained source.

After all source edits are finished:
  python -B release.py seal --tree <source-tree> --baseline-immutable <original-source/integrity/immutable.json> --report <scratch/seal.json>
  powershell -NoProfile -ExecutionPolicy Bypass -File build_carrier.ps1 -BaseDir <original-SpinDoctor-runtime> -Tree <source-tree> -OutDir <new-cartridge-directory>
  python -B release.py assemble --tree <source-tree> --baseline-app <original-SpinDoctor-runtime> --carriers <new-cartridge-directory> --release <new-release-directory>
  python -B release.py package --release <new-release-directory> --archive <new-output.zip> --scratch <work-directory> --report <separate-package-proof.json>

Every carrier round-trip, cover/prefix preservation, checkpoint header check,
and reader verification is mandatory. Failed assertions throw instead of merely
returning a false report field. Build requires new output carrier paths.

Source test commands (all paths explicit):
  powershell -NoProfile -ExecutionPolicy Bypass -File tests/engine_regressions.ps1 -SourceRoot <source/mvp> -TestRoot <scratch/engine>
  powershell -NoProfile -ExecutionPolicy Bypass -File tests/transport_tests.ps1 -SourceRoot <source/mvp> -RunRoot <scratch/transport>
  powershell -NoProfile -ExecutionPolicy Bypass -File tests/carrier_harness_tests.ps1 -Harness <source/mvp/bootstrap/TIFFGifHoloHarness.cs> -Carrier <new.tiff> -Scratch <scratch/carrier>
  powershell -NoProfile -ExecutionPolicy Bypass -File tests/verify_carriers.ps1 -Harness <source/mvp/bootstrap/TIFFGifHoloHarness.cs> -Tiff <new.tiff> -Gif <new.gif> -Smoke
  powershell -NoProfile -ExecutionPolicy Bypass -File materialize_test.ps1 -Tree <source-tree> -Carrier <new.tiff-or-gif> -Scratch <new-scratch-directory>
  node tests/carrier_browser_tests.js <source/mvp>
  powershell -NoProfile -ExecutionPolicy Bypass -File tests/detailed_report_regressions.ps1 -SourceRoot <source/mvp> -TestRoot <scratch/report-tests> -PdfChecks

The detail suite compiles all production C# sources and checks the readable TXT
layer, unchanged native JSON, zero/underflow/null distinctions, accepted versus
completed jobs, UTF-8 archive byte margins, PDF figures, raw previews, legacy PDF
view aliases and refusal of oversized PDFs. -PdfChecks keeps PDF bytes in memory.
To save two explicitly synthetic visual fixtures, use -GeneratePdfFixtures and
-PdfOutputDirectory <fresh-scratch-directory> instead; it requires a new directory.

Readable report interpretation is bounded to 8192 characters. Exact native TXT
and JSON remain the evidence; optional explanations yield to the existing 32 MiB
UTF-8 command archive limit. PDF exports are limited to 512 pages, 4 MiB printable
text and 32 MiB output. Detailed PDFs use a bounded field guide; Complete PDFs
retain every supplied field within these limits, with reversible Unicode JSON
escapes and whitespace wrapping for print. Imported preview fields are explicitly
identified; unreturned arrays, validation, counters and job completion are not
invented. Large exports should use complete TXT/JSON or smaller command batches.

Optional Python tests require numpy==2.3.5. Set PYTHONDONTWRITEBYTECODE=1 and
SPINDOCTOR_NATIVE_ROOT=<source/mvp/native> for tests/carrier_native_http_tests.py.
Copy native sources to scratch before running test_cpu_gpu.py or
test_shared_epoch_xpu.py because they write validation files beside their source.
test_probability_sheet.py accepts --report <scratch/probability.json>; --live
adds actual small RAM/OpenCL checks. Actual OpenCL GPU tests require a suitable
installed GPU driver and never silently substitute CPU calculations.

External RUN_SPINDOCTOR.cmd and RUN_QUANTUM_XPU.cmd shortcuts intentionally
delegate BOOT.cmd. The embedded launchers compile from the verified projection.
Runtime workspace files, live checkpoints, user reports, and Python caches are
excluded from this clean package. SHA256SUMS and each ZIP file are verified.

materialize_test.ps1 verifies every extracted immutable source hash and then
materializes again to prove unlisted mutable session/report sentinels survive.
It does not launch the user interface or open a native saved session.

3.0.6 additions (always supply explicit source/input/scratch paths):
  tests/newfolder_verification.ps1: deterministic gate tests with mock responses;
    does not claim genuine GPU work. -Quick skips only the 128-file size test.
  tests/manual_receipt_guard_regressions.ps1: native receipt guards and imported
    PDF provenance; missing-GPU failure is intentional and must withhold success.
  tests/native_verification_preview_regressions.ps1 -RealGpu: mandatory genuine
    GPU sample comparisons, numerical preview identity, restoration and tampering.
  tests/model_sampling_folder.ps1 -InputRoot <release/Model-Sampling-Test-Inputs>:
    actual CPU/OpenCL GPU three-file test; all4096 addresses checked per file.
  tests/full_folder_integration.ps1 -InputRoot <release/Test-Inputs>
    -SpecPath <tests/fixture-spec.json>: native authenticated loopback HTTP,
    24 files/172 commands, automatic verification and large streamed TXT export.
  tests/command_receipt_regressions.ps1: synchronous/native completion stamps,
    immutable response preservation and owner-bound receipt policy.
  node tests/ui_regressions.js <source/mvp>: deterministic browser-script checks.

Set -SourceRoot <source/mvp> and -TestRoot <new-scratch-folder> for these tests.
Transport/full HTTP tests need local loopback access. Failed checks throw.
Tests never alter the delivered application workspace or original input folders.
3.0.7 candidate: numerical previews preserve bounded calculator results independently of large status snapshots.
The native large stress test remains skipped at the user's request. Historical harnesses are retained for optional use.

  tests/native_report_example.ps1 -SourceRoot <source/mvp> -OutputDirectory <new-scratch-folder>
    Generates the actual calculator +Z/-Z/+X and genuine CPU/OpenCL GPU sampled
    model example PDF from immutable native archives in a new isolated session.
    This is a small report example, not the full raised-ceiling workload.

3.1.0 adds direct-preset queue/completion tests, 1,024 catalog defaults and the
read-only historical QFT definition-report audit. Stress tests are skipped.
Artwork output is renamed to SpinDoctor-3.1.0.tiff/.gif after the verified
cover update; final hashes and runtime bootstrap filenames must match.

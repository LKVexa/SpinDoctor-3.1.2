SPINDOCTOR 3.1.0 CANDIDATE RELEASE TESTS

See ../README.txt for the full current test inventory and tested scopes.

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

3.1.0 adds direct-preset queue/completion tests, 1,024 catalog defaults and the
read-only historical QFT definition-report audit. Stress tests are skipped.
Artwork output is renamed to SpinDoctor-3.1.0.tiff/.gif after the verified
cover update; final hashes and runtime bootstrap filenames must match.

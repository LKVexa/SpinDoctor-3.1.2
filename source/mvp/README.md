SpinDoctor 3.1.2
See SpinDoctor-3.1.2-Release-Notes.txt for the 3.1.2 audit fixes: folder runs list skipped files, accept .qasm2/.circuit, explain empty or prose .txt inputs, and keep q.folder/q.sequence status polling reports compact.
See HANDGUIDE.md (or HANDGUIDE.pdf in the release folder) for a plain-language user guide.
See SpinDoctor-3.1.1-Release-Notes.txt for CSV report companions, measurement fitting and CSV gate-list simulation.
See SpinDoctor-3.1.1-CSV-Input-Guide.txt for selecting measurement columns, fitting equations and importing CSV circuits.
Earlier version notes below are preserved as history.

SpinDoctor 3.1.0 candidate
See SpinDoctor-3.1.0-Release-Notes.txt for circuit definition and report accounting clarity.

SpinDoctor 3.0.9 candidate
See SpinDoctor-3.0.9-Release-Notes.txt for circuit-state PDF figures and preset defaults.

SpinDoctor 3.0.8 candidate
See SpinDoctor-3.0.8-Release-Notes.txt for the PDF pipeline update.

SpinDoctor 3.0.7 candidate
See SpinDoctor-3.0.7-Release-Notes.txt for this update.

## Historical SpinDoctor 3.0.6 workflow

Run `BOOT.cmd` from the release SpinDoctor folder. Folder runs now automatically sample the current procedural model with `q.verify` before publishing success receipts or purging temporary outputs. The samples show predicted amplitudes, spin and Born probabilities, with actual CPU/OpenCL GPU comparisons. Read `SpinDoctor-3.0.6-Release-Notes.txt` for scope, failure handling and validation. Earlier notes below are historical.

# SpinDoctor current terminal

BOOT.cmd launches the contained RUN_SPINDOCTOR.cmd. See SPINDOCTOR-NOTES.txt for return selection, streaming verification and selected coherent 29-qubit registers. Earlier documents below retain historical experiments and accounting.

# Current quantum product-state terminal

BOOT.cmd now launches the image-contained RUN_QUANTUM_XPU.cmd. The 500-trillion-address single-qubit model, terminal, C# CPU/OpenCL engine and verification rules are stored inside both images. See QUANTUM-NOTES.txt. The following documents retain earlier experiments and accounting.

# Current 2-adic probability-sheet experiment

Run RUN_SHEET_XPU.cmd, complete the native XOR benchmark, then reconstruct the probability sheet. The field and independent sampling seeds persist across launches. Actual CPU/GPU samples, represented domain updates, probability estimates and exact known-equation queries have separate accounting. See START_HERE.txt and SHEET-NOTES.txt for the complete compact returns and measured limits.

The earlier experiment documentation follows.

# Current black/white XOR experiment

Run `RUN_XOR_XPU.cmd` (or its `RUN_XPU.cmd` alias) from this materialized `mvp`
folder. It opens `TENQ_AT_1MHZ_DASHBOARD.html` through the native CPU and OpenCL
GPU bridge. The random seed is recorded on disk and retained across runs, launches, reset, pause/resume and
sampling; use **NEW RANDOM SEED** to change it explicitly.

Black is 0 and white is 1. Equal inputs XOR to black; different inputs XOR to
white. The current rate counts represented XORs per acknowledged shared epoch
and divides by real measured time. Selected CPU/GPU XORs and initial RAM writes
are counted separately. These are bitwise operations, not floating-point FLOPs.

Use `RUN_RAM_WAVE_REFERENCE.cmd` for `CPU_GPU_RAM_WAVE.html`, the previous
physical fill experiment. The remaining document below preserves the earlier
image-carrier and benchmark reference instructions and claims. Its modeled
FLOP metrics do not describe the current XOR experiment. See `START_HERE.txt`
and the archive-root `README-XOR-XPU.txt` for the current experiment details.

---
# Lunar127 Showcase 1.1.4 — Run Simulation session control

Release 1.1.4 adds a carrier-resident **Run Simulation** lifecycle to the 10Q@1M dashboard.

Each randomized button press creates one fresh 256-bit launch seed using the browser cryptographic RNG plus time/counter/GUID material, then derives:

`sessionSeed = SHA256(launchSeed || carrierProgramBinding || BRWWORD_SHA256)`

The active session resets the raster field, challenge context, session checkpoint chain, run counter state and displayed session proofs exactly once per press. View-only controls (fit, zoom, mode, slice and render) never rotate the seed. **Copy Seed**, **Replay Seed**, a 64-hex **Custom Seed** field, and **Randomize Seed On Run: ON/OFF** are included. Replaying the same launch seed reproduces the same session seed and raster fingerprint.

The `carrierProgramBinding` is `4ef0a6992dfdbe039a7cb6aa4b67b5de98bd3798f1fb4563ca0e6342c32e86b6`. It is a non-self-referential SHA-256 digest over run-critical image-resident payloads; the outer TIFF-GIF-HOLO/4 stream itself is still verified by the generic harness before materialization. See `meta/RUN_SESSION_BINDING.json` and `TENQ_RUN_SESSION_SPEC.md`.

The physical host benchmark remains a separate launch-time measurement. Dashboard run sessions rotate deterministic represented-compute/raster state without falsely claiming that each browser button press re-measures physical laptop FLOPS.

---

## Historical lineage preserved below

# Lunar127 Showcase 1.1.3 — 10Q@1M UltraWide represented-compute MVP

The current primary entry point is `RUN_10Q_AT_1M_MVP.cmd`. The complete UltraWide Appliance repository and its original source ZIP are stored inside the TIFF/GIF carrier and verified before benchmarking. The 1,048,576-bit UltraWide word is then verified and serves as the resident macroprogram anchor for a compiled 256-bit sequential hot-state kernel.

At the 1 MHz gate the queryable domain is `10^25 FLOP-eq/epoch`, yielding `10^31 FLOP-eq/s = 10 quettaFLOP/s-equivalent represented compute`. This is explicitly **not** a claim of physically executing `10^31` laptop FLOPs each second. The dashboard reports measured host timing, a separate scalar floating-point diagnostic, algorithmic byte accounting, randomized post-run proof challenges, and the `25T × 40T × 10T = 10^40` virtual raster cube.

See `TENQ_10Q_AT_1M_SPEC.md` and `TENQ_VALIDATION.md`.

---

## Historical lineage preserved below

# Lunar127 Showcase 1.1.0 — 10Q@1M UltraWide represented-compute MVP

The current primary entry point is `RUN_10Q_AT_1M_MVP.cmd`. The 1,048,576-bit UltraWide word is verified once at boot and then serves as the resident macroprogram anchor for a compiled 256-bit sequential hot-state kernel.

At the 1 MHz gate the queryable domain is `10^25 FLOP-eq/epoch`, yielding `10^31 FLOP-eq/s = 10 quettaFLOP/s-equivalent represented compute`. This is explicitly **not** a claim of physically executing `10^31` laptop FLOPs each second. The dashboard reports measured host timing, a separate scalar floating-point diagnostic, algorithmic byte accounting, randomized post-run proof challenges, and the `25T × 40T × 10T = 10^40` virtual raster cube.

See `TENQ_10Q_AT_1M_SPEC.md` and `TENQ_VALIDATION.md`.

---

## Historical lineage preserved below

# Lunar127 Showcase 0.3.1 — TIFF-GIF Processable Hologram

This corrective build replaces the prior static-looking TIFF carrier with a **temporal processable image cartridge**.

## What “TIFF-GIF” means in this build

TIFF and GIF are different standard image containers. Lunar127 therefore uses one logical processable-hologram format with **two lossless, interchangeable encodings**:

- `cartridge/Lunar127_Showcase_0.3.1_PROCESSABLE.tiff` — an ordered multi-page TIFF.
- `cartridge/Lunar127_Showcase_0.3.1_PROCESSABLE.gif` — an actual animated GIF89a frame stream.

The GIF visibly spools when opened in an ordinary animated-image viewer. The TIFF contains the same ordered logical frame stream as pages. They decode to the same image-resident archive and machine.

## Authoritative processing cycle

Every VM quantum is intended to use the active image itself as the authoritative machine:

`DECODE FRAMES -> PHASE UNWRAP -> VERIFY FRAME CHAIN/HASHES -> REASSEMBLE IMAGE ARCHIVE -> VERIFY IMMUTABLE BANKS -> EXECUTE IMAGE-RESIDENT BYTECODE -> UPDATE COMPLETE STATE -> JOURNAL -> REPACK -> PHASE-TEXTURE FRAMES -> ENCODE TIFF/GIF -> REREAD/VERIFY -> ATOMIC COMMIT`

The data frames are not screenshots. Each 512×512 frame is 8-bit processable image memory. A reversible coordinate/frame/tick-dependent phase texture is applied to the payload before it is represented as pixels. The generic harness reverses the texture and validates the page payload and ordered page chain before execution.

## External boundary

Only these pieces are outside the processable image:

- `harness/TIFFGifHoloHarness.cs` — generic C# TIFF/GIF evaluator, VM interpreter and virtual-console primitives.
- `BOOT.cmd` and the build/verify/inspect/reset command wrappers.
- Windows/.NET Framework plus ordinary host graphics, audio, input, loopback and filesystem primitives.
- A writable `workspace` for the current image and explicit checkpoint copies.

All Lunar127-specific VM program, mission logic, physics data, scene graph, HD assets, materials, HUD definition, terminal/VFS, state, trajectory, journal, checkpoint metadata, integrity maps and temporal-spool definition are image-resident.

## Run

Double-click:

    BOOT.cmd

That defaults to the animated GIF as the active authoritative carrier. To use the multi-page TIFF encoding instead:

    BOOT.cmd tiff

To start again from the seed image:

    BOOT.cmd gif reset
    BOOT.cmd tiff reset

The C# harness is rebuilt locally from the supplied Hotfix 4 source on every boot using the Windows .NET Framework C# compiler. This prevents an older previously compiled evaluator from being reused after an overlay update.

## Important engineering boundary

This is an image-resident virtual machine design, not a claim that TIFF/GIF pixels physically execute without a CPU. The external C# evaluator is the declared generic platform boundary. The simulation-specific machine is carried by the image.

## Hotfix 3: persistent Windows shell

This package intentionally keeps `BOOT.cmd` open while the evaluator GUI is running and after it exits. Startup and runtime exceptions are recorded in `workspace\logs\runtime.log` instead of disappearing with a transient shell window.

If a machine blocks the optional 127.0.0.1 loopback listener, run `BOOT_DIRECT.cmd reset`. The on-screen terminal remains functional because direct console input does not require the loopback transport.

For a build-and-carrier check that does not start the GUI, run `DIAGNOSE.cmd`. It also stays open on failure.


## Hotfix 3 compiler correction

Hotfix 3 corrects Windows C# compiler error CS0160 in `SimulationForm.SafeBegin`. `ObjectDisposedException` derives from `InvalidOperationException`, so the specific catch must precede the broader catch. The build now catches `ObjectDisposedException` first and then `InvalidOperationException`. `BUILD_HARNESS.cmd` also persists all C# compiler output to `workspace\logs\build.log` and echoes it on failure. The persistent boot shell remains enabled.

## Hotfix 4: crash-safe Windows boot

Hotfix 4 hardens the stage after a successful compile, where the evaluator could disappear before a usable virtual console was established. The GUI harness is now compiled as a console-attached managed executable, direct mode is the safe default, the image quantum scheduler starts only after the WinForms window is visible, and asynchronous loopback client failures are contained instead of being able to terminate the process. Image-resident PNG decode failures are degraded to logged placeholders rather than aborting startup.

Use:

    BOOT.cmd reset

For an explicit loopback-enabled run:

    BOOT.cmd loopback reset

Run `DIAGNOSE.cmd` first if desired. It verifies both carriers and performs a semantic runtime/asset smoke load without starting the full GUI. The build log is `workspace\logs\build.log`; the runtime log is `workspace\logs\runtime.log`.

## Performance Dashboard Overlay 0.4.0

This package adds a non-destructive performance instrumentation overlay around the unchanged 0.3.1 Hotfix 4 processable cartridge.

- Open `OPEN_PERFORMANCE_DASHBOARD.cmd` for the attached-carrier reference view.
- Run `BENCHMARK_DASHBOARD.cmd` to rebuild the evaluator, measure its own verify/smoke paths on this Windows machine, refresh dashboard metrics, and open the dashboard.
- Read `benchmark/WALKTHROUGH.md` for the 1 MHz-through-1 GHz modeled throttle walkthrough and the measurement boundary.

The MHz figures are modeled page-spool targets derived from the actual 512×512×8-bit carrier geometry. Machine-local evaluator results are displayed separately and are not promoted to optical/MHz measurements.

## Holographic Availability Lab 0.5.0

The primary research benchmark is now `HOLOGRAPHIC_AVAILABILITY_DASHBOARD.html`.

Run `RUN_1MHZ_HOLOGRAPHIC_AVAILABILITY_TEST.cmd` for the browser-based AVK-1 test, or `RUN_FULL_RESEARCH_DEMO.cmd` to run the existing carrier/evaluator verification first and then the availability benchmark.

The 0.5.0 test asks whether a real Lunar127 carrier page can anchor a dependency-chained virtual state machine that executes at least 1,000,000 epochs/s while keeping the complete 512×512×8-bit state directly reconstructable. Fixed reference hashes for selective 4 KiB ranges and full 262,144-byte checkpoints prevent the dashboard from validating itself by assertion alone.

At exactly 1 MHz the full-state equivalent availability is 262.144 GB/s and the evolving payload equivalent is 261.888 GB/s. Those values describe deterministically available state, not physical laptop I/O or new entropy.


## Virtual Command Throughput Benchmark 0.6.0

The second primary research axis is **virtual command capacity**. Run `RUN_VIRTUAL_COMMAND_THROUGHPUT_TEST.cmd` to measure how many VCK-1 commands the current computer can synthesize, decode, execute, dependency-commit, and retire per second.

Headline metrics include:

- retired virtual commands/s and Mcommands/s;
- VCK-1 commands available per 1 MHz hologram epoch;
- nominal epoch capacity at 64 commands/epoch;
- represented full-state equivalent rate at that nominal epoch capacity;
- carrier reads, scratch commits, opcode mix, and fixed 1,000,000-command conformance proof.

Use `RUN_DUAL_RESEARCH_BENCHMARK.cmd` to run AVK-1 availability and VCK-1 command throughput as complementary tests. Virtual commands are VCK-1 VM operations, not native CPU instructions.


## 0.7.0 — 17T@1M MVP

Run `RUN_17T_AT_1M_MVP.cmd` to test the moonshot gate, or `RUN_FULL_MOONSHOT_DEMO.cmd` for original-carrier verification + VCK-1 command throughput + 17T@1M. See `moonshot/MOONSHOT_SPEC.md`.


## QFLOP@1MHz Virtual Raster MVP 0.8.0

Run `RUN_QFLOP_RASTER_MVP.cmd` for the QCK-1/VBT-1 experiment. It tests a 1-QFLOP/s-equivalent represented compute program at a measured 1 MHz sequential clock and drives a 25B × 40B virtual raster that physically materializes only the current viewport. See `qflop/QFLOP_RASTER_SPEC.md`.

## EXA@1M Raster Cube MVP 0.9.0

Run `RUN_EXAFLOP_RASTER_CUBE_MVP.cmd`.

The new ECK-1/VRC-1 experiment raises the represented compute envelope to **1 EFLOP/s-equivalent at a measured 1 MHz sequential hologram clock** and expands the virtual image into a **25B × 40B × 1,000** raster cube.

The cube contains `10^24` addressable voxels. A flat RGBA8 materialization would be 4 YB, so the console never allocates the whole object. It reconstructs exact voxels/slices and ray-samples finite viewports. The full-screen viewport has a real ≤2-second first-fill gate.

See `exa/EXAFLOP_RASTER_CUBE_SPEC.md` for the measurement boundary and proof requirements.

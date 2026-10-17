# Lunar127 1.1.4 — Run Simulation / seed-rotation release

This release adds the dashboard-level run lifecycle requested for the 10Q@1M hologram-contained MVP.

- **Run Simulation** rotates the launch seed once per button press.
- A 256-bit session seed is derived from launch seed + carrier program binding + verified BRWWORD SHA-256.
- Raster generation uses the session seed and therefore changes between fresh runs.
- Replay/custom seed paths derive the identical session seed and raster for the same launch seed.
- The active dashboard session resets its 16-link checkpoint context, 64 lane challenges, 64 voxel challenges, run state, proof display and raster viewport.
- Fit/zoom/mode/slice/render controls do not rotate the seed.
- Host timing evidence is preserved separately; browser run-session controls do not masquerade as a new physical C# benchmark.
- All run-session logic, UI, derivation rules and binding metadata are stored inside the TIFF/GIF logical archive.

Carrier program binding: `4ef0a6992dfdbe039a7cb6aa4b67b5de98bd3798f1fb4563ca0e6342c32e86b6`

---


## Full UltraWide Appliance containment (1.1.3)

The TIFF/GIF logical archive now contains the **complete UltraWide Appliance**, not only the resident 1,048,576-bit BRWWORD. It embeds both the exact repository tree and the original source ZIP. A carrier-resident SHA-256 manifest covers every appliance file; TQK-1 validates the exact file set, total bytes, source ZIP byte count, and source ZIP digest before any timed epoch benchmark begins. External `BOOT.cmd` and generic harness source are launch-only copies and are also duplicated inside the carrier for provenance.

# Lunar127 1.1.3 — 10Q@1M UltraWide represented-compute MVP

This release moves UltraWide out of the per-epoch Python/interpreter path and turns the 1,048,576-bit word into a boot-verified resident macroprogram anchor.

The measured hot loop is a compact 256-bit compiled dependency chain. At the 1 MHz gate, each epoch exposes a deterministic queryable domain of `10^25 FLOP-eq`, yielding `10^31 FLOP-eq/s`, or **10 quettaFLOP/s-equivalent represented compute**.

The release does **not** claim a laptop physically executes `10^31` floating-point operations each second. Physical host timing and a separate scalar floating-point diagnostic are displayed beside the represented rate.

The raster experiment is expanded to `25T × 40T × 10T = 10^40` addressable voxels. The console materializes only challenged coordinates and finite viewport ray samples.

The main entry point is `RUN_10Q_AT_1M_MVP.cmd`, which is itself stored inside the TIFF/GIF carrier and is materialized only after carrier verification.


## 1.1.3 bootstrap and raster changes

- Removed the malformed `after_verify:` command path from `BOOT.cmd`; verification now flows directly into `%ERRORLEVEL%` capture.
- Added a fresh CSPRNG 256-bit raster seed on each launch.
- Added exact raster replay through `LUNAR127_RASTER_SEED=<64 hex characters>`.
- Bound raster voxel generation and measured checkpoint commitments to the launch seed while preserving fixed sequential preflight vectors.
- Kept the complete UltraWide Appliance, kernel, raster evaluator, dashboard, manifests, proof logic, and seed semantics inside both processable carriers.

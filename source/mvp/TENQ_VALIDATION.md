# Lunar127 1.1.4 — run-session validation addendum

## Packaging-time checks

- Run Simulation control present in `TENQ_AT_1MHZ_DASHBOARD.html`.
- Current Seed, Copy Seed, Replay Seed, Custom Seed and Randomize Seed On Run controls present.
- JavaScript parses successfully under Node.js.
- SHA-256 implementation matches the standard `abc` test vector.
- Kernel source derives `sessionSeed = SHA256(launchSeed || carrierProgramBinding || BRWWORD_SHA256)`.
- Kernel checkpoint commitments bind the session seed.
- Browser raster salt derivation uses the same session seed semantics.
- Same 64-hex launch seed deterministically reproduces the same session seed and procedural raster fingerprint.
- Distinct launch seeds produce distinct session seeds and raster fingerprints in the static backtest.
- Carrier binding is `4ef0a6992dfdbe039a7cb6aa4b67b5de98bd3798f1fb4563ca0e6342c32e86b6` and is described by `meta/RUN_SESSION_BINDING.json`.

## Runtime checks on Windows

After launching through `BOOT.cmd`, press **Run Simulation** twice and verify the Current Seed changes. Use **Replay Seed** (or a custom copied seed) and verify the displayed raster fingerprint returns to the prior value. Fit/zoom/mode/slice/render must leave Current Seed unchanged.

The measured 1 MHz host gate is still evaluated only by the image-resident C# kernel launched by `RUN_10Q_AT_1M_MVP.cmd`; browser run-session rotation does not re-label browser work as physical host FLOPS.

---


## Full UltraWide Appliance containment (1.1.3)

The TIFF/GIF logical archive now contains the **complete UltraWide Appliance**, not only the resident 1,048,576-bit BRWWORD. It embeds both the exact repository tree and the original source ZIP. A carrier-resident SHA-256 manifest covers every appliance file; TQK-1 validates the exact file set, total bytes, source ZIP byte count, and source ZIP digest before any timed epoch benchmark begins. External `BOOT.cmd` and generic harness source are launch-only copies and are also duplicated inside the carrier for provenance.

# Lunar127 1.1.3 — build-time validation record

This record distinguishes checks completed while packaging from host-specific gates that can only be measured when the Windows MVP is run.

## Completed during packaging

- UltraWide Appliance 0.2.0 compiled the canonical LCTL-BRIM/1 binding program.
- `resident_macroprogram.brw` is exactly 131,072 bytes / 1,048,576 bits.
- The upstream `brim_wide.verify_chain()` verifier accepted the one-word chain.
- The upstream `brim_wide.run_chain()` execution completed with `HALTED` status.
- Macroprogram-core SHA-256 is embedded in the resident word as four canonical little-endian u64 `MOVI` immediates.
- Independent Python generation produced fixed TQK-1 state/checkpoint vectors through 1,000,000 sequential epochs.
- JavaScript implementations of TQL-1 and TQV-1 reproduced every fixed Python lane and voxel reference vector.
- Dashboard JavaScript passed Node.js syntax checking.
- A native C probe using the same 256-bit transition algorithm completed 100,000,000 dependent epochs at approximately **149.2 MHz** in the packaging environment. This is a feasibility probe only; it is not the authoritative Windows C# result and excludes periodic SHA-256 checkpoints.

## Deferred to the target Windows host

The release dashboard only passes after the image-resident C# kernel measures on the user's machine:

- median sequential dependent transition rate across three rounds;
- periodic checkpoint overhead;
- separate dependent scalar FP64 diagnostic;
- post-run cryptographically randomized lane and voxel challenges;
- browser-side independent challenge verification;
- browser viewport first-fill latency.

A measured host clock below 1 MHz is a gate failure even if all structural and challenge checks pass.

## Measurement boundary

The native probe and target C# benchmark measure real host transition throughput. The `10^31 FLOP-eq/s` figure is the size of the deterministic addressable compute domain associated with a 1 MHz sequence of valid macro-transitions. It is intentionally labeled **represented/queryable** and is never reported as physically executed laptop FLOPS.


## Randomized-raster validation

A valid 1.1.3 run must expose a 64-hex-character launch seed, derive voxel salts from both the verified BRWWORD SHA-256 and that seed, pass at least 64 post-run randomized voxel challenges in the independent browser evaluator, and report that the measured checkpoint root binds the same seed. Two fresh ordinary launches are expected to produce different seeds/raster fields; replaying a recorded seed through `LUNAR127_RASTER_SEED` must reproduce the same voxel field for the same coordinates and epoch.

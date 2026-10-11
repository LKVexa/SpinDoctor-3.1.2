# Lunar127 1.1.4 — run-session audit addendum

Static review confirms that the run-session controls are image-resident, seed rotation is scoped to **Run Simulation**, replay is deterministic, view controls do not mutate the seed, the C# launch benchmark and browser session lifecycle are kept as separate measurement domains, and the carrier program binding is `4ef0a6992dfdbe039a7cb6aa4b67b5de98bd3798f1fb4563ca0e6342c32e86b6`.

The final TIFF/GIF container hashes are intentionally not embedded into their own payloads; the generic harness verifies the outer carrier stream before materialization, while `meta/RUN_SESSION_BINDING.json` supplies a non-self-referential binding digest for run-critical payloads.

---

# Lunar127 1.1.0 — 10Q@1M end-to-end audit and backtest

## Verdict

**STATIC / LOGICAL CARTRIDGE: PASS**  
**TARGET WINDOWS 1 MHz RELEASE GATE: DEFERRED TO RUNTIME MEASUREMENT**

The package implements the intended architecture: the million-bit UltraWide artifact is a boot-verified resident macroprogram anchor, while the per-epoch hot path is a compiled 256-bit sequential dependency transition. The release does not route the 131,072-byte word through Python BRIM, serialization, SHA-256, or million-bit arithmetic every epoch.

## Containment audit

- External bootstrap boundary: `BOOT.cmd` and `harness/TIFFGifHoloHarness.cs` only.
- Processable carriers: one multi-page TIFF and one animated GIF.
- Image-resident entry point: `mvp/RUN_10Q_AT_1M_MVP.cmd`.
- Image-resident runtime: `mvp/runtime/TenQKernel.cs`.
- Image-resident UltraWide word: `mvp/tenq/resident_macroprogram.brw`.
- Image-resident macroprogram descriptor, binding source/image/BRIR, reference vectors, proof rules, dashboard, specifications, provenance and license notices: present.
- Runtime benchmark outputs modify only the temporary materialized projection, never the authoritative carrier.

## Carrier equivalence

Both TIFF and GIF were independently decoded after packaging. They reconstruct the same logical THARCH3 archive with the same entry names, banks and bytes. The immutable map covers every `mvp/` entry and all covered hashes matched.

Container file hashes are intentionally not embedded inside the carrier because that would be self-referential. A post-build receipt is maintained outside the authoritative image during packaging.

## UltraWide verification

- Word length: **131,072 bytes**.
- Word width: **1,048,576 bits**.
- Format: `BRWWORD/1`.
- Resident word SHA-256: `8222fd2c7b321703b9e6f44fa22a7a7cb069aa2d7cd311fb00f6c2eeb1549b83`.
- Macroprogram core SHA-256: `12c8ff0c84018a2365b73b54d6449249fd8d5bde6733ac264203db7c852cd9bb`.
- Upstream UltraWide `verify_chain`: PASS.
- Upstream UltraWide `run_chain`: `HALTED` after 8 instructions.
- Upstream BRIM and BRWWORD self-tests: PASS.

The core descriptor digest is encoded into the canonical LCTL-BRIM/1 source as four little-endian u64 immediates. TQK-1 reconstructs that source from the current descriptor hash and requires byte-for-byte equality with the source stored in the word.

## Sequential-state backtest

Independent Python reference generation exercised TQK-1 through 1,000,000 sequential dependent epochs and produced fixed state/checkpoint vectors. The target C# kernel replays that fixed vector before the measured benchmark; mismatch is fatal.

A native C feasibility probe implementing the same 256-bit transition algorithm completed 100,000,000 dependent epochs at approximately **149.2 MHz** in the packaging environment. This probe is not the authoritative Windows result and excludes periodic SHA-256 checkpoint work; it exists only to confirm that the compact transition itself has ample headroom relative to a 1 MHz target.

## Challenge backtest

The TQL-1 lane and TQV-1 voxel algorithms were independently implemented in Python and JavaScript. All fixed cross-language vectors matched.

At target runtime, 64 compute-lane and 64 3-D voxel challenge addresses are selected only after the benchmark using the host cryptographic RNG. C# materializes those answers and the browser independently recomputes them from the addresses and resident-word digest. Any mismatch fails the dashboard gate.

## Raster-cube audit

- Width: `25,000,000,000,000`.
- Height: `40,000,000,000,000`.
- Depth: `10,000,000,000,000`.
- Addressable voxels: `10^40`.
- Flat RGBA8 materialization: `4 × 10^40` bytes.

No full materialization is attempted. The dashboard evaluates only finite viewport rays/slices and exact challenged coordinates. The first-fill runtime gate is ≤2 seconds.

## Measurement boundary audit

The package consistently distinguishes:

- **represented/queryable compute**: `10^25 FLOP-eq/epoch`, therefore `10^31 FLOP-eq/s` at 1 MHz;
- **measured host transition rate**: actual sequential TQK-1 epochs/s;
- **measured scalar floating-point diagnostic**: a separate dependent FP64 benchmark, not peak hardware FLOPS;
- **algorithmic state-byte accounting**: 64 bytes read+write per epoch by model, explicitly not claimed as measured DRAM-bus traffic.

Accordingly, the dashboard may display **10 quettaFLOP/s-equivalent represented compute** only with the `-eq` / represented boundary visible. It does not claim physically executed 10 quettaFLOP/s on the laptop.

## Remaining target-host test

The authoritative release PASS can only be issued after Windows runs the image-resident TQK-1 kernel and all dashboard gates pass simultaneously, especially the **≥1.000000 MHz measured sequential clock** and **≤2 s viewport first-fill** gates.


## 1.1.3 corrective findings

The 1.1.2 Windows log showed that TIFF-GIF-HOLO/4 verification itself passed (`pages=42`, `entries=193`) and the failure occurred afterward because `BOOT.cmd` contained the bare token `after_verify:` rather than a valid `:after_verify` label. 1.1.3 removes that label entirely. The release also implements per-launch seeded raster randomization without moving the UltraWide Appliance or raster evaluator outside the processable carrier.

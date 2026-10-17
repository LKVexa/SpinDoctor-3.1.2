# Lunar127 0.7.0 — 17T@1M MVP

This release adds the first runnable **17T@1M** moonshot benchmark on top of the 0.6.0 Holographic Command Lab.

## Target

- verified sequential hologram clock: **≥1.000 MHz**;
- exact deterministic compute envelope: **17,000,000 FLOP-equivalent operations/epoch**;
- represented compute at 1 MHz: **17.000 TFLOP/s-equivalent**;
- represented full-state evolution at 1 MHz: **262.144 GB/s**;
- addressable VCK-2 vector program: **2,076 commands/epoch = 2.076 billion represented vector commands/s at 1 MHz**.

## New files

- `17T_AT_1M_DASHBOARD.html`
- `RUN_17T_AT_1M_MVP.cmd`
- `RUN_FULL_MOONSHOT_DEMO.cmd`
- `moonshot/MOONSHOT_SPEC.md`
- `moonshot/compute_program.json`
- `moonshot/reference_vectors.json`
- `moonshot/verify_reference_vectors.py`
- `MVP_VALIDATION.md`

## Measurement boundary

`17 TFLOP/s-eq` is a represented/reconstructable compute-availability metric for VCK-2. The dashboard separately reports the physically executed full-checkpoint materialization rate in GFLOP-eq/s. It never relabels represented compute as measured hardware TFLOPS.

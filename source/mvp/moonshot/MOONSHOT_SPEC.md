# Lunar127 17T@1M MVP Specification

Release: **0.7.0**  
Schema: `LUNAR127/17T-AT-1M/1`

## Moonshot

**17 TFLOP/s-equivalent represented deterministic compute availability at a verified 1 MHz processable-hologram clock.**

This MVP deliberately distinguishes represented/available compute from conventional physically executed hardware TFLOPS.

## Exact compute envelope

Each hologram epoch exposes an addressable VCK-2 vector program containing:

- **2,075 × VFMA-4096** = 8,499,200 FMA lanes.
- **1 × VFMA-800 tail** = 800 FMA lanes.
- Total: **8,500,000 FMA lanes/epoch**.
- Each virtual lane is defined as one multiply plus one add = **2 FLOP-equivalent operations**.
- Exact total: **17,000,000 FLOP-equivalent operations/epoch**.
- Program size: **2,076 addressable vector commands/epoch**.

At 1,000,000 epochs/s:

- represented compute = **17,000,000,000,000 FLOP-eq/s = 17.000 TFLOP/s-eq**;
- represented vector-command availability = **2.076 billion commands/s**;
- represented full-state evolution = **262.144 GB/s**.

## Virtual FP32 semantics

For global FMA lane `g` and epoch `e`, three carrier addresses are deterministically derived inside the 261,888-byte payload. Carrier bytes are mixed with epoch/lane low-order state, converted to exact binary FP32 values, and evaluated as:

`out = fp32(fp32(a * b) + c)`

The output is serialized as little-endian IEEE-754 float32 for reference hashing.

The full program output therefore contains **8,500,000 float32 values = 34,000,000 bytes** for one completely materialized epoch.

## PASS gate

The MVP passes only if all seven independent gates succeed:

1. real Lunar127 carrier anchor hash;
2. ≥1 MHz measured sequential AVK-1 transition rate;
3. fixed AVK-1 chain checkpoint at epoch 1,000,000;
4. exact 17 MFLOP-eq VCK-2 program envelope;
5. all predetermined random/selective compute challenges;
6. complete 34 MB compute-output checkpoint at epoch 1,000,000;
7. complete 262,144-byte hologram-state checkpoint at epoch 1,000,000.

## Interpretation

A PASS establishes that the defined 17-MFLOP-eq-per-epoch compute space is **deterministically addressable and reconstructable** while the carrier-anchored sequential clock exceeds 1 MHz. It does not establish 17 measured hardware TFLOPS. The dashboard separately times full physical checkpoint materialization and reports that observed GFLOP-eq/s.

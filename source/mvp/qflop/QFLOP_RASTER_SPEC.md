# Lunar127 QFLOP@1MHz Virtual Raster MVP

Release: **0.8.0**  
Schemas: `LUNAR127/QCK-1/1`, `LUNAR127/VBT-1/1`

## Moonshot target

The MVP defines **1 QFLOP/s-equivalent represented deterministic compute availability at a verified 1 MHz processable-hologram clock**.

At 1,000,000 epochs/s, exactly **1,000,000,000 FLOP-equivalent operations must be addressable per epoch**.

### QCK-1 exact envelope

- 122,070 × VFMA-4096 = 499,998,720 FMA lanes.
- 1 × VFMA-1,280 tail = 1,280 FMA lanes.
- Total = **500,000,000 FMA lanes/epoch**.
- 2 FLOP-eq per FMA lane = **1,000,000,000 FLOP-eq/epoch**.
- Vector commands = **122,071/epoch**.
- At 1 MHz = **122.071 billion represented vector commands/s**.
- At 1 MHz = **1,000,000,000,000,000 FLOP-eq/s = 1.000 PFLOP/s-eq (QFLOP)**.

The PFLOP/QFLOP figure is represented/reconstructable virtual compute, not measured physical host FLOPS.

## VBT-1 virtual raster

The output address space is:

- width: **25,000,000,000 pixels**
- height: **40,000,000,000 pixels**
- total: **10^21 pixels**
- RGBA8 fully materialized equivalent: **4 × 10^21 bytes = 4 ZB**

A single conventional BigTIFF is not used. BigTIFF has 64-bit file offsets, while common LibTIFF interfaces expose ImageWidth/ImageLength as uint32. This raster exceeds both the per-axis conventional representation and the 64-bit byte-offset space if fully materialized.

VBT-1 therefore uses a **virtual coordinate manifest + procedural raster kernel + on-demand BigTIFF shards**. The included `sample_materialized_viewport.btf` is a real BigTIFF shard demonstrating the materialization format.

## Rendering requirement

The entire virtual raster is fitted into the viewport immediately as a sampled/LOD representation. The benchmark measures the final viewport raster time and requires **≤2.000 s** for the screen-population gate.

Only display pixels are physically produced. At 1280×720, that is 921,600 physical viewport pixels rather than 10^21 source pixels. Zooming changes the sampled source window and progressively exposes finer deterministic detail.

## Proof gates

A full MVP PASS requires:

1. carrier anchor PASS;
2. sequential AVK-1 rate ≥1 MHz;
3. one-million-epoch AVK chain checkpoint PASS;
4. exact QCK-1 1-billion-FLOP-eq/epoch envelope PASS;
5. all fixed compute-range reconstruction challenges PASS;
6. a fixed 1,000,000-lane (4 MB output) physical compute slice PASS;
7. fixed virtual-raster coordinate challenges PASS;
8. final viewport population ≤2 s.

## What this proves—and does not prove

PASS demonstrates that the virtual console sustains the sequential 1 MHz control clock while the specified QFLOP compute program and 10^21-pixel raster are deterministically addressable and selectively materializable. It does **not** prove one quadrillion floating-point operations were physically executed per second, nor that 4 ZB of pixels were physically stored or transmitted.

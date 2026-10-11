# Lunar127 EXA@1M Raster Cube MVP

Release: **0.9.0**  
Schemas: `LUNAR127/ECK-1/1`, `LUNAR127/VRC-1/1`

## Primary moonshot

The MVP defines **1 EFLOP/s-equivalent represented deterministic compute availability at a verified 1 MHz processable-hologram clock**.

At 1,000,000 epochs/s:

- **1,000,000,000,000 FLOP-eq/epoch**
- **500,000,000,000 FMA lanes/epoch**
- **122,070,312 × VFMA-4096**
- **1 × VFMA-2048 tail**
- **122,070,313 addressable vector commands/epoch**
- **122.070313 trillion represented vector commands/s**
- **1,000,000,000,000,000,000 FLOP-eq/s = 1.000 EFLOP/s-eq**

`EFLOP/s-eq` is represented/reconstructable virtual compute. It is not a claim that the laptop physically executes 10^18 floating-point operations each second.

## VRC-1 raster cube

Virtual dimensions:

- X = **25,000,000,000**
- Y = **40,000,000,000**
- Z = **1,000**
- addressable voxels = **10^24**
- RGBA8 fully materialized equivalent = **4 × 10^24 bytes = 4 YB**

At a 1 MHz epoch clock, treating the complete volume as one logical state per epoch corresponds to **4 × 10^30 bytes/s = 4 quettabytes/s of represented volume-state evolution**. This is not physical I/O.

The browser never allocates the full cube. It uses 64-bit X/Y coordinates plus Z and deterministically reconstructs requested voxels, slices, and finite ray-sampled viewports.

## Rendering

The virtual console renders in two stages:

1. a coarse volume projection that fills the display;
2. a refined ray-sampled projection.

The primary user-visible rendering gate is **time to first full-screen viewport fill ≤ 2.000 s**. Final refinement time is reported separately.

Slice mode can inspect any exact depth plane from Z=0 through Z=999.

## BigTIFF materialization

BigTIFF remains a finite materialization format rather than the complete virtual address space. The package includes `sample_1000_depth_volume_shard.btf`, a real BigTIFF containing **all 1,000 depth planes** at a small 64×64 XY sampling.

## Full PASS gates

1. carrier anchor matches;
2. AVK sequential one-million-epoch chain matches;
3. measured AVK rate ≥1 MHz;
4. ECK-1 exact exaflop envelope matches;
5. all fixed compute challenges match independent SHA-256 references;
6. the 1,000,000-lane physical compute proof slice matches;
7. all fixed 3-D voxel challenges match;
8. time to first full viewport fill ≤2 seconds.

The dashboard always labels represented compute separately from physically materialized compute.

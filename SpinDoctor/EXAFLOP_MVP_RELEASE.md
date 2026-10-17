# Lunar127 0.9.0 — EXA@1M Raster Cube MVP

This release extends the 0.8.0 QFLOP virtual-raster experiment to:

- **1 EFLOP/s-equivalent represented deterministic compute at 1 MHz**
- **1 TFLOP-equivalent compute envelope per hologram epoch**
- **122,070,313 vector commands per epoch**
- **25,000,000,000 × 40,000,000,000 × 1,000 virtual raster cube**
- **10^24 addressable voxels**
- **4 YB RGBA8 fully materialized equivalent**
- finite volume/slice rendering with a **≤2 s first-screen-fill gate**
- fixed compute and 3-D voxel challenge vectors
- a real BigTIFF sample shard containing all **1,000 depth planes** at a reduced XY sampling

`EFLOP/s-eq` and represented volume-state rates are virtual/reconstructable quantities, not physical laptop FLOPS, RAM bandwidth, or storage throughput.

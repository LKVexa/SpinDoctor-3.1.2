# Lunar127 0.6.0 — Holographic Availability + Virtual Command Lab

This release adds **VCK-1**, a carrier-anchored virtual command benchmark, beside the existing AVK-1 1 MHz holographic availability test.

## Two primary benchmark axes

1. **AVK-1 Holographic Availability** — measured dependency-chained epochs/s, represented state rate, selective reconstruction, and full-state integrity proofs.
2. **VCK-1 Virtual Command Throughput** — measured valid virtual commands/s through synthesize, decode, execute, state mutation, dependency commit, and retirement.

## Why both matter

AVK-1 answers: *Can the processable representation advance at ≥1 MHz while its full state remains deterministically available?*

VCK-1 answers: *How much virtual command work can the same class of host retire, and therefore how many commands can be budgeted to each 1 MHz hologram epoch?*

The benchmark does not equate VCK-1 commands with native CPU instructions and does not equate represented holographic GB/s with physical memory or I/O throughput.

# Lunar127 1.1.4 Run Session Specification

Schema: `LUNAR127/RUN-SESSION/1`

## Controls

- Run Simulation
- Current Seed (read-only 64 hex)
- Copy Seed
- Replay Seed
- Custom Seed (exactly 64 hex)
- Randomize Seed On Run (default ON)

## State transition

A successful Run Simulation event performs one and only one seed transition. When randomization is ON, it generates a new launch seed. When OFF, it uses Custom Seed if valid, otherwise reuses Current Seed. Replay Seed always reuses Custom Seed when supplied, otherwise Current Seed.

The session seed is:

`SHA256(launchSeed || carrierProgramBinding || BRWWORD_SHA256)`

where `carrierProgramBinding = 4ef0a6992dfdbe039a7cb6aa4b67b5de98bd3798f1fb4563ca0e6342c32e86b6` and `BRWWORD_SHA256 = 8222fd2c7b321703b9e6f44fa22a7a7cb069aa2d7cd311fb00f6c2eeb1549b83`.

## Reset scope

Each run resets the active session's seed pair, challenge addresses, session checkpoint chain, raster fingerprint, viewport timing state, run status and proof display. View controls are explicitly outside this transition.

## Determinism

For fixed carrier binding, BRWWORD and launch seed, session seed, voxel salts, challenge context, checkpoint root and raster fingerprint are deterministic. Fresh launch seeds are intended to produce different raster fields.

## Measurement boundary

The Run Simulation button does not claim to rerun the physical Windows C# benchmark. Host clock/FLOP diagnostics remain launch-time evidence; dashboard sessions exercise the deterministic represented-compute/raster model.


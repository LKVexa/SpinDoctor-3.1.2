# Lunar127 0.5.0 Availability Validation

## Result

**PASS** for the packaged AVK-1 reference implementation.

### Independent reference-vector verification

The Python reference implementation passed at the 1,000,000-epoch depth:

- carrier page anchor SHA-256: PASS
- 4 KiB selective range challenges reached by 1,000,000 epochs: 3/3 PASS
- full 262,144-byte checkpoint hashes reached by 1,000,000 epochs: 2/2 PASS
- sequential dependency-chain checkpoints: 2/2 PASS

### JavaScript worker implementation validation

The same algorithm used by the dashboard was separately executed in a Node.js v22.16.0 validation runtime against the fixed reference vectors through 10,000,000 epochs:

- deterministic proofs: **21/21 PASS**
- sequential-chain checkpoints: **4/4 PASS**
- selective 4 KiB challenges: **12/12 PASS**
- full-state checkpoint hashes: **4/4 PASS**
- AVK-1 frequency gate: **PASS**

The packaging runtime measured approximately **39.079 MHz** for this defined lightweight AVK-1 availability kernel, including the scheduled reference proofs.

This performance result is **not** a measurement of the user's computer and is **not** a physical 10+ TB/s memory/storage/network claim. It is included only to demonstrate that the packaged benchmark implementation and fixed vectors work end to end.

## Research boundary

The dashboard's `full-state equivalent rate` is:

`262,144 bytes × measured valid AVK-1 epochs/s`

That value describes deterministically reconstructable logical state. It is not physical byte movement and it is not an entropy-generation rate.

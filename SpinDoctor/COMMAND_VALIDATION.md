# VCK-1 Command Benchmark Validation

Release: **0.6.0**

## Static / independent reference validation

The VCK-1 reference generator and the packaged independent Python verifier agree at all fixed checkpoints:

- 100,000 commands — PASS
- 1,000,000 commands — PASS
- 5,000,000 commands — PASS
- 10,000,000 commands — PASS

The JavaScript command kernel used by the dashboard was independently cross-checked against the same vectors:

- 1,000,000-command complete VM state SHA-256 — PASS
- 10,000,000-command complete VM state SHA-256 — PASS

10,000,000-command expected/observed state SHA-256:

`2494ee0cb42372019ec03daf21cdf7c54058815ab98fa27dbf9c335f7efb33be`

## Syntax / runtime smoke validation

- Dashboard JavaScript parse: PASS
- Node diagnostic runtime: v22.16.0
- Python verifier runtime: Python 3.13.5

A short Node diagnostic run produced approximately **19 M VCK-1 commands/s** in the packaging environment. This number is **not a standard-laptop claim or a benchmark for the user's computer**. The browser dashboard must be run on the target laptop to produce that machine's result.

## Measurement interpretation

The primary measured result is retired VCK-1 virtual commands per second. At a 1 MHz hologram clock:

`commands per epoch = retired commands/s / 1,000,000`

For example, 20 Mcommands/s corresponds to a 20-command budget per 1 MHz hologram epoch. VCK-1 commands are virtual-machine operations, not native CPU instructions.

# VCK-1 Virtual Command Throughput Benchmark

Release: **0.6.0**  
Schema: `LUNAR127/VIRTUAL-COMMAND-KERNEL/1`

## Research question

**How many deterministic virtual hologram commands can a standard host generate, decode, execute, and retire per second in this processable-hologram model?**

This is deliberately a second axis from the AVK-1 1 MHz availability test. AVK-1 measures valid hologram epochs per second and reconstructable state availability. VCK-1 measures **virtual instruction/command retirement capacity**.

## What counts as one command

A command is counted only after all of the following have occurred:

1. Synthesize the command key from the prior dependency chain and command index.
2. Decode one of eight VCK-1 opcodes and operand registers.
3. Execute the command.
4. Mutate registers, carrier address state, scratch state, and/or phase-textured symbol state as required.
5. Commit the resulting value into the sequential dependency chain.
6. Retire the command.

Therefore this benchmark does **not** count empty loop iterations, pre-generated command words, native CPU instructions, or UI counter increments.

## Command set

- `VLOAD` — resolve/load an epoch-addressed processable-hologram symbol.
- `VXOR` — register XOR/rotate transformation.
- `VADDMUL` — modular add/multiply transformation.
- `VADDR` — resolve an address in the 261,888-byte payload field.
- `VSTORE` — commit a byte to the VM scratch plane.
- `VBRANCH` — execute a state-dependent branch and state mix.
- `VPHASE` — reconstruct an epoch-dependent phase-textured symbol.
- `VCOMMIT` — explicitly mix state into the dependency chain.

The opcode stream is itself dependent on prior retired state.

## Primary metrics

- **Retired virtual commands/s**
- **Mcommands/s**
- **Commands available per 1 MHz hologram epoch** = `commands/s ÷ 1,000,000`
- **Nominal epoch capacity at 64 commands/epoch** = `commands/s ÷ 64`
- **Nominal represented state rate at that capacity** = `epoch capacity × 262,144 bytes`

The last figure remains a represented/reconstructable state rate—not physical memory throughput.

## Correctness gate

Before the timed sustained run, the browser executes a deterministic **1,000,000-command conformance pass** and compares registers, chain state, scratch SHA-256, opcode counts, and complete VM-state SHA-256 against `commands/reference_vectors.json`.

Fixed checkpoints are also provided for 100,000, 5,000,000 and 10,000,000 commands and can be independently regenerated with:

`python commands\verify_reference_vectors.py`

## Standard-computer interpretation

There is intentionally no universal number labeled "a standard laptop." The dashboard measures the machine that actually runs the test. For reporting, record browser, CPU model, power mode, battery/AC state, and the median of several runs.

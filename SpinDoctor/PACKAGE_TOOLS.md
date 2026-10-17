# Current package tools

Run the package-root `BOOT.cmd` to verify the authoritative image, refresh the
saved code projection, and launch SpinDoctor. The saved projection retains seeds,
checkpoints, and reports. Its `BOOT_SAFE.cmd`, `BOOT_DIRECT.cmd`, and
`BOOT_LOOPBACK.cmd` aliases all launch the current SpinDoctor terminal.

The tools below can be run from the saved `workspace\projection\mvp` folder. They
locate the containing package and compile its harness in Windows PowerShell,
without creating a transient executable:

* `VERIFY.cmd` checks both current TIFF/GIF carriers. Supply `gif`, `tiff`, or a
  quoted carrier filename to check one image.
* `INSPECT.cmd [gif|tiff|filename]` prints the carrier inventory.
* `BUILD_HARNESS.cmd` confirms that the current generic harness compiles.
* `DIAGNOSE.cmd` checks current carrier integrity, the required native/UI payloads,
  and any corresponding saved projection source hashes. It does not run a
  simulation.
* `RESET_CURRENT.cmd` preserves saved data and explains how to use `q.reset` in
  the running terminal to reset the active model.

For a bounded carrier smoke check without materialization or program execution:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\PACKAGE_TOOLS.ps1 -Mode Smoke
```

The underlying harness `--smoke` emits `PASS MVP-SMOKE` for this package after
verifying its archive, launchable entrypoint, native sources, and declared required
files. Legacy carriers that have no MVP metadata retain the older VM smoke path.

`BENCHMARK_DASHBOARD.cmd` refreshes machine-local verified carrier decode/hash and
MVP smoke measurements. Its geometry comes from the measured image, and the
report includes the exact image hash. Compilation and process startup are outside
the recorded timings. This benchmark does not execute the quantum model or imply
physical quantum, optical, or represented-operation throughput.

When using a projection extracted to another folder, set
`SPINDOCTOR_PACKAGE_ROOT` to the package folder containing `BOOT.cmd`, `harness`,
and `cartridge` before running these tools.

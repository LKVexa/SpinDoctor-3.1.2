# Lunar127 Showcase 0.4.0 — Performance Dashboard Overlay

This release layers an instrumented throughput dashboard and machine-local benchmark harness over the unchanged Lunar127 Showcase 0.3.1 Hotfix 4 image-resident cartridge.

## Added

- `PERFORMANCE_DASHBOARD.html` — interactive rate sweep from 1 MHz through 1 GHz model ceiling.
- `OPEN_PERFORMANCE_DASHBOARD.cmd` — opens the dashboard using reference metrics.
- `BENCHMARK_DASHBOARD.cmd` — rebuilds the supplied C# evaluator, runs machine-local `--verify` / `--smoke` timings, refreshes dashboard data, and opens the dashboard.
- `benchmark/carrier_profile.json` — geometry parsed from the attached TIFF/GIF headers.
- `benchmark/latest_metrics.js` / `.json` — dashboard benchmark data.
- `benchmark/Run-Benchmark.ps1` — machine-local benchmark driver.
- `benchmark/WALKTHROUGH.md` — interpretation and walkthrough guide.

## Measurement discipline

The dashboard deliberately separates:
- **MEASURED**: host evaluator timing from the package or the current machine.
- **MODELED**: capacity and processing demand implied by a selected page-spool frequency.

The dashboard does not claim the supplied Windows/.NET evaluator, filesystem, display hardware, or any optical mechanism physically runs at 1 MHz or higher.

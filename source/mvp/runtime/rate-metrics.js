/* Measured clock conversion for the fixed 10^25 FLOP-equivalent domain. */
(function (root, factory) {
  "use strict";
  var api = factory();
  if (typeof module === "object" && module.exports) module.exports = api;
  else root.SimulatorRates = api;
})(typeof globalThis !== "undefined" ? globalThis : this, function () {
  "use strict";

  function positive(value) {
    if (typeof value !== "number" && typeof value !== "string") return null;
    if (typeof value === "string" && value.trim() === "") return null;
    var number = Number(value);
    return Number.isFinite(number) && number > 0 ? number : null;
  }

  function unavailable(reason) {
    return {
      measured: false,
      epochsPerSecond: null,
      clockMHz: null,
      representedQ: null,
      representedFlops: null,
      physicalFlops: null,
      physicalG: null,
      expansion: null,
      consistent: false,
      reason: reason
    };
  }

  function matches(declared, expected) {
    var number = positive(declared);
    return number !== null && Number.isFinite(expected) && expected > 0 && Math.abs(number - expected) <=
      1e-9 * Math.max(number, expected);
  }

  function calculate(metrics) {
    if (!metrics || typeof metrics !== "object") {
      return unavailable("Host benchmark metrics are missing.");
    }
    if (metrics.notRun) return unavailable("The host benchmark has not run.");
    var epochs = positive(metrics.sequentialEpochsPerSecondMedian);
    if (epochs === null) return unavailable("A finite positive measured epoch rate is required.");

    var clockMHz = epochs / 1e6;
    var representedQ = epochs / 1e5;
    var representedFlops = epochs * 1e25;
    if (positive(clockMHz) === null || positive(representedQ) === null ||
        positive(representedFlops) === null) {
      return unavailable("The measured epoch rate cannot be converted to finite positive rates.");
    }

    var issues = [];
    var own = Object.prototype.hasOwnProperty;
    [['flopEquivalentPerEpoch', '10000000000000000000000000'],
      ['fmaLanesPerEpoch', '5000000000000000000000000']].forEach(function (field) {
      if (own.call(metrics, field[0]) && metrics[field[0]] !== field[1]) {
        issues.push('Declared ' + field[0] + ' does not match the exact ledger domain.');
      }
    });
    if (own.call(metrics, "hologramClockMHzMeasured") &&
        !matches(metrics.hologramClockMHzMeasured, clockMHz)) {
      issues.push("Declared clock does not match the measured epoch rate.");
    }
    if (own.call(metrics, "representedQuettaFlopEquivalentPerSecondAtMeasuredClock") &&
        !matches(metrics.representedQuettaFlopEquivalentPerSecondAtMeasuredClock, representedQ)) {
      issues.push("Declared represented rate does not match the measured epoch rate.");
    }

    var diagnostic = metrics.physicalScalarFlopDiagnostic;
    var physical = diagnostic && diagnostic.measured === true ?
      positive(diagnostic.flopsPerSecond) : null;
    if (physical !== null && diagnostic.countedFlops !== undefined &&
        diagnostic.elapsedSeconds !== undefined &&
        !matches(physical, positive(diagnostic.countedFlops) / positive(diagnostic.elapsedSeconds))) {
      issues.push('Scalar diagnostic rate does not match its counted operations and elapsed time.');
    }
    var expansion = physical === null ? null : representedFlops / physical;
    if (!Number.isFinite(expansion)) expansion = null;
    return {
      measured: true,
      epochsPerSecond: epochs,
      clockMHz: clockMHz,
      representedQ: representedQ,
      representedFlops: representedFlops,
      physicalFlops: physical,
      physicalG: physical === null ? null : physical / 1e9,
      expansion: expansion,
      consistent: issues.length === 0,
      reason: issues.join(" ")
    };
  }

  return { calculate: calculate };
});

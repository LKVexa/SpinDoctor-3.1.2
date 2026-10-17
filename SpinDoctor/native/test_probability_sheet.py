"""Statistical geometry, known-field and genuine small CPU/GPU checks."""
from __future__ import annotations

import argparse
import json
import math
from pathlib import Path
import unittest

from probability_sheet import (ReconstructionCancelled, build_probability_sheet, exact_result, normalize_config,
                               probability_at, reconstruct_values, sampling_design)
from shared_epoch_xpu import Session, plan_budget

SEED_256 = str(2**255 + 123456789012345678901234567890123456789)


def independent_shape(seed, index):
    x = (seed ^ (index & 0xFFFFFFFF) ^ (index >> 32)) & 0xFFFFFFFF
    x = ((x ^ (x >> 16)) * 0x7FEB352D) & 0xFFFFFFFF
    x = ((x ^ (x >> 15)) * 0x846CA68B) & 0xFFFFFFFF
    return x ^ (x >> 16)


def independent_result(seed, pattern, bit_id, epoch):
    mask_seed = independent_shape(seed ^ 0xA511E9B3, epoch)
    mask_word = independent_shape(mask_seed, bit_id // 32)
    return ((pattern >> (bit_id % 8)) & 1) ^ ((mask_word >> (bit_id % 32)) & 1)


def oracle_entries(rows, seed=123456, pattern=0x69):
    output = []
    for row in rows:
        base = (pattern >> (row["bitId"] % 8)) & 1
        e = row["epoch"]
        current = independent_result(seed, pattern, row["bitId"], e - 1)
        answer = independent_result(seed, pattern, row["bitId"], e)
        operand = current ^ answer
        output.append({"bitId": str(row["bitId"]), "epoch": str(e), "inputA": current,
                       "inputB": operand, "result": answer, "matches": True, "backend": "independent-test-oracle",
                       "baseBit": base})
    return output


def sheet_for(n, e, options):
    config, leaves, rows = sampling_design(n, e, options)
    return reconstruct_values(n, e, 123456, 0x69, config, leaves, rows, oracle_entries(rows))


class ProbabilitySheetTests(unittest.TestCase):
    def test_256_bit_seed_is_decimal_json_string_and_replay(self):
        options = {"sampling_seed": SEED_256, "depth": 2, "address_regions": 3,
                   "epoch_regions": 2, "training_per_leaf": 5, "holdout_per_leaf": 3}
        first = sampling_design(1001, 17, options)
        second = sampling_design(1001, 17, options)
        self.assertEqual(first, second)
        self.assertEqual(first[0]["sampling_seed"], SEED_256)
        self.assertIsInstance(json.loads(json.dumps(first[0]))["sampling_seed"], str)
        maximum = normalize_config({"sampling_seed": str(2**256 - 1)}, 1001, 17)
        self.assertEqual(maximum["sampling_seed"], str(2**256 - 1))
        self.assertEqual(first[2], sampling_design(1001, 17, first[0])[2])
        third = sampling_design(1001, 17, {**options, "sampling_seed": str(int(SEED_256) + 1)})
        self.assertNotEqual(first[2], third[2])

    def test_joint_strata_cover_domain_without_overlap(self):
        config, leaves, rows = sampling_design(97, 13, {"sampling_seed": SEED_256, "depth": 3,
                               "address_regions": 4, "epoch_regions": 4,
                               "training_per_leaf": 2, "holdout_per_leaf": 1})
        self.assertEqual(sum(int(leaf["population"]) for leaf in leaves), 97 * 13)
        for epoch in range(1, 14):
            for address in range(97):
                matching = [leaf for leaf in leaves if leaf["epochStart"] <= epoch < leaf["epochEndExclusive"]
                            and int(leaf["addressStart"]) <= address < int(leaf["addressEndExclusive"])
                            and address % leaf["modulus"] == leaf["residue"]]
                self.assertEqual(len(matching), 1)
        coordinates = {(row["epoch"], row["bitId"]) for row in rows}
        self.assertEqual(len(coordinates), len(rows))
        training = {(row["epoch"], row["bitId"]) for row in rows if row["split"] == "training"}
        heldout = {(row["epoch"], row["bitId"]) for row in rows if row["split"] == "holdout"}
        self.assertFalse(training & heldout)
        for row in rows:
            leaf = leaves[row["leaf"]]
            self.assertTrue(leaf["epochStart"] <= row["epoch"] < leaf["epochEndExclusive"])
            self.assertEqual(row["bitId"] % (2**config["depth"]), leaf["residue"])

    def test_sparse_sheet_has_no_claim_of_exact_unsampled_bits(self):
        options = {"sampling_seed": SEED_256, "depth": 2, "address_regions": 2,
                   "epoch_regions": 2, "training_per_leaf": 8, "holdout_per_leaf": 4}
        sheet = sheet_for(513, 9, options)
        self.assertFalse(sheet["fullReturn"]["unsampledExactBitsRecovered"])
        self.assertFalse(sheet["fullReturn"]["allDomainPointsMaterialized"])
        self.assertLess(sheet["validation"]["trainingPairs"], 513 * 9)
        predictions = probability_at(sheet, "300", 5)
        self.assertFalse(predictions["exactBitRecovered"])
        self.assertTrue(0 <= predictions["probabilityOfWhite"] <= 1)
        self.assertTrue(-1 <= predictions["spinMean"] <= 1)
        self.assertTrue(0 <= sheet["validation"]["holdoutBinaryAccuracy"] <= 1)
        population = int(sheet["domain"]["populationPoints"])
        independently_weighted = sum(int(leaf["population"]) * leaf["holdoutBinaryAccuracy"]
                                     for leaf in sheet["leaves"]) / population
        self.assertAlmostEqual(sheet["validation"]["holdoutBinaryAccuracy"], independently_weighted)

    def test_known_equation_matches_independent_exhaustive_oracle(self):
        sheet = sheet_for(97, 5, {"sampling_seed": SEED_256, "training_per_leaf": 4,
                                 "holdout_per_leaf": 2, "depth": 2})
        descriptor = sheet["exactFieldDescriptor"]
        for epoch in range(1, 6):
            for address in range(97):
                self.assertEqual(exact_result(descriptor, address, epoch),
                                 independent_result(123456, 0x69, address, epoch))
        with self.assertRaises(ValueError):
            exact_result(descriptor, 97, 1)
        with self.assertRaises(ValueError):
            exact_result(descriptor, 0, 6)
        high_descriptor = {**descriptor, "workerCount": str(2**39), "epochEndInclusive": 2**63 + 17}
        for epoch in (2**32 - 1, 2**32, 2**63 + 17):
            for address in (2**37 - 1, 2**37, 2**38 + 31):
                self.assertEqual(exact_result(high_descriptor, address, epoch),
                                 independent_result(123456, 0x69, address, epoch))

    def test_exhaustive_training_makes_leaf_intervals_exact(self):
        sheet = sheet_for(23, 3, {"sampling_seed": SEED_256, "depth": 3,
                                 "address_regions": 4, "epoch_regions": 2,
                                 "training_per_leaf": 4096, "holdout_per_leaf": 1})
        self.assertEqual(sheet["validation"]["trainingPairs"], 69)
        self.assertEqual(sheet["validation"]["holdoutPairs"], 0)
        self.assertTrue(sheet["fullReturn"]["allDomainPointsMaterialized"])
        total_white = sum(independent_result(123456, 0x69, i, e) for e in range(1, 4) for i in range(23))
        for leaf in sheet["leaves"]:
            self.assertTrue(leaf["exhaustiveTraining"])
            self.assertEqual(leaf["probabilityInterval"], [leaf["probabilityOfWhite"]] * 2)
        self.assertAlmostEqual(sheet["globalEstimate"]["probabilityOfWhite"], total_white / 69)
        self.assertAlmostEqual(sheet["globalEstimate"]["probabilityInterval"][0], total_white / 69)

    def test_sparse_samples_cannot_distinguish_opposite_unsampled_fields(self):
        config, leaves, rows = sampling_design(1000, 10, {"sampling_seed": SEED_256, "depth": 1,
                               "address_regions": 2, "epoch_regions": 2,
                               "training_per_leaf": 8, "holdout_per_leaf": 4})
        # Both completions agree at every observed point and differ everywhere else.
        observed = {(row["epoch"], row["bitId"]) for row in rows}
        entries = [{"bitId": str(row["bitId"]), "epoch": str(row["epoch"]), "inputA": 0,
                    "inputB": 0, "result": 0, "matches": True} for row in rows]
        sheet = reconstruct_values(1000, 10, 0, 0, config, leaves, rows, entries)
        possible_white_fraction_a = 0.
        possible_white_fraction_b = (10000 - len(observed)) / 10000
        self.assertGreater(possible_white_fraction_b - possible_white_fraction_a, .99 - .001)
        self.assertFalse(sheet["fullReturn"]["unsampledExactBitsRecovered"])

    def test_confidence_formula_and_weighted_hierarchy(self):
        sheet = sheet_for(1001, 19, {"sampling_seed": SEED_256, "depth": 3,
                                   "address_regions": 3, "epoch_regions": 4,
                                   "training_per_leaf": 32, "holdout_per_leaf": 8})
        radius = math.sqrt(math.log(2 * len(sheet["leaves"]) / .025) / (2 * 32))
        self.assertAlmostEqual(sheet["leaves"][0]["hoeffdingRadius"], radius)
        for node in sheet["hierarchy"]:
            children = [leaf for leaf in sheet["leaves"] if leaf["epochRegion"] == node["epochRegion"]
                        and leaf["addressRegion"] == node["addressRegion"]
                        and leaf["residue"] % node["modulus"] == node["residue"]]
            population = sum(int(leaf["population"]) for leaf in children)
            weighted = sum(int(leaf["population"]) * leaf["probabilityOfWhite"] for leaf in children) / population
            self.assertAlmostEqual(node["probabilityOfWhite"], weighted)
            self.assertEqual(int(node["population"]), population)
        self.assertAlmostEqual(sheet["globalEstimate"]["spinMean"], 1 - 2 * sheet["globalEstimate"]["probabilityOfWhite"])
        proxy = sum((int(leaf["population"]) / (1001 * 19))**2 / leaf["trainingCount"]
                    for leaf in sheet["leaves"] if not leaf["exhaustiveTraining"])
        global_radius = math.sqrt(math.log(2 / .025) * proxy / 2)
        self.assertAlmostEqual(sheet["globalEstimate"]["stratifiedHoeffdingRadius"], global_radius)
        self.assertLess(global_radius, radius)
        self.assertAlmostEqual(sheet["confidence"]["leafFamilyFailureProbability"] +
                               sheet["confidence"]["globalFailureProbability"], .05)

    def test_rejects_nonbinary_or_unverified_samples(self):
        config, leaves, rows = sampling_design(100, 3, {"sampling_seed": SEED_256, "depth": 0,
                               "address_regions": 1, "epoch_regions": 1,
                               "training_per_leaf": 8, "holdout_per_leaf": 4})
        entries = oracle_entries(rows)
        entries[0]["result"] = .5
        with self.assertRaises(ValueError):
            reconstruct_values(100, 3, 123456, 0x69, config, leaves, rows, entries)
        entries = oracle_entries(rows)
        entries[0]["matches"] = False
        with self.assertRaises(ValueError):
            reconstruct_values(100, 3, 123456, 0x69, config, leaves, rows, entries)

    def test_rejects_uncommitted_epochs_and_unbounded_design(self):
        with self.assertRaises(ValueError):
            sampling_design(100, 0)
        with self.assertRaises(ValueError):
            sampling_design(100, 3, {"epoch_end": 4})
        with self.assertRaises(ValueError):
            sampling_design(10000000, 100, {"depth": 12, "address_regions": 4})
        with self.assertRaises(ValueError):
            normalize_config({"sampling_seed": str(2**256)}, 100, 3)
        with self.assertRaises(ValueError):
            normalize_config({"confidence": True}, 100, 3)


def live_cpu_gpu_check():
    """One 1 MiB actual RAM reservoir, eight commits, 192 statistical queries."""
    session = Session(plan_budget({"auto_ram": False, "budget_mib": 1,
                                  "seed": 0x1234ABCD, "cpu_workers": 2,
                                  "sample_count": 64, "duration_seconds": .01}))
    try:
        run = session.execute(epoch_count=8)
        if run["status"] != "COMPLETE":
            raise AssertionError("Small real CPU/GPU session did not complete.")
        before = session.snapshot()
        options = {"sampling_seed": SEED_256, "depth": 2, "address_regions": 2,
                   "epoch_regions": 2, "training_per_leaf": 8, "holdout_per_leaf": 4}
        sheet = build_probability_sheet(session, options)
        after = session.snapshot()
        if sheet["measuredSampling"]["virtualXorCredit"] != "0" or after["acknowledgedEpochs"] != before["acknowledgedEpochs"]:
            raise AssertionError("Reconstruction query incorrectly advanced/credited virtual work.")
        expected_queries = sheet["validation"]["trainingPairs"] + sheet["validation"]["holdoutPairs"]
        primary = sum(int(sheet["measuredSampling"][backend]["sampledPrimaryXorOperations"]) for backend in ("cpu", "gpu"))
        if primary != expected_queries or primary != 192:
            raise AssertionError("Actual query arithmetic does not equal statistical sample count.")
        if any(int(sheet["measuredSampling"][backend]["sampledPrimaryXorOperations"]) <= 0 for backend in ("cpu", "gpu")):
            raise AssertionError("Both genuine sampled compute backends must execute.")
        if after["initializationRAMWriteBytes"] != before["initializationRAMWriteBytes"] or after["epochRAMWriteBytes"] != "0":
            raise AssertionError("Reconstruction wrote new full RAM waves.")
        if after["recordedSeed"] != before["recordedSeed"]:
            raise AssertionError("Reconstruction changed the preserved field seed.")
        for entry in sheet["samples"]:
            expected = independent_result(0x1234ABCD, session.plan["initialPatternByte"], int(entry["bitId"]), int(entry["epoch"]))
            if entry["result"] != expected or entry["spin"] not in (-1, 1):
                raise AssertionError("Actual selected result disagrees with independent spin/XOR oracle.")
        replay = build_probability_sheet(session, options)
        if replay["sampleSha256"] != sheet["sampleSha256"]:
            raise AssertionError("Replay changed the fixed-seed positions or values.")
        cancel_before = session.snapshot()
        original_compute = session._compute
        completed_chunks = []
        def cancel_after_first_completed_chunk(ids, epoch, query=False):
            result = original_compute(ids, epoch, query=query)
            completed_chunks.append(len(ids))
            session.cancel_event.set()
            return result
        session._compute = cancel_after_first_completed_chunk
        try:
            try:
                build_probability_sheet(session, options)
            except ReconstructionCancelled:
                pass
            else:
                raise AssertionError("A paused reconstruction returned a completed sheet.")
        finally:
            session._compute = original_compute
        cancel_after = session.snapshot()
        cancelled_primary = sum(int(cancel_after[backend]["sampledPrimaryXorOperations"])
                                - int(cancel_before[backend]["sampledPrimaryXorOperations"])
                                for backend in ("cpu", "gpu"))
        if len(completed_chunks) != 1 or cancelled_primary != completed_chunks[0] or cancelled_primary >= primary:
            raise AssertionError("Cancellation either lost completed counters or issued later chunks.")
        if cancel_after["acknowledgedEpochs"] != cancel_before["acknowledgedEpochs"]:
            raise AssertionError("Cancelled reconstruction advanced shared epochs.")
        session.cancel_event.clear()
        resumed = build_probability_sheet(session, options)
        if resumed["sampleSha256"] != sheet["sampleSha256"]:
            raise AssertionError("Resumed fixed-seed reconstruction changed its sampling design or results.")
        return {"status": "PASS", "allocatedBytes": session.plan["totalReservoirBytes"],
                "committedEpochs": after["acknowledgedEpochs"], "samplePairsPerReconstruction": primary,
                "cpuQueryPrimaryXors": sheet["measuredSampling"]["cpu"]["sampledPrimaryXorOperations"],
                "gpuQueryPrimaryXors": sheet["measuredSampling"]["gpu"]["sampledPrimaryXorOperations"],
                "samplingSeedBits": int(SEED_256).bit_length(), "replaySampleSha256": sheet["sampleSha256"],
                "virtualQueryCredit": "0", "fieldSeedPreserved": True,
                "cancelledCompletedChunks": len(completed_chunks),
                "cancelledCompletedPrimaryXors": cancelled_primary,
                "cancelResumePreservesSeedAndSampleHash": True,
                "gpuDevice": session.plan["gpuDevice"]["name"],
                "selectedResultKind": "classical-simulated-binary-spin"}
    finally:
        if not session.running and session.phase != "released":
            session.release()


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--live", action="store_true", help="Run actual small CPU/OpenCL GPU and RAM checks.")
    parser.add_argument("--report", default="probability-sheet-validation.json")
    args = parser.parse_args()
    suite = unittest.defaultTestLoader.loadTestsFromTestCase(ProbabilitySheetTests)
    result = unittest.TextTestRunner(verbosity=2).run(suite)
    report = {"schema": "XOR-2ADIC-PROBABILITY-VALIDATION/1", "status": "PASS" if result.wasSuccessful() else "FAIL",
              "testsRun": result.testsRun, "failures": len(result.failures), "errors": len(result.errors)}
    if result.wasSuccessful() and args.live:
        report["actualCpuGpuCheck"] = live_cpu_gpu_check()
    Path(args.report).write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(report, indent=2))
    if not result.wasSuccessful():
        raise SystemExit(1)


if __name__ == "__main__":
    main()

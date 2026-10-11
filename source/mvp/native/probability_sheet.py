"""A finite-population probability sheet over the committed binary XOR field.

Low address bits define a 2-adic residue hierarchy.  Numerical address regions
and epoch regions keep sampling spread over the finite batch.  This estimates
probabilities; it does not infer each unsampled bit or physical spin measurement.
The known field equation is exported separately as an exact query descriptor.
"""
from __future__ import annotations

from collections import defaultdict
import hashlib
import json
import math
import random
import secrets
import time

import numpy as np

from shared_epoch_xpu import PressurePause, epoch_seed
from full_traversal_reference import exact, known_word

SCHEMA = "XOR-2ADIC-PROBABILITY-SHEET/1"
MAX_SAMPLES = 262144
MAX_LEAVES = 4096


class ReconstructionCancelled(RuntimeError):
    """Completed selected calculations remain counted; no partial sheet is returned."""


def _check_reconstruction_active(session):
    if session.cancel_event.is_set():
        raise ReconstructionCancelled("Reconstruction paused after the current selected CPU/GPU work completed; completed sample calculations remain counted.")
    try:
        session._pressure()
    except PressurePause as error:
        raise ReconstructionCancelled(str(error)) from error


def _integer(value, name, minimum, maximum):
    value = exact(value, name)
    if not minimum <= value <= maximum:
        raise ValueError(f"{name} must be {minimum}..{maximum}.")
    return value


def _sampling_seed(value):
    if isinstance(value, str):
        if not 1 <= len(value) <= 78 or any(char not in "0123456789" for char in value):
            raise ValueError("sampling_seed must be an ASCII decimal uint256 string or integer.")
        if len(value) > 1 and value[0] == "0":
            raise ValueError("sampling_seed must use canonical decimal notation without leading zeros.")
        value = int(value)
    if isinstance(value, bool) or not isinstance(value, int) or not 0 <= value < 2**256:
        raise ValueError("sampling_seed must fit an exact nonnegative uint256 integer.")
    return str(value)


def normalize_config(config, worker_count, committed_epochs):
    config = dict(config or {})
    allowed = {"depth", "address_regions", "epoch_regions", "training_per_leaf",
               "holdout_per_leaf", "confidence", "sampling_seed", "epoch_start", "epoch_end",
               "sampling_seed_origin"}
    if set(config) - allowed:
        raise ValueError("Unknown reconstruction options: " + ", ".join(sorted(set(config) - allowed)))
    if "sampling_seed_origin" in config and config["sampling_seed_origin"] not in ("explicit-replay", "os-random-recorded"):
        raise ValueError("Unknown recorded sampling_seed_origin metadata.")
    if worker_count < 1 or committed_epochs < 1:
        raise ValueError("Reconstruction requires a nonempty live domain and at least one committed epoch.")
    depth = _integer(config.get("depth", 3), "depth", 0, 12)
    address_regions = _integer(config.get("address_regions", 4), "address_regions", 1, 32)
    epoch_regions = _integer(config.get("epoch_regions", 4), "epoch_regions", 1, 32)
    training = _integer(config.get("training_per_leaf", 64), "training_per_leaf", 2, 4096)
    holdout = _integer(config.get("holdout_per_leaf", 16), "holdout_per_leaf", 1, 4096)
    start = _integer(config.get("epoch_start", 1), "epoch_start", 1, committed_epochs)
    end = _integer(config.get("epoch_end", committed_epochs), "epoch_end", start, committed_epochs)
    confidence = config.get("confidence", .95)
    if isinstance(confidence, bool) or not isinstance(confidence, (int, float)) or not math.isfinite(confidence) or not .5 < confidence < 1:
        raise ValueError("confidence must be finite and greater than .5 and less than 1.")
    supplied_seed = "sampling_seed" in config
    sampling_seed = _sampling_seed(config["sampling_seed"] if supplied_seed else secrets.randbits(256))
    address_regions = min(address_regions, worker_count)
    epoch_regions = min(epoch_regions, end - start + 1)
    if address_regions * epoch_regions * 2**depth > MAX_LEAVES:
        raise ValueError(f"Requested sheet exceeds the {MAX_LEAVES}-leaf bound.")
    return {"depth": depth, "address_regions": address_regions, "epoch_regions": epoch_regions,
            "training_per_leaf": training, "holdout_per_leaf": holdout,
            "confidence": float(confidence), "sampling_seed": str(sampling_seed),
            "sampling_seed_origin": "explicit-replay" if supplied_seed else "os-random-recorded",
            "epoch_start": start, "epoch_end": end}


def _strata(worker_count, config):
    modulus = 2**config["depth"]
    epoch_count = config["epoch_end"] - config["epoch_start"] + 1
    output = []
    for t in range(config["epoch_regions"]):
        epoch_lo = config["epoch_start"] + epoch_count * t // config["epoch_regions"]
        epoch_hi = config["epoch_start"] + epoch_count * (t + 1) // config["epoch_regions"]
        for region in range(config["address_regions"]):
            address_lo = worker_count * region // config["address_regions"]
            address_hi = worker_count * (region + 1) // config["address_regions"]
            for residue in range(modulus):
                first = address_lo + (residue - address_lo) % modulus
                address_count = 0 if first >= address_hi else 1 + (address_hi - 1 - first) // modulus
                if not address_count:
                    continue
                population = address_count * (epoch_hi - epoch_lo)
                train = min(config["training_per_leaf"], population)
                holdout = min(config["holdout_per_leaf"], population - train)
                output.append({"epochRegion": t, "addressRegion": region, "residue": residue,
                               "depth": config["depth"], "modulus": modulus,
                               "epochStart": epoch_lo, "epochEndExclusive": epoch_hi,
                               "addressStart": str(address_lo), "addressEndExclusive": str(address_hi),
                               "firstAddress": str(first), "addressCount": str(address_count),
                               "population": str(population), "trainingCount": train,
                               "holdoutCount": holdout})
    if sum(x["trainingCount"] + x["holdoutCount"] for x in output) > MAX_SAMPLES:
        raise ValueError(f"Requested reconstruction exceeds the {MAX_SAMPLES}-sample bound.")
    return output


def _draw_unique(rng, population, count):
    """Uniform ordered sample without replacement, including huge Python ints."""
    if count == population:
        result = list(range(population))
        rng.shuffle(result)
        return result
    chosen = set()
    result = []
    while len(result) < count:
        value = rng.randrange(population)
        if value not in chosen:
            chosen.add(value)
            result.append(value)
    return result


def sampling_design(worker_count, committed_epochs, config=None):
    normalized = normalize_config(config, worker_count, committed_epochs)
    leaves = _strata(worker_count, normalized)
    rng = random.Random(int(normalized["sampling_seed"]))
    rows = []
    for index, leaf in enumerate(leaves):
        count = leaf["trainingCount"] + leaf["holdoutCount"]
        ordinals = _draw_unique(rng, int(leaf["population"]), count)
        width = int(leaf["addressCount"])
        first = int(leaf["firstAddress"])
        for j, ordinal in enumerate(ordinals):
            rows.append({"leaf": index, "split": "training" if j < leaf["trainingCount"] else "holdout",
                         "epoch": leaf["epochStart"] + ordinal // width,
                         "bitId": first + (ordinal % width) * leaf["modulus"]})
    return normalized, leaves, rows


def _read_committed_samples(session, design_rows):
    """Accounted queries to live RAM and genuine CPU/OpenCL GPU, without commits."""
    by_epoch = defaultdict(list)
    for index, row in enumerate(design_rows):
        by_epoch[row["epoch"]].append((index, row["bitId"]))
    output = [None] * len(design_rows)
    with session.lock:
        if session.running or session.phase != "idle" or not session.ram or session.init_bytes != session.plan["totalReservoirBytes"]:
            raise RuntimeError("Pause a fully initialized live session before reconstruction.")
        committed = session.epochs
        if any(epoch < 1 or epoch > committed for epoch in by_epoch):
            raise ValueError("Reconstruction samples must belong to individually committed epochs.")
        for epoch, numbered in sorted(by_epoch.items()):
            for offset in range(0, len(numbered), 4096):
                _check_reconstruction_active(session)
                chunk = numbered[offset:offset + 4096]
                ids = np.array([entry[1] for entry in chunk], dtype=np.uint64)
                traces, cpu_count, _ = session._compute(ids, epoch, query=True)
                entries = session._entries(ids, traces, cpu_count, epoch)
                session.query_calls += 1
                for (index, _), entry in zip(chunk, entries):
                    output[index] = entry
        _check_reconstruction_active(session)
        if session.epochs != committed:
            raise RuntimeError("A query unexpectedly advanced the shared epoch descriptor.")
    return output


def exact_result(descriptor, bit_id, epoch):
    """Known-equation query; no measured RAM read or GPU call, no virtual credit."""
    bit_id = _integer(bit_id, "bitId", 0, int(descriptor["workerCount"]) - 1)
    epoch = _integer(epoch, "epoch", descriptor["epochStart"], descriptor["epochEndInclusive"])
    base = (descriptor["initialPatternByte"] >> (bit_id % 8)) & 1
    mask = (known_word(epoch_seed(descriptor["fieldSeed"], epoch), bit_id // 32) >> (bit_id % 32)) & 1
    return base ^ mask


def probability_at(sheet, bit_id, epoch):
    bit_id = _integer(bit_id, "bitId", 0, int(sheet["domain"]["workerCount"]) - 1)
    epoch = _integer(epoch, "epoch", sheet["domain"]["epochStart"], sheet["domain"]["epochEndInclusive"])
    depth = sheet["config"]["depth"]
    for leaf in sheet["leaves"]:
        if (leaf["epochStart"] <= epoch < leaf["epochEndExclusive"]
                and int(leaf["addressStart"]) <= bit_id < int(leaf["addressEndExclusive"])
                and bit_id % (2**depth) == leaf["residue"]):
            return {"probabilityOfWhite": leaf["probabilityOfWhite"],
                    "probabilityInterval": leaf["probabilityInterval"],
                    "spinMean": leaf["spinMean"], "spinInterval": leaf["spinInterval"],
                    "kind": "statistical-probability-estimate", "exactBitRecovered": False}
    raise RuntimeError("The coefficient table does not cover this domain point.")


def _coefficient(probability, lower, upper):
    return {"probabilityOfWhite": probability, "probabilityInterval": [lower, upper],
            "spinMean": 1 - 2 * probability, "spinInterval": [1 - 2 * upper, 1 - 2 * lower]}


def _counter_delta(before, after):
    result = {}
    for backend in ("cpu", "gpu"):
        result[backend] = {key: str(int(after[backend][key]) - int(before[backend][key]))
                           for key in ("sampledPrimaryXorOperations", "auxiliaryRepresentationXorOperations",
                                       "maskHashEvaluations", "querySampledPairs", "kernelDispatches")}
    result["actualSelectedWordReads"] = str(int(after["sampling"]["actualSelectedWordReads"]) - int(before["sampling"]["actualSelectedWordReads"]))
    result["actualSelectedReadBytes"] = str(int(after["sampling"]["actualSelectedReadBytes"]) - int(before["sampling"]["actualSelectedReadBytes"]))
    result["virtualXorCredit"] = str(int(after["virtualXorOperations"]) - int(before["virtualXorOperations"]))
    return result


def reconstruct_values(worker_count, committed_epochs, field_seed, initial_pattern_byte,
                       config, leaves, rows, entries):
    """Pure statistical stage; entries must already contain actual binary values."""
    if len(rows) != len(entries) or any(entry is None for entry in entries):
        raise ValueError("Every planned sample needs an actual result.")
    alpha = 1 - config["confidence"]
    alpha_leaf = alpha_global = alpha / 2
    leaf_count = len(leaves)
    collected = [{"training": [], "holdout": []} for _ in leaves]
    recorded = []
    for row, entry in zip(rows, entries):
        if int(entry["bitId"]) != row["bitId"] or int(entry["epoch"]) != row["epoch"]:
            raise ValueError("Returned sample coordinates differ from the planned sampling design.")
        if (entry["inputA"] not in (0, 1) or entry["inputB"] not in (0, 1)
                or entry["result"] not in (0, 1) or entry["result"] != (entry["inputA"] ^ entry["inputB"])
                or not entry["matches"]):
            raise ValueError("A sample is not a verified strict binary XOR result.")
        collected[row["leaf"]][row["split"]].append(entry["result"])
        recorded.append({**entry, "leaf": row["leaf"], "split": row["split"],
                         "spin": 1 - 2 * entry["result"], "measurementKind": "classical-simulated-binary-spin"})
    output_leaves = []
    total_population = worker_count * (config["epoch_end"] - config["epoch_start"] + 1)
    total_brier = total_flat_brier = total_absolute = total_accuracy = 0.
    for leaf, values in zip(leaves, collected):
        training = values["training"]
        holdout = values["holdout"]
        n = len(training)
        if n != leaf["trainingCount"] or len(holdout) != leaf["holdoutCount"]:
            raise ValueError("Sample counts do not match the planned stratum counts.")
        population = int(leaf["population"])
        probability = sum(training) / n
        radius = 0. if n == population else math.sqrt(math.log(2 * leaf_count / alpha_leaf) / (2 * n))
        lower, upper = max(0., probability - radius), min(1., probability + radius)
        h = len(holdout)
        brier = sum((value - probability)**2 for value in holdout) / h if h else None
        predicted = 1 if probability > .5 else 0
        accuracy = sum(value == predicted for value in holdout) / h if h else None
        if h:
            weight = population / total_population
            total_brier += weight * brier
            total_flat_brier += weight * .25
            total_absolute += weight * abs(sum(holdout) / h - probability)
            total_accuracy += weight * accuracy
        output_leaves.append({**leaf, **_coefficient(probability, lower, upper),
                              "trainingWhite": sum(training), "holdoutWhite": sum(holdout),
                              "holdoutProbabilityOfWhite": sum(holdout) / h if h else None,
                              "holdoutBrierScore": brier, "exhaustiveTraining": n == population,
                              "holdoutBinaryAccuracy": accuracy, "predictedBit": predicted,
                              "hoeffdingRadius": radius, "unsampledAfterTraining": str(population - n)})
    hierarchy = []
    for level in range(config["depth"] + 1):
        buckets = defaultdict(list)
        for leaf in output_leaves:
            buckets[(leaf["epochRegion"], leaf["addressRegion"], leaf["residue"] % (2**level))].append(leaf)
        for (time_region, address_region, residue), children in sorted(buckets.items()):
            population = sum(int(child["population"]) for child in children)
            weighted = lambda key: sum(int(child["population"]) * child[key] for child in children) / population
            p = weighted("probabilityOfWhite")
            lo = sum(int(child["population"]) * child["probabilityInterval"][0] for child in children) / population
            hi = sum(int(child["population"]) * child["probabilityInterval"][1] for child in children) / population
            hierarchy.append({"depth": level, "modulus": 2**level, "residue": residue,
                              "epochRegion": time_region, "addressRegion": address_region,
                              "population": str(population), "trainingCount": sum(child["trainingCount"] for child in children),
                              **_coefficient(p, lo, hi)})
    p = sum(int(leaf["population"]) * leaf["probabilityOfWhite"] for leaf in output_leaves) / total_population
    lo = sum(int(leaf["population"]) * leaf["probabilityInterval"][0] for leaf in output_leaves) / total_population
    hi = sum(int(leaf["population"]) * leaf["probabilityInterval"][1] for leaf in output_leaves) / total_population
    variance_proxy = sum((int(leaf["population"]) / total_population)**2 / leaf["trainingCount"]
                         for leaf in output_leaves if not leaf["exhaustiveTraining"])
    global_radius = math.sqrt(math.log(2 / alpha_global) * variance_proxy / 2)
    tight_interval = [max(0., p - global_radius), min(1., p + global_radius)]
    holdout_covered_population = sum(int(leaf["population"]) for leaf in output_leaves if leaf["holdoutCount"])
    holdout_weight = holdout_covered_population / total_population
    descriptor = {"schema": "XOR-KNOWN-FIELD-DESCRIPTOR/1", "fieldSeed": field_seed,
                  "initialPatternByte": initial_pattern_byte, "workerCount": str(worker_count),
                  "epochStart": config["epoch_start"], "epochEndInclusive": config["epoch_end"],
                  "maskGenerator": "known_word(known_word(fieldSeed XOR 0xA511E9B3, epoch), floor(bitId/32))",
                  "baseGenerator": "bit(initialPatternByte, bitId mod 8)",
                  "equation": "result(bitId,epoch) = baseBit XOR bit(maskGenerator, bitId mod 32)",
                  "scope": "Exact queryable known-equation descriptor, checked against selected live RAM/CPU/GPU results. Unsampled bits are computable from the equation; they are not statistically recovered, measured, scanned or materialized by this export."}
    return {"schema": SCHEMA, "config": config,
            "domain": {"workerCount": str(worker_count), "committedEpochCount": str(committed_epochs),
                       "epochStart": config["epoch_start"], "epochEndInclusive": config["epoch_end"],
                       "populationPoints": str(total_population), "fieldSeed": field_seed,
                       "field": "one committed binary XOR result for each (epoch, worker address)"},
            "leaves": output_leaves, "hierarchy": hierarchy, "samples": recorded,
            "globalEstimate": {**_coefficient(p, tight_interval[0], tight_interval[1]),
                               "populationWeighting": "exact finite stratum sizes",
                               "propagatedLeafInterval": [lo, hi], "stratifiedHoeffdingRadius": global_radius,
                               "stratifiedVarianceProxy": variance_proxy,
                               "intervalKind": "separate weighted stratified Hoeffding bound with allocated global failure probability"},
            "validation": {"trainingPairs": sum(leaf["trainingCount"] for leaf in output_leaves),
                           "holdoutPairs": sum(leaf["holdoutCount"] for leaf in output_leaves),
                           "disjointTrainingAndHoldout": True,
                           "holdoutCoveredPopulation": str(holdout_covered_population),
                           "holdoutBrierScore": total_brier / holdout_weight if holdout_weight else None,
                           "flatHalfProbabilityBrierScore": total_flat_brier / holdout_weight if holdout_weight else None,
                           "weightedHoldoutProbabilityAbsoluteError": total_absolute / holdout_weight if holdout_weight else None,
                           "holdoutBinaryAccuracy": total_accuracy / holdout_weight if holdout_weight else None,
                           "binaryPredictionRule": "predict white if fitted probability > .5, otherwise black; ties select black",
                           "holdoutScope": "Disjoint uniform holdout from each stratum's remaining population. Scores describe these actual holdout outcomes, not proof of all unsampled bits."},
            "confidence": {"requestedSimultaneousCoverage": config["confidence"], "terminalStrata": leaf_count,
                           "leafFamilyFailureProbability": alpha_leaf,
                           "globalFailureProbability": alpha_global,
                           "method": "finite-population Hoeffding with union bound for leaves and weighted propagation to hierarchy parents; separate weighted stratified global bound; split total failure probability equally",
                           "leafFormula": "epsilon = sqrt(log(2*K/alpha_leaf)/(2*n)); clipped to [0,1]; exact interval if n=N",
                           "globalFormula": "epsilon_global = sqrt(log(2/alpha_global)*sum_nonexhaustive((N_h/N)^2/n_h)/2); clipped to [0,1]",
                           "assumption": "The finite binary field is fixed. Sampling is uniform without replacement within each stratum, independently of that field, with independent ideal sampling draws across strata. A recorded independently generated sampling seed drives Python's reproducible PRNG; coverage uses that ideal uniform sampling-design assumption, not quantum randomness or independent field bits.",
                           "primarySource": "https://arxiv.org/abs/1309.4029",
                           "scope": "The combined guarantee covers all leaf means, weighted hierarchy parents and the global mean with at least the requested confidence under the design assumptions. The propagatedLeafInterval is also retained; none of these intervals recovers individual unsampled binary outputs."},
            "exactFieldDescriptor": descriptor,
            "interpretation": "2-adic refers to nested low-bit residue classes: i congruent to r modulo 2^d. The returned probability sheet is a piecewise finite table over address region, epoch region and residue. This is not a theorem of p-adic holographic recovery, an entangled quantum state, or a physical spin measurement.",
            "fullReturn": {"kind": "complete-queryable-probability-sheet-and-known-equation-descriptor",
                           "probabilitySheetCoversAllDomainPoints": True, "unsampledExactBitsRecovered": False,
                           "actualSampledDomainPoints": str(len(rows)),
                           "unsampledDomainPoints": str(total_population - len(rows)),
                           "allDomainPointsMaterialized": len(rows) == total_population}}


def build_probability_sheet(session, config=None):
    started = time.perf_counter()
    with session.lock:
        if session.running or session.phase != "idle" or not session.ram:
            raise RuntimeError("Pause an initialized session before reconstruction.")
        _check_reconstruction_active(session)
        worker_count = int(session.plan["domainBitWorkers"])
        committed_epochs = session.epochs
        normalized, leaves, rows = sampling_design(worker_count, committed_epochs, config)
        before = session.snapshot()
        entries = _read_committed_samples(session, rows)
        after = session.snapshot()
        sheet = reconstruct_values(worker_count, committed_epochs, session.config["seed"],
                                   session.plan["initialPatternByte"], normalized, leaves, rows, entries)
    for entry in sheet["samples"]:
        if session.cancel_event.is_set():
            raise ReconstructionCancelled("Reconstruction paused before publishing its probability sheet; completed sample calculations remain counted.")
        if exact_result(sheet["exactFieldDescriptor"], entry["bitId"], int(entry["epoch"])) != entry["result"]:
            raise RuntimeError("The exact known-field descriptor disagrees with an actual selected result.")
    sheet["measuredSampling"] = _counter_delta(before, after)
    sheet["measuredSampling"]["elapsedWallSeconds"] = time.perf_counter() - started
    sheet["measuredSampling"]["scope"] = "Actual live selected RAM reads and CPU/GPU XOR calculations plus sampling-design, statistics and report formatting time. Separate from shared-epoch throughput measurement; no domain-wide virtual credit."
    if sheet["measuredSampling"]["virtualXorCredit"] != "0":
        raise RuntimeError("Reconstruction queries earned unexpected virtual operation credit.")
    canonical = json.dumps(sheet["samples"], sort_keys=True, separators=(",", ":")).encode()
    sheet["sampleSha256"] = hashlib.sha256(canonical).hexdigest()
    if session.cancel_event.is_set():
        raise ReconstructionCancelled("Reconstruction paused before publishing its probability sheet; completed sample calculations remain counted.")
    return sheet

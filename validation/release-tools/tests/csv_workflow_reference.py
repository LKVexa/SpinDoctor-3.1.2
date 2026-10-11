"""Independent read-only check of one combined native CSV equations/circuits report."""
from __future__ import annotations
import argparse
import csv
import hashlib
import json
import re
from pathlib import Path

import numpy as np
from pypdf import PdfReader


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--example", type=Path, required=True)
    parser.add_argument("--source", type=Path, required=True)
    parser.add_argument("--report", type=Path, required=True)
    args = parser.parse_args()
    checks = 0

    def check(value: bool, label: str) -> None:
        nonlocal checks
        checks += 1
        if not value:
            raise AssertionError(label)

    def near(actual: float, expected: float, label: str, tolerance: float = 1e-10) -> None:
        check(bool(np.isfinite(actual)) and abs(actual - expected) <= tolerance, label)

    proof = json.loads((args.example / "combined-csv-workflow-proof.json").read_text(encoding="utf-8"))
    check(proof["passed"] and proof["version"] == "3.1.1-candidate", "Native combined example passed on the requested version")
    check(len(proof["fits"]) == 3 and len(proof["circuitRuns"]) == 2, "Combined report includes three actual fits and two actual circuit simulations")
    expected_hashes = []
    reference_fits = []
    for fit in proof["fits"]:
        original = (args.example / "inputs" / fit["input"]).read_bytes()
        digest = hashlib.sha256(original).hexdigest()
        expected_hashes.append(digest)
        result = fit["result"]
        check(digest == fit["sourceSha256"] == result["source"]["sha256"], "Native fit source matches exact copied original CSV bytes")
        records = list(csv.reader(original.decode("utf-8-sig").splitlines()))
        header, records = records[0], records[1:]
        target = result["target"]["columnIndex"]
        features = [item["columnIndex"] for item in result["features"]]
        check(header[target] == result["target"]["name"], "Fitted target matches explicitly selected original header")
        check([header[index] for index in features] == [item["name"] for item in result["features"]], "Fitted predictor order matches original selected columns")
        used_indexes, excluded, values = [], [], []
        for index, row in enumerate(records):
            try:
                numbers = [float(row[target]), *(float(row[column]) for column in features)]
                if not all(np.isfinite(value) and abs(value) <= 1e150 for value in numbers):
                    raise ValueError("numeric bounds")
            except ValueError:
                excluded.append(index)
            else:
                used_indexes.append(index)
                values.append(numbers)
        matrix = np.array(values, dtype=np.float64)
        y, x = matrix[:, 0], matrix[:, 1:]
        x_mean = x.mean(axis=0)
        scale = np.max(np.abs(x - x_mean), axis=0)
        design = np.column_stack((np.ones(len(x)), (x - x_mean) / scale))
        beta, _, rank, _ = np.linalg.lstsq(design, y - y.mean(), rcond=None)
        slopes = beta[1:] / scale
        intercept = y.mean() + beta[0] - slopes @ x_mean
        prediction = y.mean() + design @ beta
        residuals = y - prediction
        metrics = {
            "rSquared": 1 - np.sum(residuals ** 2) / np.sum((y - y.mean()) ** 2),
            "rmse": np.sqrt(np.mean(residuals ** 2)),
            "mae": np.mean(np.abs(residuals)),
            "residualSumSquares": np.sum(residuals ** 2),
        }
        check(rank == result["rank"] == len(features) + 1, "Independent SVD rank agrees with native pivoted QR")
        check(result["usedRows"] == len(values) == 24 and result["rowCount"] == len(records), "Independent complete-case row counts agree")
        check(result["excludedRows"] == len(excluded) and [item["rowIndex"] for item in result["excluded"]] == excluded, "Every actual excluded row agrees with independent source parsing")
        near(result["intercept"], float(intercept), "Independent fitted intercept")
        for index, slope in enumerate(slopes):
            near(result["coefficients"][index]["coefficient"], float(slope), "Independent fitted coefficient", 1e-12)
        for key, value in metrics.items():
            near(result["metrics"][key], float(value), "Independent " + key)
        check(result["metrics"]["trainingOnly"] and result["metrics"]["holdoutRows"] == 0, "No independent validation is fabricated")
        check(result["measurementShots"] == result["physicalQuantumOperations"] == 0, "Numeric fitting does not invent simulation or hardware work")
        check(result["dataRowsComplete"] and len(result["dataRows"]) == len(values), "All actual native fitted rows retained")
        for index, native in enumerate(result["dataRows"]):
            check(native["rowIndex"] == used_indexes[index], "Original data-row order retained")
            check(native["features"] == x[index].tolist(), "Actual predictor values preserved")
            near(native["observed"], float(y[index]), "Actual observed numeric value")
            near(native["fitted"], float(prediction[index]), "Independent predicted training-row value")
            near(native["residual"], float(residuals[index]), "Independent observed-minus-fitted residual")
        reference_fits.append({"input": fit["input"], "sourceSha256": digest, "intercept": float(intercept), "slopes": slopes.tolist(), "metrics": {key: float(value) for key, value in metrics.items()}})
    for index, simulation in enumerate(proof["circuitRuns"]):
        original = (args.example / "inputs" / simulation["input"]).read_bytes()
        digest = hashlib.sha256(original).hexdigest()
        expected_hashes.append(digest)
        check(digest == simulation["sourceSha256"] == simulation["sequence"]["sourceSha256"], "Circuit source provenance is authenticated to actual original CSV bytes")
        result = simulation["result"]
        parent_stem = "Rotated-CSV-Circuit-8-Shots" if index == 0 else "Bell-CSV-Circuit-1024-Shots"
        parent_archive = json.loads((args.example / "native-evidence" / (parent_stem + "-parent-native.json")).read_text(encoding="utf-8"))
        parent_job = parent_archive["response"]["result"]
        check(parent_archive["executionOutcome"] == "completed" and parent_archive["commandCompletedUtc"] == parent_job["completedUtc"], "Actual q.input run parent has a genuine matched native completion stamp")
        check(parent_job["phase"] == "completed" and parent_job["sourceSha256"] == digest, "Actual completed parent receipt retains original CSV source provenance")
        check(parent_job["receiptReady"] and not parent_job["verificationRequired"] and parent_job["verificationPhase"] == "not-requested", "Native return is available without fabricating independent sampled verification")
        check("final snapshot" in parent_archive["completionScope"] and "acceptance" not in parent_archive["completionScope"].lower(), "Completed input-run envelope states completion rather than stale acceptance scope")
        expected_shots = 8 if index == 0 else 1024
        check(result["phase"] == "completed" and not result["active"], "Actual circuit job completed")
        check(int(result["requestedShots"]) == int(result["completedShots"]) == expected_shots, "Actual circuit shots equal explicit CSV metadata")
        check(sum(int(row["count"]) for row in result["histogram"]) == expected_shots, "Histogram contains only the actual completed shots")
        spin_rows = result["coherentSpinPreview"]["rows"]
        if index == 0:
            near(spin_rows[0]["bloch"][0], 1, "RY(pi/2) native Bloch X agrees with analytic state", 1e-12)
            near(spin_rows[0]["bloch"][1], 0, "RY(pi/2) native Bloch Y agrees with analytic state", 1e-12)
            near(spin_rows[0]["bloch"][2], 0, "RY(pi/2) native Bloch Z agrees with analytic state", 1e-12)
            near(spin_rows[0]["purity"], 1, "RY(pi/2) actual local state purity", 1e-12)
            check(int(result["counts"]["executedCircuitInstructions"]) == 9, "Eight single-gate shots plus one coherent replay have nine actual gate instructions")
        else:
            check({row["bitstring"] for row in result["histogram"]} <= {"00", "11"}, "Bell samples only the correlated Born outcomes")
            check(int(result["counts"]["executedCircuitInstructions"]) == 2050, "Bell1024 shots plus retained coherent replay has2050 actual gate instructions")
            for row in spin_rows:
                check(all(abs(value) < 1e-12 for value in row["bloch"]), "Bell marginal has no preferred Bloch direction")
                near(row["purity"], .5, "Bell actual reduced state purity", 1e-12)
                check(row["mixed"] and not row["directionDefined"], "Bell local zero is labelled mixed/undefined rather than north-pole arrow")
    check(proof["workBeforeExport"] == proof["workAfterExport"], "Export adds no numerical fitting, shots, verification or quantum work")
    for key in ("actualCpuStates", "actualGpuStates", "verifiedSamples", "measurementShots", "quantumHardwareGates"):
        check(str(proof["workAfterExport"][key]) == "0", "Circuit/fit work is distinguished from procedural/physical counter: " + key)
    txt_raw = (args.example / proof["txt"]).read_bytes()
    txt = txt_raw.decode("utf-8")
    csv_raw = (args.example / proof["csv"]).read_bytes()
    pdf_raw = (args.example / proof["pdf"]).read_bytes()
    check(hashlib.sha256(txt_raw).hexdigest() == proof["txtSha256"], "Exact combined TXT hash")
    check(hashlib.sha256(csv_raw).hexdigest() == proof["csvSha256"], "Exact combined CSV hash")
    check(hashlib.sha256(pdf_raw).hexdigest() == proof["pdfSha256"], "Exact combined PDF hash")
    reader = PdfReader(args.example / proof["pdf"])
    pdf_text = "\n".join(page.extract_text() for page in reader.pages)
    compact = "".join(pdf_text.split())
    for digest in expected_hashes:
        check(digest in txt, "Combined TXT contains the actual original source SHA")
        check(digest in csv_raw.decode("utf-8"), "Combined CSV contains the actual original source SHA")
        check(digest in compact, "Combined PDF contains the actual original source SHA")
    for equation in proof["fits"]:
        check(equation["result"]["equation"] in txt, "Exact fitted equation preserved in combined TXT")
    check("observed" in pdf_text.lower() and "residual" in pdf_text.lower(), "PDF presents actual native fit graph semantics")
    check("bloch" in pdf_text.lower() and "purity" in pdf_text.lower(), "PDF presents actual coherent-state Bloch semantics")
    check("causality" in pdf_text.lower() and "holdout" in pdf_text.lower(), "PDF keeps empirical/validation limits explicit")
    with (args.example / proof["csv"]).open(encoding="utf-8", newline="") as handle:
        csv_rows = list(csv.DictReader(handle))
    check(all(len(row) == 9 for row in csv_rows), "Every RFC 4180 combined CSV row matches the documented schema")

    def csv_nodes(rows: list[dict[str, str]]) -> dict[str, dict[str, str]]:
        nodes = {row["path"]: row for row in rows if row["presence"] == "present"}
        check(len(nodes) == sum(row["presence"] == "present" for row in rows), "CSV has exactly one present value per native path")
        return nodes

    def exact_tree(nodes: dict[str, dict[str, str]], path: str, expected: object) -> None:
        check(path in nodes, "CSV retains actual archived native path " + path)
        node = nodes[path]
        if isinstance(expected, dict):
            check(node["value_type"] == "object" and int(node["item_count"]) == len(expected), "CSV object shape agrees with native archive " + path)
            for key, value in expected.items():
                exact_tree(nodes, path + "/" + key.replace("~", "~0").replace("/", "~1"), value)
        elif isinstance(expected, list):
            check(node["value_type"] == "array" and int(node["item_count"]) == len(expected), "CSV array shape agrees with native archive " + path)
            for index, value in enumerate(expected):
                exact_tree(nodes, path + "/" + str(index), value)
        else:
            actual = json.loads(node["value_json"])
            # JavaScriptSerializer and FP64 round-trip CSV can spell the same
            # double with different decimal lengths; compare parsed values.
            if isinstance(expected, float):
                near(actual, expected, "CSV preserves native FP64 value " + path, max(1e-14, abs(expected) * 1e-14))
            else:
                check(type(actual) is type(expected) and actual == expected, "CSV preserves exact scalar/null/zero " + path)

    combined_nodes = csv_nodes(csv_rows)
    equation_file = json.loads((args.example / "native-evidence" / "report-equations.json").read_text(encoding="utf-8"))
    check(equation_file == proof["equationRepresentations"], "Preserved native equation descriptor equals actual guarded export")
    check(equation_file["schema"] == "SPINDOCTOR/REPORT-EQUATIONS/1" and equation_file["count"] == 5 and equation_file["omittedRepresentations"] == 0, "All five native equation representations are returned without hidden omissions")
    exact_tree(combined_nodes, "/export/equationRepresentations", equation_file)
    for index, representation in enumerate(equation_file["representations"]):
        original = ([*proof["fits"], *proof["circuitRuns"]])[index]
        check(representation["sourceSha256"] == original["sourceSha256"] and representation["nativeArchiveSha256"] == original["archiveSha256"], "Editable/native mathematical descriptor preserves exact original source and archive binding")
        check("".join(representation["mathNotation"].split()) in "".join(txt.split()), "TXT preserves conventional native mathematical notation")
        result = original["result"]
        if index < 3:
            check(representation["kind"] == "empirical-fit" and representation["featureCount"] == len(result["features"]), "Empirical equation is distinguished from declared circuit evolution")
            near(representation["intercept"], result["intercept"], "Mathematical equation uses actual native fitted intercept")
            check(result["equation"] in representation["equationText"], "Editable equation text retains native input-column equation")
            for feature, variable, coefficient in zip(result["features"], representation["variables"], result["coefficients"]):
                check(variable["column"] == feature["name"] and variable["unit"] == coefficient["unit"], "Conventional variable mapping preserves actual input columns and units")
                near(variable["coefficient"], coefficient["coefficient"], "Conventional coefficient uses actual native fit value")
            check("in-sample" in representation["scope"] and "no circuit" in representation["scope"], "Empirical math display does not infer a gate/circuit from numeric measurements")
        else:
            check(representation["kind"] == "declared-circuit" and representation["programHash"] == result["programHash"], "Circuit mathematical display binds authentic retained program")
            check(str(representation["initialBasis"]) == str(result["initialBasis"]) and int(representation["qubits"]) == int(result["qubits"]), "Conventional circuit uses declared initial basis and register width")
            gates = result["programSnapshot"]["gates"]
            check(representation["operatorRepresentationsComplete"] and representation["gateCount"] == len(gates), "Every actual fixture gate has an operator representation")
            initial_ket = format(int(result["initialBasis"]), "0" + str(result["qubits"]) + "b")
            check(representation["computationalBasisKet"] == initial_ket, "Conventional basis ket preserves actual register width and rightmost qubit0")
            evolution = representation["equationText"].splitlines()[0]
            math_product = "".join(representation["mathNotation"].split()).replace("\\,", "").replace("\\;", "")
            if len(gates) == 1:
                check(evolution == "|psi_out> = U[1] |" + initial_ket + ">" and "U_1" in math_product and "\\cdots" not in math_product, "One-gate evolution contains exactly U1 without a repeated endpoint/ellipsis")
            elif len(gates) == 2:
                check(evolution == "|psi_out> = U[2] U[1] |" + initial_ket + ">" and "U_2U_1" in math_product and "\\cdots" not in math_product, "Two-gate evolution is exactly U2 U1 without an ellipsis")
            for gate_index, (operator, gate) in enumerate(zip(representation["gateOperators"], gates)):
                check(operator["index"] == gate_index + 1 and operator["gate"] == gate["name"] and operator["qubits"] == gate["qubits"] and operator["all"] == gate["all"] and operator["angles"] == gate["angles"], "Conventional operators retain chronological native operand/control order and angle values")
            check("first row acts first" in representation["equationText"] and "rightmost" in representation["equationText"], "Native mathematical representation states chronological product/bit-order convention")
            check("B_Y = tensor(H*S_dagger)" in representation["equationText"], "Non-Z Born equation names correct actual Y measurement rotation")
            check("partial_trace" in representation["equationText"] and "Entangled local states" in representation["equationText"], "Bloch math is reduced-density based and permits a legitimate entangled zero")
            if index == 3:
                check(representation["gateOperators"][0]["matrix"]["rows"] == [["cos(a1/2)", "-sin(a1/2)"], ["sin(a1/2)", "cos(a1/2)"]], "RY symbolic local matrix has correct rotation sign")
            else:
                check(representation["gateOperators"][0]["matrix"]["prefactor"] == "1/sqrt(2)", "H symbolic matrix has correct normalization")
                check(representation["gateOperators"][1]["matrix"]["rows"] == [["1", "0", "0", "0"], ["0", "0", "0", "1"], ["0", "0", "1", "0"], ["0", "1", "0", "0"]], "CX symbolic matrix matches native local least-significant control bit")
    stems = ["Weather-Equation", "Gate-Measurement-Equation", "Circuit-Measurement-Equation", "Rotated-CSV-Circuit-8-Shots", "Bell-CSV-Circuit-1024-Shots"]
    for index, item in enumerate([*proof["fits"], *proof["circuitRuns"]]):
        archive_bytes = (args.example / "native-evidence" / (stems[index] + "-native.json")).read_bytes()
        archive = json.loads(archive_bytes)
        archive_hash = hashlib.sha256(archive_bytes).hexdigest()
        check(archive_hash == item["archiveSha256"], "Actual preserved immutable command archive SHA agrees")
        record_path = "/native/records/" + str(index)
        check(json.loads(combined_nodes[record_path + "/nativeArchiveSha256"]["value_json"]) == archive_hash, "Combined CSV resolves actual immutable archive SHA")
        check(archive_hash in compact, "Combined PDF publishes matching immutable archive SHA")
        final = archive["response"] if index < 3 else archive["finalResponse"]
        check(final["result"] == item["result"], "Proof data equals the independently reread native archive")
        response_path = record_path + ("/nativeResponse" if index < 3 else "/finalNativeResponse")
        exact_tree(combined_nodes, response_path, final)
        completion = archive["commandCompletedUtc"] if index < 3 else archive["completedUtc"]
        check(completion in txt and completion in compact, "Matching actual native completion UTC appears in TXT and PDF")
        check(json.loads(combined_nodes[record_path + ("/commandCompletedUtc" if index < 3 else "/completedUtc")]["value_json"]) == completion, "Matching actual completion UTC appears in CSV")
        individual = args.example / (stems[index] + ".csv")
        with individual.open(encoding="utf-8", newline="") as handle:
            individual_nodes = csv_nodes(list(csv.DictReader(handle)))
        actual_path = "/native/commandReport" if index < 3 else "/native/commandReports/" + str(item["nativeArchive"]["index"]) + "/commandReport"
        exact_tree(individual_nodes, actual_path, archive)
        if index < 3:
            check("".join(item["result"]["equation"].split()) in compact, "PDF fitted equation exactly matches native equation")
            for metric in ("rSquared", "rmse", "mae", "residualSumSquares"):
                displayed = [float(value) for value in re.findall('"' + metric + '":([-+0-9.eE]+)', compact)]
                check(any(abs(value - item["result"]["metrics"][metric]) < 1e-12 for value in displayed), "PDF exact native answer includes fitted metric " + metric)
    for name, digest in proof["sourceHashes"].items():
        check(hashlib.sha256((args.source / "quantum" / name).read_bytes()).hexdigest() == digest, "Final native source binding: " + name)
    for pair in proof["perReportFiles"]:
        check(hashlib.sha256((args.example / pair["txt"]).read_bytes()).hexdigest() == pair["txtSha256"], "Individual TXT native report hash")
        check(hashlib.sha256((args.example / pair["csv"]).read_bytes()).hexdigest() == pair["csvSha256"], "Individual CSV native report hash")
    output = {"passed": True, "checks": checks, "sourceHashes": proof["sourceHashes"], "pdfSha256": proof["pdfSha256"], "pdfPages": len(reader.pages), "txtSha256": proof["txtSha256"], "csvSha256": proof["csvSha256"], "inputSourceHashes": expected_hashes, "independentFits": reference_fits,
              "scope": "Read-only independent NumPy/LAPACK SVD least-squares checks against actual uploaded synthetic example rows; analytic one-qubit/Bell expectations; every exported native response field/container compared against preserved immutable archives; matching PDF/TXT/CSV equation, metrics, completion UTC and source/archive hashes; native equation text/LaTeX coefficient mappings, chronology/control order and symbolic RY/H/CX matrices. No new native fit, simulation, stress or GPU work."}
    args.report.write_text(json.dumps(output, indent=2) + "\n", encoding="utf-8")
    print(f"PASS {checks} independent combined CSV fit/circuit/export assertions; {len(reader.pages)} PDF pages")


if __name__ == "__main__":
    main()

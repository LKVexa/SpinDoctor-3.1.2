"""Read-only, independent numerical and report checks for a native CSV-fit example."""
from __future__ import annotations

import argparse
import csv
import hashlib
import json
from pathlib import Path

import numpy as np
from pypdf import PdfReader


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--example", type=Path, required=True)
    parser.add_argument("--input", type=Path, required=True)
    parser.add_argument("--source", type=Path, required=True)
    parser.add_argument("--report", type=Path, required=True)
    args = parser.parse_args()
    checks = 0

    def check(condition: bool, label: str) -> None:
        nonlocal checks
        checks += 1
        if not condition:
            raise AssertionError(label)

    def near(actual: float, expected: float, label: str, tolerance: float = 1e-10) -> None:
        check(bool(np.isfinite(actual)) and abs(actual - expected) <= tolerance, label)

    proof = json.loads((args.example / "csv-input-proof.json").read_text(encoding="utf-8"))
    result = proof["nativeFit"]
    original = args.input.read_bytes()
    source_sha = hashlib.sha256(original).hexdigest()
    check(source_sha == result["source"]["sha256"], "Fit source is the exact example CSV, including original line endings")
    check(proof["passed"] and proof["checks"] >= 123, "Native input/numerical/archive tests passed")
    rows = list(csv.reader(original.decode("utf-8-sig").splitlines()))
    names, rows = rows[0], rows[1:]
    target = result["target"]["columnIndex"]
    features = [value["columnIndex"] for value in result["features"]]
    check(names[target] == result["target"]["name"], "Explicit selected target matches actual CSV header")
    check([names[index] for index in features] == [value["name"] for value in result["features"]], "Explicit selected predictor order matches actual CSV headers")
    indexes, values, excluded = [], [], []
    for index, row in enumerate(rows):
        try:
            numeric = [float(row[target]), *(float(row[column]) for column in features)]
            if not all(np.isfinite(value) and abs(value) <= 1e150 for value in numeric):
                raise ValueError("invalid finite magnitude")
        except ValueError:
            excluded.append(index)
        else:
            indexes.append(index)
            values.append(numeric)
    matrix = np.asarray(values, dtype=np.float64)
    y, x = matrix[:, 0], matrix[:, 1:]
    means = x.mean(axis=0)
    scales = np.max(np.abs(x - means), axis=0)
    design = np.column_stack([np.ones(len(x)), (x - means) / scales])
    # NumPy/LAPACK SVD least squares is independent of the native Householder QR.
    beta, _, rank, singular = np.linalg.lstsq(design, y - y.mean(), rcond=None)
    coefficients = beta[1:] / scales
    intercept = y.mean() + beta[0] - np.dot(coefficients, means)
    predicted = y.mean() + design @ beta
    residuals = y - predicted
    rmse = np.sqrt(np.mean(residuals * residuals))
    mae = np.mean(np.abs(residuals))
    r_squared = 1 - np.sum(residuals * residuals) / np.sum((y - y.mean()) ** 2)
    check(rank == result["rank"] == 3, "Independent SVD finds the native full rank")
    check(result["usedRows"] == len(values) == 24 and result["rowCount"] == len(rows) == 26, "Exact complete-case fit row counts")
    check(result["excludedRows"] == len(excluded) == 2, "Exact exclusion count")
    check([row["rowIndex"] for row in result["excluded"]] == excluded, "Every actual excluded row is preserved")
    near(result["intercept"], float(intercept), "Native intercept matches independent SVD")
    for index, coefficient in enumerate(coefficients):
        near(result["coefficients"][index]["coefficient"], float(coefficient), f"Native slope {index} matches independent SVD", 1e-12)
    near(result["metrics"]["rmse"], float(rmse), "Native RMSE matches independent actual residuals")
    near(result["metrics"]["mae"], float(mae), "Native MAE matches independent actual residuals")
    near(result["metrics"]["rSquared"], float(r_squared), "Native R squared matches independent actual target variation")
    near(result["metrics"]["residualSumSquares"], float(np.sum(residuals * residuals)), "Native RSS matches independent residuals")
    check(result["metrics"]["trainingOnly"] and result["metrics"]["holdoutRows"] == 0, "No independent holdout is fabricated")
    check(result["measurementShots"] == result["physicalQuantumOperations"] == 0, "Classical fit does not create quantum or measurement credits")
    check(result["dataRowsComplete"] and len(result["dataRows"]) == len(values), "Every native fitted row is retained")
    for index, native in enumerate(result["dataRows"]):
        check(native["rowIndex"] == indexes[index], "Native rows keep the original data record indexes")
        check(native["features"] == x[index].tolist(), "Native predictor values equal actual uploaded CSV values")
        near(native["observed"], float(y[index]), "Native observed value equals actual uploaded CSV target")
        near(native["fitted"], float(predicted[index]), "Native fitted value matches independent SVD")
        near(native["residual"], float(residuals[index]), "Native residual matches independent observed-minus-fitted")
    archive_path = args.example / "native-weather-fit-archive.json"
    archive_raw = archive_path.read_bytes()
    archive = json.loads(archive_raw)
    check(archive["response"]["result"] == result, "Saved immutable archive equals the actual native fit result")
    check(archive["executionOutcome"] == "completed" and bool(archive["commandCompletedUtc"]), "Actual synchronous fit has a native completion stamp")
    txt = (args.example / "SpinDoctor-3.1.1-Weather-Equation-Example.txt").read_text(encoding="utf-8")
    check(source_sha in txt and result["equation"] in txt and archive["commandCompletedUtc"] in txt, "TXT includes the actual source, equation and completion time")
    csv_path = args.example / "SpinDoctor-3.1.1-Weather-Equation-Example.csv"
    with csv_path.open(encoding="utf-8", newline="") as handle:
        exported = list(csv.DictReader(handle))
    leaves = {row["path"]: row for row in exported if row["presence"] == "present"}
    check(all(len(row) == 9 for row in exported), "RFC 4180 exported CSV has a consistent documented schema")
    check(json.loads(leaves["/export/sourceArchiveSha256"]["value_json"]) == hashlib.sha256(archive_raw).hexdigest(), "CSV is pinned to the same immutable native archive")
    result_prefix = "/archive/response/result"
    if result_prefix + "/equation" not in leaves:
        matches = [path for path in leaves if path.endswith("/response/result/equation")]
        check(len(matches) == 1, "Exactly one archived fitted equation is exported")
        result_prefix = matches[0][:-len("/equation")]
    check(json.loads(leaves[result_prefix + "/equation"]["value_json"]) == result["equation"], "CSV fitted equation equals the TXT/native value")
    check(json.loads(leaves[result_prefix + "/source/sha256"]["value_json"]) == source_sha, "CSV original source SHA equals TXT/native value")
    for index, row in enumerate(result["dataRows"]):
        for key in ("rowIndex", "observed", "fitted", "residual"):
            check(json.loads(leaves[f"{result_prefix}/dataRows/{index}/{key}"]["value_json"]) == row[key], "CSV preserves exact native fitted row leaf")
    pdf = args.example / "SpinDoctor-3.1.1-Weather-Equation-Example.pdf"
    pdf_raw = pdf.read_bytes()
    check(hashlib.sha256(pdf_raw).hexdigest() == proof["pdfSha256"], "PDF is the exact generated report pinned in the native proof")
    reader = PdfReader(pdf)
    text = "\n".join(page.extract_text() for page in reader.pages)
    compact = "".join(text.split())
    check(source_sha in compact and archive["commandCompletedUtc"] in compact, "PDF includes the same actual source SHA and completion time")
    check("observed" in text.lower() and "residual" in text.lower(), "PDF includes actual observed/fitted and residual reporting")
    check("causality" in text.lower() and "holdout" in text.lower(), "PDF explicitly qualifies empirical fit and validation limits")
    for name, digest in proof["sourceHashes"].items():
        check(hashlib.sha256((args.source / "quantum" / name).read_bytes()).hexdigest() == digest, "Example source binding: " + name)
    output = {
        "passed": True,
        "checks": checks,
        "method": "Read-only independent NumPy/LAPACK SVD least squares against actual example input rows",
        "sourceSha256": source_sha,
        "nativeSourceHashes": proof["sourceHashes"],
        "nativeArchiveSha256": hashlib.sha256(archive_raw).hexdigest(),
        "pdfSha256": proof["pdfSha256"],
        "pdfPages": len(reader.pages),
        "txtSha256": hashlib.sha256((args.example / "SpinDoctor-3.1.1-Weather-Equation-Example.txt").read_bytes()).hexdigest(),
        "csvSha256": hashlib.sha256(csv_path.read_bytes()).hexdigest(),
        "usedRows": len(values),
        "excludedRows": len(excluded),
        "independentCoefficients": coefficients.tolist(),
        "independentIntercept": float(intercept),
        "independentRmse": float(rmse),
        "independentMae": float(mae),
        "independentRSquared": float(r_squared),
        "scope": "No native calculation was rerun by this independent read-only check. Synthetic example data are not observed weather, not causal proof and not a validated forecast.",
    }
    args.report.write_text(json.dumps(output, indent=2) + "\n", encoding="utf-8")
    print(f"PASS {checks} independent CSV fit/numeric/PDF/TXT/CSV archive assertions; {len(reader.pages)} PDF pages")


if __name__ == "__main__":
    main()

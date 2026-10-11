#!/usr/bin/env python3
"""Compare smoke_chug.ps1 result JSON(s) with EXPECTED.csv (stdlib only).
Usage: python check_expected.py EXPECTED.csv results1.json [results2.json ...] [--alias old=new ...]
Later result files override earlier ones for the same folder."""
import csv, json, sys
args = sys.argv[1:]; alias = {}
if "--alias" in args:
    i = args.index("--alias"); alias = dict(a.split("=", 1) for a in args[i + 1:]); args = args[:i]
exp = list(csv.DictReader(open(args[0], encoding="utf-8-sig")))
res = {}
for p in args[1:]:
    for r in json.load(open(p, encoding="utf-8-sig"))["results"]:
        res[alias.get(r["folder"], r["folder"])] = r
ok = bad = 0; lines = []
by = {}
for e in exp: by.setdefault(e["folder"], []).append(e)
for folder, rows in by.items():
    r = res.get(folder)
    if r is None: lines.append("NOT RUN   " + folder); continue
    files = {f["filename"]: f for f in (r.get("files") or [])}
    job = "rejected at q.folder run" if not r["runOk"] else r["phase"]
    errtext = (r.get("runError") or "") + " " + (r.get("error") or "")
    for e in rows:
        problems = []
        ej = e["expected_job"]
        if not job.startswith(ej.split(" (")[0]) and not ej.startswith(job): problems.append("job %s != %s" % (job, ej))
        if e["file"] != "*":
            f = files.get(e["file"]); ef = e["expected_file"]
            got = f["phase"] if f else ("skipped (not enumerated)" if ef.startswith("skipped") and r["runOk"] else "not attempted")
            if ef.startswith("skipped") and f: problems.append("decoy was enumerated")
            elif not ef.startswith("skipped") and got != ef.split(" ")[0] and not (ef == "not attempted" and f is None): problems.append("file %s != %s" % (got, ef))
            m = e["expected_message"]
            if m.startswith("run order index"):
                if not f or f["index"] != int(m.split()[-1]): problems.append("order index %s != %s" % (f and f["index"], m))
            elif m and m not in ((f or {}).get("error", "") + " " + errtext): problems.append("message missing: " + m[:70])
        elif e["expected_message"] and e["expected_message"] not in errtext: problems.append("message missing: " + e["expected_message"][:70])
        if problems: bad += 1; lines.append("MISMATCH  %s/%s: %s" % (folder, e["file"], "; ".join(problems)))
        else: ok += 1
for l in lines: print(l)
print("rows matched: %d, mismatched: %d, folders not run: %d" % (ok, bad, sum(1 for l in lines if l.startswith("NOT RUN"))))

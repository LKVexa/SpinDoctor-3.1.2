import json, math, pathlib, sys
import refsim

root = pathlib.Path(sys.argv[1])
output = pathlib.Path(sys.argv[2])
rows = [json.loads(line) for line in (root/'run_log.jsonl').read_text(encoding='utf-8-sig').splitlines()]
checks = []
def response(path, marker):
    lines = path.read_text(encoding='utf-8').splitlines()
    for at, line in enumerate(lines):
        if line == marker and at + 1 < len(lines):
            return json.loads(lines[at+1])
for preset in rows:
    for step in preset.get('steps', []):
        if not step['command'].startswith('q.circuit run'):
            continue
        data = response(root/(preset['preset']+'_steps')/('%02d.txt' % step['index']), 'COMPLETE MATCHED ASYNCHRONOUS COMPLETION RESPONSE:')
        if not data or not data.get('ok'):
            raise RuntimeError('Missing completed circuit response')
        result = data['result']
        program = result['programSnapshot']
        n, basis, shots = int(result['qubits']), result['basis'], int(result['completedShots'])
        hist = {h['bitstring']: int(h['count']) for h in result['histogram']}
        if sum(hist.values()) != shots or not result['histogramComplete']:
            raise RuntimeError('Incomplete histogram')
        state = refsim.run(n, int(program['initialBasis']), program['gates'])
        dist = refsim.distribution(n, state, basis) if state is not None else None
        item = {'preset': preset['preset'], 'step': step['index'], 'qubits': n, 'basis': basis, 'shots': shots}
        if dist is None:
            # Large uniform supports cannot be materialized by this sparse oracle.
            z = max(abs(sum(c for k,c in hist.items() if k[n-1-q]=='1')/shots-0.5)/math.sqrt(0.25/shots) for q in range(n))
            even = sum(c for k,c in hist.items() if k.count('1')%2==0)
            if preset['preset'] == 'qc-238' and even != shots:
                raise RuntimeError('GHZ X parity violated')
            item.update(method='large-support marginals and GHZ parity', maxZ=z, evenCount=even)
        else:
            impossible = {k:c for k,c in hist.items() if dist.get(k,0)<1e-12}
            if impossible:
                raise RuntimeError(f'Impossible outcomes: {item} {impossible}')
            z = 0
            for q in range(n):
                expected = sum(p for k,p in dist.items() if k[n-1-q]=='1')
                observed = sum(c for k,c in hist.items() if k[n-1-q]=='1')/shots
                if 0 < expected < 1:
                    z = max(z, abs(observed-expected)/math.sqrt(expected*(1-expected)/shots))
            item.update(method='independent exact sparse reference', impossibleOutcomes=0, maxMarginalZ=z,
                        tvd=0.5*sum(abs(hist.get(k,0)/shots-dist.get(k,0)) for k in hist.keys()|dist.keys()))
        checks.append(item)
report = {'histograms': len(checks), 'exactReferenceChecks': sum(c['method'].startswith('independent') for c in checks),
          'largeSupportChecks': sum(c['method'].startswith('large') for c in checks), 'checks': checks}
output.write_text(json.dumps(report, indent=2), encoding='utf-8')
print(json.dumps({k:v for k,v in report.items() if k!='checks'}, indent=2))

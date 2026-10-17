import sys, os, re, json, hashlib
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from pdfcheck import pdf_info
run = sys.argv[1]; MAN = sys.argv[2]
presets = {p['id']: p for p in json.load(open(MAN, encoding='utf-8'))['presets']}
def sha(p): return hashlib.sha256(open(p,'rb').read()).hexdigest()
def norm(s): return re.sub(r'\s+', '', s)
def nodig(s): return re.sub(r'[0-9.]', '', norm(s))
def jline(lines, marker):
    for i,l in enumerate(lines):
        if l.strip()==marker and i+1 < len(lines):
            try: return json.loads(lines[i+1])
            except Exception: return 'unparsable'
    return None
out = []
for line in open(os.path.join(run,'run_log.jsonl'), encoding='utf-8-sig'):
    e = json.loads(line); pid = e['preset']; issues = []; calc = []
    cmd = e['command']; rid = e.get('reportId')
    t, p = os.path.join(run, pid+'.txt'), os.path.join(run, pid+'.pdf')
    rec = {'preset': pid, 'label': presets[pid]['label'], 'availability': presets[pid]['availability'], 'command': cmd, 'ok': e.get('ok'), 'jobPhase': e.get('jobPhase'), 'durationSec': e.get('durationSec'), 'timedOut': e.get('timedOut'), 'exception': e.get('exception'), 'stepCount': e.get('stepCount', 0)}
    if e.get('exception'): issues.append('runner exception: '+e['exception'][:200])
    for f in (t,p):
        if not os.path.exists(f) or os.path.getsize(f)==0: issues.append('missing/empty '+os.path.basename(f))
    if os.path.exists(t) and os.path.getsize(t):
        tx = open(t, encoding='utf-8').read()
        if ('Command: '+cmd) not in tx: issues.append('txt lacks command line')
        if rid and ('Report ID: '+rid) not in tx: issues.append('txt report id mismatch')
        rec['txtBytes'] = os.path.getsize(t); rec['txtSha256'] = sha(t)
    if os.path.exists(p) and os.path.getsize(p):
        info, ptx = pdf_info(p); rec['pdfBytes']=info['bytes']; rec['pdfPages']=info['pages']; rec['pdfSha256']=sha(p)
        if not (info['header'] and info['eof'] and info['startxref']): issues.append('pdf structure invalid')
        if info['pages'] < 1: issues.append('pdf has no pages')
        if norm(cmd) not in norm(ptx): issues.append('pdf text lacks command')
        if rid and rid not in norm(ptx): issues.append('pdf text lacks report id')
    if e.get('jobPhase') not in (None, 'completed'): calc.append('sequence phase '+str(e.get('jobPhase'))+(': '+str(e.get('jobError')) if e.get('jobError') else ''))
    if e.get('ok') is False: calc.append('preset command rejected: '+str(e.get('error')))
    steps = []
    sd = os.path.join(run, pid+'_steps')
    for s in e.get('steps') or []:
        ix = s['index']; st, sp = os.path.join(sd, '%02d.txt'%ix), os.path.join(sd, '%02d.pdf'%ix); si = []
        if s.get('reportError'): si.append('report error: '+s['reportError'])
        for f in (st, sp):
            if not os.path.exists(f) or os.path.getsize(f)==0: si.append('missing/empty step '+os.path.basename(f))
        acc = fin = None
        if os.path.exists(st) and os.path.getsize(st):
            lines = open(st, encoding='utf-8').read().splitlines()
            if not any(l.startswith('Original command: '+s['command']) for l in lines): si.append('step txt lacks command')
            acc = jline(lines, 'COMPLETE ACCEPTED NATIVE RESPONSE:'); fin = jline(lines, 'COMPLETE MATCHED ASYNCHRONOUS COMPLETION RESPONSE:')
        if os.path.exists(sp) and os.path.getsize(sp):
            info, ptx = pdf_info(sp)
            if not (info['header'] and info['eof'] and info['pages']>=1): si.append('step pdf invalid')
            if norm(s['command'])[:60] not in norm(ptx) and nodig(s['command'])[:60] not in nodig(ptx): si.append('step pdf text lacks command')
        okacc = acc.get('ok') if isinstance(acc, dict) else None
        okfin = fin.get('ok') if isinstance(fin, dict) else None
        err = None
        if okacc is False: err = (acc.get('error') or '')
        elif okfin is False: err = 'async: ' + str((fin.get('error') or (fin.get('result') or {}).get('error') or (fin.get('result') or {}).get('phase')))
        if err is not None: calc.append('step %d `%s` failed: %s' % (ix, s['command'], err[:240]))
        steps.append({'index': ix, 'command': s['command'], 'acceptedOk': okacc, 'completedOk': okfin, 'issues': si})
        issues += ['step %d: %s'%(ix,x) for x in si]
    rec['steps'] = steps; rec['reportIssues'] = issues; rec['calcIssues'] = calc
    rec['reportStatus'] = 'PASS' if not issues else 'FAIL'
    rec['calcStatus'] = 'OK' if not calc else 'ERROR'
    out.append(rec)
json.dump(out, open(os.path.join(run,'verify.json'),'w',encoding='utf-8'), indent=1)
print('presets', len(out), 'reportPASS', sum(r['reportStatus']=='PASS' for r in out), 'calcOK', sum(r['calcStatus']=='OK' for r in out), 'steps', sum(len(r['steps']) for r in out))
for r in out:
    if r['reportIssues'] or r['calcIssues']: print(r['preset'], r['reportIssues'][:3], r['calcIssues'][:3])



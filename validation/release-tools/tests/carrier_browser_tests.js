'use strict';
const fs = require('fs'), path = require('path'), vm = require('vm'), assert = require('assert');
const root = process.argv[2];
if (!root) throw new Error('Usage: node carrier_browser_tests.js <source/mvp>');
let checks = 0, scripts = 0;
function check(fn, name) { fn(); checks++; console.log('PASS ' + name); }
for (const name of fs.readdirSync(path.join(root, 'runtime')).filter(x => x.endsWith('.js'))) {
  new vm.Script(fs.readFileSync(path.join(root, 'runtime', name), 'utf8'), { filename: name }); scripts++;
}
for (const name of fs.readdirSync(root).filter(x => x.endsWith('.html'))) {
  const html = fs.readFileSync(path.join(root, name), 'utf8');
  for (const [index, match] of [...html.matchAll(/<script\b([^>]*)>([\s\S]*?)<\/script>/gi)].entries()) {
    if (/\bsrc\s*=/.test(match[1]) || /type=["']application\/json/i.test(match[1])) continue;
    new vm.Script(match[2], { filename: name + '#' + index }); scripts++;
  }
}
check(() => assert(scripts > 0), 'all runtime and embedded page scripts compile (' + scripts + ')');
const Rates = require(path.join(root, 'runtime', 'rate-metrics.js'));
const base = { sequentialEpochsPerSecondMedian: 1000 };
check(() => assert(Rates.calculate(base).consistent), 'measured epoch rate is consistent');
check(() => assert(!Rates.calculate({...base, physicalScalarFlopDiagnostic: { measured: true, flopsPerSecond: 1, countedFlops: 1, elapsedSeconds: 0 }}).consistent), 'zero elapsed diagnostic cannot pass consistency');
check(() => assert(!Rates.calculate({...base, physicalScalarFlopDiagnostic: { measured: true, flopsPerSecond: 1, countedFlops: 1e300, elapsedSeconds: 1e-300 }}).consistent), 'overflowed diagnostic cannot pass consistency');
check(() => assert(Rates.calculate({...base, physicalScalarFlopDiagnostic: { measured: true, flopsPerSecond: 1000, countedFlops: 10, elapsedSeconds: .01 }}).consistent), 'valid scalar diagnostic remains consistent');
const Ledger = require(path.join(root, 'runtime', 'flop-ledger.js'));
const hash = '0'.repeat(64);
check(() => assert.throws(() => Ledger.evaluateLane({wordSha256: hash, epoch: 0, lane: '1'.repeat(100000)})), 'oversized ledger integer rejected');
check(() => { const q = Ledger.evaluateLane({wordSha256: hash, epoch: 0, lane: 0}); assert.equal(q.operations.length, 2); }, 'legitimate ledger query retained');
console.log('Browser audit checks=' + checks);

/* Exact logical-operation accounting for the TQL-1 represented lane domain.
 * Only the sampled multiply/add pairs are materialized. Domain coverage is
 * a symbolic, exact integer partition, not evidence of full physical execution.
 */
(function (root, factory) {
  'use strict';
  var api = factory();
  if (typeof module === 'object' && module.exports) module.exports = api;
  else root.FlopLedger = api;
}(typeof globalThis !== 'undefined' ? globalThis : this, function () {
  'use strict';
  var VERSION = '1.0.0';
  var SCHEMA = 'QUETTAFLOP/FRAME-LEDGER/1';
  var LANES = 5000000000000000000000000n;
  var OPERATIONS = LANES * 2n;
  var MAX_CELLS = 65536;
  var MASK32 = 0xffffffffn;

  function object(value, allowed) {
    if (!value || typeof value !== 'object' || Array.isArray(value)) throw new TypeError('Parameters must be an object.');
    Object.keys(value).forEach(function (key) {
      if (allowed.indexOf(key) < 0) throw new TypeError('Unknown parameter: ' + key);
    });
    return value;
  }
  function decimal(value, name) {
    if (typeof value === 'bigint') {
      if (value < 0n || value >= (1n << 256n)) throw new RangeError(name + ' must fit an unsigned 256-bit integer.');
      return value;
    }
    if (typeof value === 'number') {
      if (!Number.isSafeInteger(value) || value < 0) throw new RangeError(name + ' must be an exact nonnegative integer.');
      return BigInt(value);
    }
    if (typeof value !== 'string' || value.length > 78 || !/^(0|[1-9][0-9]*)$/.test(value)) throw new TypeError(name + ' must be a bounded decimal integer.');
    var result = BigInt(value);
    if (result >= (1n << 256n)) throw new RangeError(name + ' exceeds 256 bits.');
    return result;
  }
  function epochValue(value) {
    var epoch = decimal(value, 'epoch');
    if (epoch > MASK32) throw new RangeError('epoch must be between 0 and 4294967295.');
    return epoch;
  }
  function hashValue(value, name) {
    if (typeof value !== 'string' || !/^[0-9a-f]{64}$/i.test(value)) throw new TypeError(name + ' must contain exactly 64 hexadecimal characters.');
    return value.toLowerCase();
  }
  function words(hex) {
    var result = [];
    for (var i = 0; i < 32; i += 4) {
      var offset = i * 2;
      result.push((parseInt(hex.slice(offset, offset + 2), 16) |
        parseInt(hex.slice(offset + 2, offset + 4), 16) << 8 |
        parseInt(hex.slice(offset + 4, offset + 6), 16) << 16 |
        parseInt(hex.slice(offset + 6, offset + 8), 16) << 24) >>> 0);
    }
    return result;
  }
  function rol32(x, n) { return ((x << n) | (x >>> (32 - n))) >>> 0; }
  function mix32(x) {
    x >>>= 0; x ^= x >>> 16; x = Math.imul(x, 0x7feb352d) >>> 0;
    x ^= x >>> 15; x = Math.imul(x, 0x846ca68b) >>> 0;
    x ^= x >>> 16; return x >>> 0;
  }
  function seedState(seed) {
    var input = words(seed), a = 0xa5a5a5a5, b = 0x3c6ef372, c = 0xbb67ae85;
    for (var i = 0; i < input.length; i++) {
      a = mix32(a ^ input[i] ^ Math.imul(i + 1, 0x9e3779b1));
      b = mix32(b ^ rol32(input[i], 7) ^ a);
      c = mix32(c ^ rol32(input[i], 17) ^ b);
    }
    return [a, b, c];
  }
  function sampleOffset(state, index, count) {
    var a = mix32(state[0] ^ index ^ 0x9e3779b9);
    var b = mix32(state[1] ^ rol32(index, 11) ^ a ^ 0x85ebca6b);
    var c = mix32(state[2] ^ rol32(index, 19) ^ b ^ 0xc2b2ae35);
    return ((BigInt(a) << 64n) | (BigInt(b) << 32n) | BigInt(c)) % count;
  }
  function traceLane(salt, epoch, lane) {
    var e = Number(epoch), l0 = Number(lane & MASK32), l1 = Number((lane >> 32n) & MASK32), l2 = Number((lane >> 64n) & MASK32);
    var h0 = mix32(l0 ^ rol32(l1, 7) ^ rol32(l2, 13) ^ e ^ salt[0]);
    var h1 = mix32(l1 ^ rol32(l2, 11) ^ rol32(l0, 17) ^ Math.imul(e, 0x9e3779b1) ^ salt[1]);
    var h2 = mix32(l2 ^ rol32(l0, 5) ^ rol32(l1, 19) ^ Math.imul(e, 0x85ebca6b) ^ salt[2]);
    var a = (h0 & 2047) - 1024, b = (h1 & 2047) - 1024, c = (h2 & 2047) - 1024;
    // Integer values stay below 2^24, so these two separate scalar operations
    // have the same exact values in the existing C# model and FP32 arithmetic.
    var product = a * b;
    var result = product + c;
    return {
      epoch: epoch.toString(), lane: lane.toString(), a: a, b: b, c: c,
      product: product, result: result, digit: ((result % 10) + 10) % 10,
      operations: [
        { id: (lane * 2n).toString(), kind: 'multiply', left: a, right: b, result: product },
        { id: (lane * 2n + 1n).toString(), kind: 'add', left: product, right: c, result: result }
      ]
    };
  }
  function evaluateLane(params) {
    object(params, ['wordSha256', 'epoch', 'lane']);
    var word = hashValue(params.wordSha256, 'wordSha256'), epoch = epochValue(params.epoch), lane = decimal(params.lane, 'lane');
    if (lane >= LANES) throw new RangeError('lane is outside the represented domain.');
    return traceLane(words(word), epoch, lane);
  }
  function queryOperation(params) {
    object(params, ['wordSha256', 'epoch', 'operationId']);
    var id = decimal(params.operationId, 'operationId');
    if (id >= OPERATIONS) throw new RangeError('operationId is outside the represented domain.');
    var trace = evaluateLane({ wordSha256: params.wordSha256, epoch: params.epoch, lane: id / 2n });
    return { epoch: trace.epoch, lane: trace.lane, operation: trace.operations[Number(id % 2n)], trace: trace };
  }
  function createFrame(params) {
    object(params, ['columns', 'rows', 'epoch', 'sessionSeed', 'wordSha256', 'start', 'end']);
    var columns = params.columns, rows = params.rows;
    if (!Number.isSafeInteger(columns) || !Number.isSafeInteger(rows) || columns < 1 || rows < 1 || !Number.isSafeInteger(columns * rows) || columns * rows > MAX_CELLS) {
      throw new RangeError('columns and rows must be positive safe integers, with at most ' + MAX_CELLS + ' cells.');
    }
    var cells = columns * rows, epoch = epochValue(params.epoch);
    var word = hashValue(params.wordSha256, 'wordSha256'), seed = hashValue(params.sessionSeed, 'sessionSeed');
    var start = params.start === undefined ? 0n : decimal(params.start, 'start');
    var end = params.end === undefined ? LANES : decimal(params.end, 'end');
    if (start >= end || end > LANES) throw new RangeError('Range must satisfy 0 <= start < end <= domain lanes.');
    var span = end - start, count = BigInt(cells);
    if (span < count) throw new RangeError('The lane range must contain at least one lane per cell.');
    var salt = words(word), random = seedState(seed), entries = [];
    for (var index = 0; index < cells; index++) {
      var lower = start + BigInt(index) * span / count;
      var upper = start + BigInt(index + 1) * span / count;
      var size = upper - lower, sampled = lower + sampleOffset(random, index, size);
      var trace = traceLane(salt, epoch, sampled);
      entries.push({
        index: index, column: index % columns, row: Math.floor(index / columns),
        laneStart: lower.toString(), laneEndExclusive: upper.toString(), laneCount: size.toString(),
        operationStart: (lower * 2n).toString(), operationEndExclusive: (upper * 2n).toString(),
        representedOperations: (size * 2n).toString(), sampledLane: sampled.toString(),
        a: trace.a, b: trace.b, c: trace.c, product: trace.product, result: trace.result,
        digit: trace.digit, operations: trace.operations
      });
    }
    return {
      schema: SCHEMA, version: VERSION, columns: columns, rows: rows, cells: cells,
      epoch: epoch.toString(), sessionSeed: seed, wordSha256: word, start: start.toString(), end: end.toString(),
      domain: { lanes: LANES.toString(), operations: OPERATIONS.toString() },
      accounting: {
        representedLanes: span.toString(), representedOperations: (span * 2n).toString(),
        sampledLanes: count.toString(), sampledOperationEvaluations: (count * 2n).toString(),
        unmaterializedOperations: ((span - count) * 2n).toString(),
        fullDomainCoverage: start === 0n && end === LANES,
        range: { laneStart: start.toString(), laneEndExclusive: end.toString(), operationStart: (start * 2n).toString(), operationEndExclusive: (end * 2n).toString() },
        domainCoverage: 'symbolic exact partition; only sampled operation pairs are materialized',
        operationModel: 'two canonical logical operation equivalents per lane; no full physical execution claim',
        sampleMethod: 'session-seed cell-index mix32-96-modulo/v1'
      },
      entries: entries
    };
  }
  function verifyFrame(frame) {
    var errors = [], expected;
    try {
      if (!frame || typeof frame !== 'object' || Array.isArray(frame)) throw new TypeError('Frame must be an object.');
      expected = createFrame({ columns: frame.columns, rows: frame.rows, epoch: frame.epoch, sessionSeed: frame.sessionSeed, wordSha256: frame.wordSha256, start: frame.start, end: frame.end });
    } catch (error) {
      return { ok: false, errors: [error.message], modelOperationEvaluations: '0' };
    }
    function compare(actual, wanted, path) {
      if (errors.length >= 50) return;
      if (wanted === null || typeof wanted !== 'object') {
        if (actual !== wanted) errors.push(path + ' does not match regenerated ledger.');
        return;
      }
      if (!actual || typeof actual !== 'object' || Array.isArray(actual) !== Array.isArray(wanted)) {
        errors.push(path + ' has invalid structure.'); return;
      }
      if (Array.isArray(wanted) && actual.length !== wanted.length) errors.push(path + ' has an incorrect length.');
      var actualKeys = Object.keys(actual), keys = Object.keys(wanted);
      if (actualKeys.length !== keys.length || actualKeys.some(function (key) { return !Object.prototype.hasOwnProperty.call(wanted, key); })) errors.push(path + ' has unexpected or missing fields.');
      keys.forEach(function (key) { compare(actual[key], wanted[key], path + '.' + key); });
    }
    compare(frame, expected, 'frame');
    return { ok: errors.length === 0, errors: errors, modelOperationEvaluations: (BigInt(expected.cells) * 2n).toString() };
  }
  return { version: VERSION, schema: SCHEMA, domainLanes: LANES.toString(), domainOperations: OPERATIONS.toString(), maxCells: MAX_CELLS, createFrame: createFrame, evaluateLane: evaluateLane, queryOperation: queryOperation, verifyFrame: verifyFrame };
}));

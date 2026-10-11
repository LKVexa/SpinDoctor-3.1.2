/* Single-digit tiles for the simulator's Canvas 2D output.
 * A frame samples one digit per cell and copies cached bitmaps.
 * No text layout, font rasterization, or tile allocation occurs per frame.
 */
(function (root, factory) {
  'use strict';
  var api = factory();
  if (typeof module === 'object' && module.exports) module.exports = api;
  else root.NumberRaster = api;
}(typeof globalThis !== 'undefined' ? globalThis : this, function () {
  'use strict';

  var CELL_WIDTH = 12;
  var CELL_HEIGHT = 18;
  var GLYPHS = [
    ['01110', '10001', '10011', '10101', '11001', '10001', '01110'],
    ['00100', '01100', '00100', '00100', '00100', '00100', '01110'],
    ['01110', '10001', '00001', '00010', '00100', '01000', '11111'],
    ['11110', '00001', '00001', '01110', '00001', '00001', '11110'],
    ['00010', '00110', '01010', '10010', '11111', '00010', '00010'],
    ['11111', '10000', '10000', '11110', '00001', '00001', '11110'],
    ['01110', '10000', '10000', '11110', '10001', '10001', '01110'],
    ['11111', '00001', '00010', '00100', '01000', '01000', '01000'],
    ['01110', '10001', '10001', '01110', '10001', '10001', '01110'],
    ['01110', '10001', '10001', '01111', '00001', '00001', '01110']
  ];

  function packedColor(r, g, b) {
    var bytes = new Uint8ClampedArray([r, g, b, 255]);
    return new Uint32Array(bytes.buffer)[0];
  }

  var BACKGROUND = packedColor(7, 16, 25);
  var INK = packedColor(89, 232, 248);
  var tileRows = null;
  var blankRows = null;

  function warmTiles() {
    if (tileRows) return;
    tileRows = new Array(10);
    var blank = new Uint32Array(CELL_WIDTH * CELL_HEIGHT);
    blank.fill(BACKGROUND);
    blankRows = new Array(CELL_HEIGHT);
    for (var blankRow = 0; blankRow < CELL_HEIGHT; blankRow++) {
      blankRows[blankRow] = blank.subarray(blankRow * CELL_WIDTH, (blankRow + 1) * CELL_WIDTH);
    }
    for (var value = 0; value < 10; value++) {
      var tile = new Uint32Array(CELL_WIDTH * CELL_HEIGHT);
      tile.fill(BACKGROUND);
      var glyph = GLYPHS[value];
      for (var y = 0; y < 7; y++) {
        for (var x = 0; x < 5; x++) {
          if (glyph[y].charAt(x) === '1') {
            var offset = (y * 2 + 2) * CELL_WIDTH + x * 2 + 1;
            tile[offset] = tile[offset + 1] = INK;
            tile[offset + CELL_WIDTH] = tile[offset + CELL_WIDTH + 1] = INK;
          }
        }
      }
      var rows = new Array(CELL_HEIGHT);
      for (var row = 0; row < CELL_HEIGHT; row++) {
        rows[row] = tile.subarray(row * CELL_WIDTH, (row + 1) * CELL_WIDTH);
      }
      tileRows[value] = rows;
    }
  }

  function digit(value) {
    value = Number(value);
    if (!Number.isFinite(value)) return 0;
    return Math.max(0, Math.min(9, Math.round(value)));
  }

  function create(canvas) {
    if (!canvas || typeof canvas.getContext !== 'function') {
      throw new TypeError('NumberRaster.create requires a canvas.');
    }
    var context = canvas.getContext('2d', { alpha: false });
    if (!context) throw new Error('A Canvas 2D context is required.');
    warmTiles();
    // Progressive previews alternate two resolutions; retain both buffers.
    var frames = [];
    var age = 0;
    var renderer = { lastStats: null, render: render };

    function render(nextWidth, nextHeight, sample) {
      nextWidth = Math.floor(Number(nextWidth));
      nextHeight = Math.floor(Number(nextHeight));
      if (!Number.isFinite(nextWidth) || !Number.isFinite(nextHeight) || nextWidth < 1 || nextHeight < 1) {
        throw new RangeError('NumberRaster dimensions must be positive finite numbers.');
      }
      if (typeof sample !== 'function') throw new TypeError('NumberRaster requires a digit sampling function.');
      var columns = Math.floor(nextWidth / CELL_WIDTH);
      var rows = Math.floor(nextHeight / CELL_HEIGHT);
      var active = null;
      for (var i = 0; i < frames.length; i++) {
        if (frames[i].width === nextWidth && frames[i].height === nextHeight) {
          active = frames[i];
          break;
        }
      }
      if (canvas.width !== nextWidth) canvas.width = nextWidth;
      if (canvas.height !== nextHeight) canvas.height = nextHeight;
      if (!active) {
        var newFrame = context.createImageData(nextWidth, nextHeight);
        var newPixels = new Uint32Array(newFrame.data.buffer, newFrame.data.byteOffset, nextWidth * nextHeight);
        newPixels.fill(BACKGROUND);
        active = {
          width: nextWidth,
          height: nextHeight,
          frame: newFrame,
          pixels: newPixels,
          age: 0,
          stats: { columns: columns, rows: rows, cells: columns * rows, width: nextWidth, height: nextHeight }
        };
        if (frames.length < 2) frames.push(active);
        else frames[frames[0].age <= frames[1].age ? 0 : 1] = active;
      }
      active.age = ++age;
      var pixels = active.pixels;
      var width = nextWidth;
      for (var row = 0; row < rows; row++) {
        var rowOffset = row * CELL_HEIGHT * width;
        for (var col = 0; col < columns; col++) {
          var sampled = sample(col, row, columns, rows);
          var cached = sampled === null ? blankRows : tileRows[digit(sampled)];
          var offset = rowOffset + col * CELL_WIDTH;
          for (var line = 0; line < CELL_HEIGHT; line++) {
            pixels.set(cached[line], offset + line * width);
          }
        }
      }
      renderer.lastStats = active.stats;
      context.putImageData(active.frame, 0, 0);
      return renderer.lastStats;
    }

    return renderer;
  }

  return {
    create: create,
    digit: digit,
    cellWidth: CELL_WIDTH,
    cellHeight: CELL_HEIGHT
  };
}));

// Run the C# suite first to generate its List.Sort reference ordering.
import assert from 'node:assert/strict';
import fs from 'node:fs';
import {sort, prepare, select, release} from '../WolfNET.Game/wwwroot/sprite-sort.js';

const cases = JSON.parse(fs.readFileSync('artifacts/performance/sprite-sort-oracle.json'));
for (const {distances, expected} of cases) {
    // Include unused capacity and guard words to verify that only active keys move.
    const keys = new Uint32Array((distances.length + 16) * 2), values = new Float32Array(keys.buffer);
    keys.fill(0xdeadbeef);
    for (let i = 0; i < distances.length; i++) { keys[i * 2] = i; values[i * 2 + 1] = distances[i]; }
    sort(keys, distances.length);
    assert.deepEqual(Array.from({length: distances.length}, (_, i) => keys[i * 2]), expected,
        `Browser painter order differs from .NET List.Sort for ${distances.length} keys`);
    assert(Array.from(keys.subarray(distances.length * 2)).every(word => word === 0xdeadbeef));
    // Reuse the same typed array, as the renderer does every frame.
    for (let i = 0; i < distances.length; i++) { keys[i * 2] = i; values[i * 2 + 1] = distances[i]; }
    sort(keys, distances.length);
    assert.deepEqual(Array.from({length: distances.length}, (_, i) => keys[i * 2]), expected);
}
sort(new Uint32Array(), 0);
const spatial = JSON.parse(fs.readFileSync('artifacts/performance/sprite-spatial-oracle.json'));
prepare(new Float32Array(spatial.positions), spatial.positions.length / 4);
for (const {x, y, directionX, directionY, widths, expected, visible} of spatial.views) {
    const overrides = new Uint32Array(widths.length * 2), values = new Float32Array(overrides.buffer);
    widths.forEach(([index, width], i) => { overrides[i * 2] = index; values[i * 2 + 1] = width; });
    const output = new Uint32Array((expected.length + 2) * 2);
    const fullCount = select(output, overrides, widths.length, x, y, 60, directionX, directionY, 1, false, false, 0, 0, 0);
    assert.deepEqual(Array.from({length: fullCount}, (_, i) => output[2 + i * 2]), expected, `Spatial painter order differs at ${x},${y}`);
    const count = select(output, overrides, widths.length, x, y, 60, directionX, directionY, 1, true, false, 0, 0, 0);
    assert.deepEqual(Array.from({length: count}, (_, i) => output[2 + i * 2]), visible, `Browser visibility/order differs at ${x},${y}`);
    assert(output[1] < spatial.positions.length / 12, 'Browser query examined most of Pine Loop');
}
const output = new Uint32Array(32), noWidths = new Uint32Array();
prepare(new Float32Array([4, 0, 1, 1, 8, 0, 1, 1]), 2);
assert.equal(select(output, noWidths, 0, 0, 0, 60, 1, 0, 1, true, false, 0, 0, 0), 2);
const candidates = output[1];
const expanded = new Float32Array(10_002 * 4);
expanded.set([4, 0, 1, 1, 8, 0, 1, 1]);
for (let i = 2; i < 10_002; i++) expanded.set([500 + i % 100, 500 + Math.floor(i / 100), 1, 1], i * 4);
prepare(expanded, 10_002);
assert.equal(select(output, noWidths, 0, 0, 0, 60, 1, 0, 1, true, false, 0, 0, 0), 2);
assert.equal(output[1], candidates, 'Distant scenery increased per-frame browser candidate work');
prepare(new Float32Array([62, 0, 1, 6]), 1);
assert.equal(select(output, noWidths, 0, 0, 0, 60, 1, 0, 1, true, false, 0, 0, 0), 0);
const wideOverride = new Uint32Array(2);
new Float32Array(wideOverride.buffer)[1] = 6;
assert.equal(select(output, wideOverride, 1, 0, 0, 60, 1, 0, 1, true, false, 0, 0, 0), 1, 'Wide atlas frame was culled at range boundary');
release(); prepare(new Float32Array(4), 0);
assert.equal(select(output, noWidths, 0, 0, 0, 60, 1, 0, 1, true, true, 3, 0, 1), 1);
assert.equal(select(output, noWidths, 0, 0, 0, 60, 1, 0, 1, true, true, -3, 0, 1), 0, 'Moving avatar used stale position');
assert.equal(select(output, noWidths, 0, 0, 0, 60, 1, 0, 1, true, true, 3, 0, 1, true, 5, 0, 1), 2);
assert.deepEqual([output[2], output[4]], [1, 0], 'Opponent and player were not sorted together');
assert.equal(select(output, noWidths, 0, 0, 0, 60, 1, 0, 1, true, false, 0, 0, 0, true, 5, 0, 1), 1);
assert.equal(output[2], 1, 'Cockpit view hid the opponent along with the player');
assert.equal(select(output, noWidths, 0, 0, 0, 60, 1, 0, 1, true, false, 0, 0, 0, true, -5, 0, 1), 0, 'Moving opponent used stale position');
assert.equal(select(output, noWidths, 0, 0, 0, 60, 1, 0, 1, true, false, 0, 0, 0, true, 70, 0, 1), 0, 'Opponent ignored render distance');
assert.equal(select(output, noWidths, 0, 0, 0, 60, 1, 0, 1, true,
    true, 3, 0, 1, true, 5, 0, 1, true, 7, 0, 1), 3);
assert.deepEqual([output[2], output[4], output[6]], [2, 1, 0], 'Three cars were not sorted together');
assert.equal(select(output, noWidths, 0, 0, 0, 60, 1, 0, 1, true,
    false, 0, 0, 0, true, 5, 0, 1, true, 7, 0, 1), 2, 'Cockpit view hid the green rival');
assert.equal(select(output, noWidths, 0, 0, 0, 60, 1, 0, 1, true,
    false, 0, 0, 0, false, 0, 0, 0, true, -7, 0, 1), 0, 'Green rival used stale position');
assert.equal(select(output, noWidths, 0, 0, 0, 60, 1, 0, 1, true,
    false, 0, 0, 0, false, 0, 0, 0, true, 70, 0, 1), 0, 'Green rival ignored render distance');
release();
console.log(`PASS: browser painter sort matches .NET for ${cases.length} fixtures, including equal distances, partition boundaries, reused buffers, and spare capacity.`);
console.log(`PASS: browser spatial ordering matches .NET at all ${spatial.views.length} Pine Loop viewpoints.`);

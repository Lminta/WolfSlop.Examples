// Run the game C# suite first to generate Pine Loop reference ordering.
import assert from 'node:assert/strict';
import fs from 'node:fs';
import {prepare, select as selectSprites, release} from '../Engine/WolfNET.Engine.Browser/wwwroot/sprite-sort.js';

function select(output, widths, count, x, y, range, dx, dy, plane, cull, ...slots) {
    const actors = [];
    for (let i = 0; i < slots.length; i += 4) {
        // Preserve slot indices so the existing painter-order fixtures remain useful.
        actors.push(slots[i] ? slots[i + 1] : 1e20, slots[i + 2] || 0, slots[i + 3] || 0, slots[i + 3] || 0);
    }
    return selectSprites(output, widths, count, x, y, range, dx, dy, plane, cull, new Float32Array(actors), actors.length / 4);
}

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
release();
console.log(`PASS: browser spatial ordering matches .NET at all ${spatial.views.length} Pine Loop viewpoints.`);

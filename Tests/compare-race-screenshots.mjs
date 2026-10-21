// Compare the frozen-animation screenshots from two race-performance runs.
// node Tests/compare-race-screenshots.mjs artifacts/performance/before.json artifacts/performance/after.json
import assert from 'node:assert/strict';
import fs from 'node:fs';
import {inflateSync} from 'node:zlib';

function decode(filename) {
    const png = fs.readFileSync(filename), chunks = [];
    let width, height, channels;
    for (let offset = 8; offset < png.length;) {
        const length = png.readUInt32BE(offset), type = png.toString('ascii', offset + 4, offset + 8);
        const data = png.subarray(offset + 8, offset + 8 + length);
        if (type === 'IHDR') {
            width = data.readUInt32BE(0); height = data.readUInt32BE(4);
            assert(data[8] === 8 && [2, 6].includes(data[9]) && data[12] === 0, 'Expected a noninterlaced 8-bit RGB/RGBA screenshot');
            channels = data[9] === 6 ? 4 : 3;
        }
        if (type === 'IDAT') chunks.push(data);
        offset += length + 12;
    }
    const raw = inflateSync(Buffer.concat(chunks)), stride = width * channels, pixels = Buffer.alloc(stride * height);
    for (let y = 0; y < height; y++) {
        const filter = raw[y * (stride + 1)];
        assert(filter <= 4);
        for (let x = 0; x < stride; x++) {
            const index = y * stride + x, left = x >= channels ? pixels[index - channels] : 0;
            const up = y ? pixels[index - stride] : 0, corner = y && x >= channels ? pixels[index - stride - channels] : 0;
            let predictor = 0;
            if (filter === 1) predictor = left;
            if (filter === 2) predictor = up;
            if (filter === 3) predictor = (left + up) >> 1;
            if (filter === 4) {
                const estimate = left + up - corner, a = Math.abs(estimate - left), b = Math.abs(estimate - up), c = Math.abs(estimate - corner);
                predictor = a <= b && a <= c ? left : b <= c ? up : corner;
            }
            pixels[index] = raw[y * (stride + 1) + 1 + x] + predictor;
        }
    }
    return {width, height, channels, pixels};
}

const before = process.argv[2], after = process.argv[3];
const views = JSON.parse(fs.readFileSync(after)).views;
let differences = 0;
for (const {name} of views) {
    const a = decode(before.replace(/\.json$/, '') + `-${name}.png`);
    const b = decode(after.replace(/\.json$/, '') + `-${name}.png`);
    assert(a.width === b.width && a.height === b.height);
    let changed = 0, maximum = 0;
    const bounds = [a.width, a.height, 0, 0];
    for (let y = 0; y < a.height; y++) for (let x = 0; x < a.width; x++) {
        // The FPS counter is expected to differ after an optimization.
        if (x < 120 && y > a.height - 45) continue;
        let delta = 0;
        for (let c = 0; c < 3; c++) delta = Math.max(delta, Math.abs(a.pixels[(y * a.width + x) * a.channels + c] - b.pixels[(y * b.width + x) * b.channels + c]));
        maximum = Math.max(maximum, delta);
        if (delta > 3) {
            changed++; bounds[0] = Math.min(bounds[0], x); bounds[1] = Math.min(bounds[1], y);
            bounds[2] = Math.max(bounds[2], x); bounds[3] = Math.max(bounds[3], y);
        }
    }
    differences += changed;
    console.log(`${name}: ${changed} changed pixels, maximum channel difference ${maximum}${changed ? ', bounds ' + bounds.join(',') : ''}`);
}
assert.equal(differences, 0, 'Scenery changed outside the FPS counter; inspect the reported image regions');

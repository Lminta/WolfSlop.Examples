// Physical scenery sizes for a 3-meter map block. Safe to apply repeatedly.
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

export const metersPerBlock = 3;
const directory = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../WolfNET.Examples/wwwroot/objects/raceway');
const treeHeights = new Map([
    [540, [12]], [541, [6]], [570, [12]], [571, [6]], [572, [14]], [573, [7]],
    [574, [11, 6]], [575, [12, 7]], [576, [13, 7]], [577, [11, 9, 13]],
    [578, [10]], [579, [12, 10, 14]],
]);
const round = value => Math.round(value * 1e6) / 1e6;

function size(member, widthMeters, heightMeters, baseline = 1) {
    member.scale = { x: round(widthMeters / metersPerBlock), y: round(heightMeters / metersPerBlock) };
    member.offset.z = round(-.5 + member.scale.y * (baseline - .5));
}

export function sizeRacewayObject(object) {
    if (object.id === 528) object.footprint = { length: 4.5 / metersPerBlock, width: 1.9 / metersPerBlock };
    if (object.type !== 'spriteGroup') return object;
    object.sprites.forEach((member, index) => {
        const id = member.spriteId;
        if ([520, 529, 530].includes(id)) {
            const height = treeHeights.get(object.id)?.[index] ?? 10;
            size(member, height * (id === 529 ? .85 : .55), height);
        } else if (id === 522) {
            size(member, index ? 1.5 : 1.8, index ? .9 : 1.2);
        } else if ([521, 531].includes(id)) {
            // Three stacked racing tires, about 0.8 m in diameter and 0.9 m tall.
            size(member, .81, .9);
        } else if ([523, 524].includes(id)) {
            size(member, 3, [546, 547].includes(object.id) || object.id >= 716 && object.id < 732 ? 3 : 2.4);
        } else if ([525, 526, 552, 553].includes(id)) {
            size(member, 3, .9);
        } else if ([527, 591].includes(id)) {
            // The top third of the image is transparent; the visible roof is about 5 m high.
            size(member, object.id === 548 ? 6 : 7.5, object.id === 548 ? 6 : 7.5);
        } else if (id === 528) {
            // Footprint supplies directional width; X is its multiplier, Y is body height.
            member.scale = { x: 1, y: 1.2 / metersPerBlock };
            member.offset.z = round((member.scale.y - 1) / 2 - .005);
        } else if (id === 609) {
            // Texture has padding: visible wooden barrel is approximately 0.64 x 0.9 m.
            size(member, .9, 1.06, 1200 / 1280);
        } else if ([630, 654, 655].includes(id)) {
            size(member, 3.6, 3.6, 496 / 512);
        } else if ([631, 658, 659].includes(id)) {
            // A 2.7 m frame gives standing adults ~1.8 m bodies, with room for raised arms.
            size(member, 2.7, 2.7, 496 / 512);
        } else if (id === 632) {
            size(member, 3, 3);
        } else if (id >= 633 && id <= 640) {
            size(member, 3, 2.4);
        }
    });
    return object;
}

export function sizeRacewayProps() {
    let changed = 0;
    for (const name of fs.readdirSync(directory).filter(name => name.endsWith('.json'))) {
        const filename = path.join(directory, name);
        const original = fs.readFileSync(filename, 'utf8');
        const object = JSON.parse(original);
        const before = JSON.stringify(object);
        sizeRacewayObject(object);
        if (JSON.stringify(object) !== before) {
            fs.writeFileSync(filename, JSON.stringify(object, null, 2) + '\n');
            changed++;
        }
    }
    return changed;
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
    console.log(`Sized ${sizeRacewayProps()} raceway definitions at ${metersPerBlock} meters per block.`);
}

// Small symmetric stacks arranged as reusable colliding trackside groups.
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { sizeRacewayObject } from './size-raceway-props.mjs';

const directory = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../WolfNET.Game/wwwroot/objects/raceway');
const stack = (spriteId, x, y, scale = .31) => ({ spriteId,
    offset: { x, y, z: (scale - 1) / 2 }, scale: { x: scale, y: scale } });
const row = (spriteId, axis, scale = .31) => [-.32, 0, .32].map(offset =>
    stack(spriteId, axis === 'x' ? offset : 0, axis === 'y' ? offset : 0, scale));

export function writeRacewayTireProps() {
    const group = (name, id, sprites) => fs.writeFileSync(path.join(directory, `${name}.json`),
        JSON.stringify(sizeRacewayObject({ type: 'spriteGroup', id, raycastTransparent: true, collides: true,
            jumpable: false, crouchable: false, passageId: 502, sprites }), null, 2) + '\n');
    group('group-tires', 543, [-.6, -.3, 0, .3, .6].map((x, i) => stack(i % 2 ? 531 : 521, x, 0)));
    group('group-tires-north-south', 580, [-.6, -.3, 0, .3, .6].map((y, i) => stack(i % 2 ? 531 : 521, 0, y)));
    group('group-tires-corner', 581, [-.3, 0, .3].flatMap((x, i) =>
        [stack(i % 2 ? 531 : 521, x, -.155), stack(i % 2 ? 521 : 531, x, .155)]));
    group('group-tires-elbow', 582, [stack(521, -.155, -.155), stack(531, .155, -.155), stack(521, -.155, .155)]);
    group('group-tire-black', 583, row(521, 'x', .35));
    group('group-tire-white', 584, row(531, 'x', .35));
    group('group-tires-double-row', 585, [-.58, -.29, 0, .29, .58].flatMap((x, i) =>
        [stack(i % 2 ? 531 : 521, x, -.145, .29), stack(i % 2 ? 521 : 531, x, .145, .29)]));
    group('group-safety-rail-corner-tires', 623, row(531, 'x'));
    group('group-safety-rail-black-tires', 624, row(521, 'x'));
    group('group-safety-rail-white-tires-y', 660, row(531, 'y'));
    group('group-safety-rail-black-tires-y', 661, row(521, 'y'));
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) writeRacewayTireProps();

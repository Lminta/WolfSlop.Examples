// Put boundary artwork on the track-facing edges of the colliding tiles.
// Collision remains tile-based; these are ordinary sprite-group presets.
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { sizeRacewayObject } from './size-raceway-props.mjs';

const directory = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../WolfSlop.Examples/wwwroot/objects/raceway');
const faces = [
    { bit: 1, name: 'west', dx: -1, dy: 0 },
    { bit: 2, name: 'east', dx: 1, dy: 0 },
    { bit: 4, name: 'north', dx: 0, dy: -1 },
    { bit: 8, name: 'south', dx: 0, dy: 1 },
];
const styles = [
    { name: 'fence', ids: [620, 621, 622], scale: 1.1 },
    { name: 'tall-fence', ids: [546, 547], scale: 1.3 },
    { name: 'white-tires', ids: [623, 660], spriteId: 531, scale: .31 },
    { name: 'black-tires', ids: [624, 661], spriteId: 521, scale: .31 },
];

export function alignRacewayBoundaries(grid, definitions, drivingArea) {
    const key = (x, y) => `${x},${y}`;
    const drivable = (x, y) => drivingArea.has(key(x, y)) &&
        definitions.get(grid[x]?.[y])?.collides === false;
    const variants = new Map();
    for (let x = 1; x < grid.length - 1; x++) for (let y = 1; y < grid[x].length - 1; y++) {
        const styleIndex = styles.findIndex(style => style.ids.includes(grid[x][y]));
        if (styleIndex < 0) continue;
        const style = styles[styleIndex];
        function facingMask(neighbor) {
            let mask = faces.reduce((mask, face) => neighbor(x + face.dx, y + face.dy) ? mask | face.bit : mask, 0);
            // The outer diagonal tile at a convex bend has no cardinal driving
            // neighbor. Close both faces meeting the drivable corner instead.
            if (!mask) for (const dx of [-1, 1]) for (const dy of [-1, 1]) {
                if (neighbor(x + dx, y + dy)) mask |= (dx < 0 ? 1 : 2) | (dy < 0 ? 4 : 8);
            }
            return mask;
        }
        // Some outer rail tiles sit behind the paddock's taller fence. Keep
        // those buried sections facing inward without adding caps along it.
        const mask = facingMask(drivable) || facingMask((x, y) => drivingArea.has(key(x, y))) ||
            facingMask((x, y) => definitions.get(grid[x]?.[y])?.collides === false);
        if (!mask) throw new Error(`Boundary at ${x},${y} has no track-facing edge`);
        const id = 700 + styleIndex * 16 + mask - 1;
        if (!variants.has(id)) {
            const visibleFaces = faces.filter(face => mask & face.bit);
            const sprites = visibleFaces.flatMap(face => (style.spriteId ? [-.32, 0, .32] : [0]).map(along => ({
                spriteId: style.spriteId ?? (face.dx ? 523 : 524),
                offset: { x: face.dx * .5 + (face.dy ? along : 0),
                    y: face.dy * .5 + (face.dx ? along : 0), z: (style.scale - 1) / 2 },
                scale: { x: style.scale, y: style.scale },
            })));
            const original = definitions.get(grid[x][y]);
            const definition = { type: 'spriteGroup', id, raycastTransparent: true, collides: true,
                jumpable: false, crouchable: false, passageId: original.passageId, sprites };
            sizeRacewayObject(definition);
            const filename = `group-boundary-${style.name}-${visibleFaces.map(face => face.name).join('-')}.json`;
            fs.writeFileSync(path.join(directory, filename), JSON.stringify(definition, null, 2) + '\n');
            definitions.set(id, definition);
            variants.set(id, definition);
        }
        grid[x][y] = id;
    }
    return variants;
}

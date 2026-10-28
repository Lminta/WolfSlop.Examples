// Rebuild only Pine Loop's authored map, scenery presets, lap fixture, and views.
// Run from the repository root: node Tools/build-pine-loop-map.mjs
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { writeRacewayTireProps } from './build-raceway-tire-props.mjs';
import { sizeRacewayObject, sizeRacewayProps } from './size-raceway-props.mjs';

// Preserve the previous compact course as an explicit authoring option.
// The default builds the requested256x512grand circuit.
if (!process.argv.includes('--legacy')) {
    await import('./build-pine-loop-grand-circuit.mjs');
    process.exit(0);
}

const repository = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const web = path.join(repository, 'WolfSlop.Examples/wwwroot');
const objectDirectory = path.join(web, 'objects/raceway');
const width = 80, height = 70;
const grid = Array.from({ length: width }, () => Array(height).fill(501));
const track = new Set();
const key = (x, y) => `${x},${y}`;
const ownedObjects = new Map();

function writeObject(name, id, type, fields) {
    const value = { type, id, raycastTransparent: true, collides: false,
        jumpable: false, crouchable: false, ...fields };
    sizeRacewayObject(value);
    fs.writeFileSync(path.join(objectDirectory, `${name}.json`), JSON.stringify(value, null, 2) + '\n');
    ownedObjects.set(id, value);
}
function sprite(name, id, texture, layer) {
    writeObject(name, id, 'sprite', { passageId: 501,
        texture: `textures/raceway/${texture}`, textureLayer: layer });
}
function member(spriteId, x, y, scale) {
    return { spriteId, offset: { x, y, z: (scale - 1) / 2 }, scale: { x: scale, y: scale } };
}
function group(name, id, sprites, passageId = 501) {
    writeObject(name, id, 'spriteGroup', { collides: true, passageId, sprites });
}

// Each generated tree and tire is a separate symmetric billboard. A group
// arranges several independently projected sprites at distinct world positions.
sprite('sprite-tree-oak', 529, 'tree-oak.png', 106);
sprite('sprite-tree-birch', 530, 'tree-birch.png', 107);
sprite('sprite-tires', 521, 'tire-stack.png', 101);
sprite('sprite-tires-white', 531, 'tire-stack-white.png', 108);
group('group-oak-tall', 570, [member(529, 0, 0, 2.5)]);
group('group-oak-young', 571, [member(529, 0, 0, 1.8)]);
group('group-birch-tall', 572, [member(530, 0, 0, 2.55)]);
group('group-birch-young', 573, [member(530, 0, 0, 1.85)]);
group('group-pine-grove', 574, [member(520, -.28, .24, 2.25), member(520, .3, -.23, 1.8)]);
group('group-oak-grove', 575, [member(529, -.3, -.2, 2.4), member(529, .31, .25, 1.7)]);
group('group-birch-grove', 576, [member(530, -.29, .23, 2.45), member(530, .32, -.25, 1.85)]);
group('group-mixed-grove', 577, [member(520, -.32, .28, 2.2), member(529, .29, .18, 1.85), member(530, .04, -.3, 2.45)]);
group('group-woodland-pine', 578, [member(520, -.12, .12, 2.1), member(522, .3, -.2, .7)]);
group('group-forest-edge', 579, [member(520, -.34, .25, 2.5), member(529, .3, .16, 2.05), member(530, .03, -.33, 2.6)]);

writeRacewayTireProps();
sizeRacewayProps();

writeObject('wall-hedge-boundary', 510, 'wall', { raycastTransparent: false,
    collides: true, passageId: 501,
    textures: Object.fromEntries(['north', 'east', 'south', 'west'].map(face =>
        [face, 'textures/raceway/forest-boundary.png'])) });

for (let x = 0; x < width; x++) grid[x][0] = grid[x][height - 1] = 510;
for (let y = 0; y < height; y++) grid[0][y] = grid[width - 1][y] = 510;

// Anchors mark the northwest corner of a 2x2 road stamp. Long straights
// expand distance, while bends retain the original lane width.
const anchors = [[12, 49], [12, 22], [13, 22], [13, 21], [14, 21], [14, 20],
    [15, 20], [15, 19], [16, 19], [16, 18], [17, 18], [17, 17], [18, 17],
    [18, 16], [19, 16], [19, 15], [20, 15], [20, 14], [21, 14], [21, 13],
    [22, 13], [22, 12], [50, 12], [50, 13], [51, 13], [51, 14], [52, 14],
    [52, 15], [53, 15], [53, 16], [54, 16], [54, 17], [55, 17], [55, 18],
    [56, 18], [56, 26], [57, 26], [57, 27], [58, 27], [58, 28], [59, 28],
    [59, 29], [60, 29], [60, 30], [61, 30], [61, 31], [62, 31], [62, 32],
    [63, 32], [63, 33], [64, 33], [64, 47], [63, 47], [63, 48], [62, 48],
    [62, 49], [61, 49], [61, 50], [60, 50], [60, 51], [59, 51], [59, 52],
    [58, 52], [58, 53], [57, 53], [57, 54], [56, 54], [56, 55], [55, 55],
    [55, 56], [24, 56], [24, 55], [23, 55], [23, 54], [22, 54], [22, 53],
    [21, 53], [21, 52], [20, 52], [20, 51], [19, 51], [19, 50], [18, 50],
    [18, 49], [12, 49]];
for (let i = 1; i < anchors.length; i++) {
    const [ax, ay] = anchors[i - 1], [bx, by] = anchors[i];
    if (ax !== bx && ay !== by) throw new Error('Road anchors must join orthogonally');
    for (let x = Math.min(ax, bx); x <= Math.max(ax, bx); x++)
        for (let y = Math.min(ay, by); y <= Math.max(ay, by); y++)
            for (const ox of [0, 1]) for (const oy of [0, 1]) track.add(key(x + ox, y + oy));
}
for (let y = 33; y <= 49; y++) track.add(key(14, y));
for (const point of track) {
    const [x, y] = point.split(',').map(Number);
    for (const [dx, dy] of [[1, 0], [-1, 0], [0, 1], [0, -1]])
        if (!track.has(key(x + dx, y + dy))) grid[x + dx][y + dy] = 502;
}
for (const point of track) { const [x, y] = point.split(',').map(Number); grid[x][y] = 500; }
// Generous dirt runoff at the major corner exits.
for (const [left, top, right, bottom] of [[12, 10, 22, 17], [53, 10, 61, 19],
    [64, 28, 69, 36], [59, 49, 68, 58], [13, 51, 24, 59]])
    for (let x = left; x <= right; x++) for (let y = top; y <= bottom; y++)
        if (grid[x][y] === 501) grid[x][y] = 502;

for (let x = 12; x <= 14; x++) grid[x][46] = 505;
for (const y of [36, 38, 40, 42, 44, 48]) for (let x = 12; x <= 14; x++) grid[x][y] = 506;
grid[14][33] = 508;

// Fourteen-tile stone tunnel across the northern straight.
for (let x = 33; x <= 46; x++) {
    for (const y of [12, 13]) grid[x][y] = 503;
    for (const y of [11, 14]) grid[x][y] = 511;
}
for (const x of [32, 47]) for (const y of [11, 14]) grid[x][y] = 512;

// The pit service spur has its own apron behind the starting-straight fence.
for (let x = 16; x <= 18; x++) for (let y = 31; y <= 51; y++) grid[x][y] = 500;
for (let y = 34; y <= 46; y++) grid[24][y] = 513;
for (const y of [34, 38, 42, 46]) for (let x = 19; x <= 24; x++) grid[x][y] = 513;
for (const [bay, first] of [[1, 35], [2, 39], [3, 43]]) {
    for (let x = 19; x <= 23; x++) for (let y = first; y < first + 3; y++) grid[x][y] = 504;
    grid[19][first] = 513 + bay; grid[19][first + 2] = 513; grid[22][first + 1] = 549;
}
for (let x = 7; x <= 10; x++) for (let y = 40; y <= 46; y++) grid[x][y] = 507;
grid[8][43] = 548;

function place(id, coordinates) {
    for (const [x, y] of coordinates)
        if ([501, 502, 507].includes(grid[x]?.[y])) grid[x][y] = id;
}
const lineX = (left, right, y) => Array.from({ length: right - left + 1 }, (_, i) => [left + i, y]);
const lineY = (x, top, bottom) => Array.from({ length: bottom - top + 1 }, (_, i) => [x, top + i]);
place(546, [...lineY(11, 26, 51), ...lineY(15, 34, 48), ...lineY(70, 29, 51), ...lineY(52, 25, 47)]);
place(547, [...lineX(22, 54, 9), ...lineX(24, 60, 60), ...lineX(26, 49, 17), ...lineX(28, 49, 52)]);

// Independent round tire stacks stay symmetric when viewed from any side.
place(580, [[10, 30], [10, 34], [10, 50], [54, 22], [54, 25], [68, 36], [68, 40], [68, 44], [68, 48]]);
place(543, [[25, 10], [29, 10], [49, 10], [28, 59], [34, 59], [41, 59], [48, 59], [53, 59]]);
place(581, [[12, 18], [14, 16], [16, 14], [18, 12], [20, 10], [55, 11], [57, 13], [59, 15],
    [59, 24], [61, 26], [63, 28], [65, 30], [67, 32], [66, 50], [64, 52], [62, 54],
    [60, 56], [58, 58], [15, 52], [17, 54], [19, 56], [21, 58]]);
place(582, [[17, 23], [21, 19], [50, 18], [53, 20], [59, 34], [61, 36], [61, 44], [56, 51], [24, 51]]);
place(585, [[7, 31], [73, 38], [56, 62]]);
place(583, [[14, 30], [25, 54], [60, 41], [31, 19]]);
place(584, [[14, 31], [26, 54], [60, 42], [32, 19]]);
// Buffer definitions are maintained separately; this script only places them.
place(544, [[14, 29], [14, 32], [55, 22], [61, 38], [66, 46]]);
place(545, [[22, 18], [53, 21], [62, 55], [55, 59], [24, 52]]);

// A fixed hash gives repeatable, staggered woodland without random rerolls.
function hash(x, y) { return ((Math.imul(x + 101, 73856093) ^ Math.imul(y + 67, 19349663)) >>> 0); }
function clearOfRoad(x, y, radius = 2) {
    for (let dx = -radius; dx <= radius; dx++) for (let dy = -radius; dy <= radius; dy++)
        if ([500, 503, 504, 505, 506, 508].includes(grid[x + dx]?.[y + dy])) return false;
    return true;
}
function scenicReserve(x, y) {
    if ((x >= 6 && x <= 11 && y >= 38 && y <= 48) ||
        (x >= 15 && x <= 27 && y >= 29 && y <= 49)) return true;
    // Groups can extend beyond their owning collision square.
    for (let dx = -1; dx <= 1; dx++) for (let dy = -1; dy <= 1; dy++)
        if ([543, 544, 545, 580, 581, 582, 583, 584, 585].includes(grid[x + dx]?.[y + dy])) return true;
    return false;
}
const treePresets = [540, 541, 570, 571, 572, 573, 574, 575, 576, 577, 578];
for (let x = 3; x < width - 3; x += 3) for (let y = 3; y < height - 3; y += 3) {
    const random = hash(x, y), tx = x + random % 2, ty = y + (random >>> 3) % 2;
    if (grid[tx][ty] === 501 && clearOfRoad(tx, ty) && !scenicReserve(tx, ty))
        grid[tx][ty] = treePresets[(random >>> 6) % treePresets.length];
}
// Two dense, mixed-species belts conceal the forest wall at the perimeter.
const forestBelt = [...lineX(2, width - 3, 2), ...lineX(2, width - 3, height - 3),
    ...lineY(2, 3, height - 4), ...lineY(width - 3, 3, height - 4),
    ...lineX(4, width - 5, 5), ...lineX(4, width - 5, height - 6),
    ...lineY(5, 6, height - 7), ...lineY(width - 6, 6, height - 7)];
for (const [x, y] of forestBelt) if (hash(x, y) % 4 === 0 && grid[x][y] === 501 && clearOfRoad(x, y)) grid[x][y] = 579;
for (let x = 3; x < width - 3; x += 2) for (let y = 3; y < height - 3; y += 2)
    if (grid[x][y] === 501 && hash(x, y) % 7 === 0 && clearOfRoad(x, y, 1) && !scenicReserve(x, y))
        grid[x][y] = (hash(x, y) >>> 3) % 2 ? 542 : 550;

// Include existing buffer definitions and every dependency in the map registry.
const objects = {};
for (const name of fs.readdirSync(objectDirectory).filter(name => name.endsWith('.json')).sort()) {
    const definition = JSON.parse(fs.readFileSync(path.join(objectDirectory, name), 'utf8'));
    objects[definition.id] = `../objects/raceway/${name}`;
    ownedObjects.set(definition.id, definition);
}
const map = { objects, worldMap: grid, spawn: { controller: 'car', x: 13.5, y: 47.5,
    directionX: 0, directionY: -1, moveSpeed: 3.2, rotSpeed: 3, collisionRadius: .15,
    jumpRise: .4, jumpLength: 2, jumpSpeedMultiplier: 1.25, crouchDrop: .25, crouchSpeedMultiplier: .5 },
    sky: { texture: 'textures/skyboxes/countryside_sky.jpg', rotationDegrees: 110 } };
const pretty = JSON.stringify(map, null, 2);
const before = pretty.indexOf('  "worldMap": ['), after = pretty.indexOf('  "spawn":', before);
const columns = '  "worldMap": [\n' + grid.map(column => '    [' + column.join(', ') + ']').join(',\n') + '\n  ],\n';
fs.writeFileSync(path.join(web, 'maps/pine-loop.json'), pretty.slice(0, before) + columns + pretty.slice(after) + '\n');

const fixture = { dimensions: [width, height], lap: anchors.map(([x, y]) => [x + 1, y + 1]),
    tunnel: { left: 33, right: 46, top: 12, bottom: 13 },
    pitEntrances: [[19, 36], [19, 40], [19, 44]] };
fs.mkdirSync(path.join(repository, 'Tests/Fixtures'), { recursive: true });
fs.writeFileSync(path.join(repository, 'Tests/Fixtures/pine-loop-lap.json'), JSON.stringify(fixture, null, 2) + '\n');
fs.mkdirSync(path.join(repository, 'artifacts'), { recursive: true });
fs.writeFileSync(path.join(repository, 'artifacts/pine-loop-views.json'), JSON.stringify([
    ['start', [13.5, 47.5, 0, -1]], ['narrowing', [13.5, 35.5, 0, -1]],
    ['tunnel-entry', [30.5, 13, 1, 0]], ['tunnel-interior', [39.5, 13, 1, 0]],
    ['pits', [17.5, 40.5, 1, 0]], ['grandstand', [13.5, 43.5, -1, 0]],
    ['east-s', [57, 25.5, 0, 1]], ['forest', [42, 57, 0, -1]],
    ['tires', [64.5, 44.5, 1, 0]], ['woodland', [29.5, 29.5, 1, 0]],
    ['buffer-x', [64.5, 36.5, -.83205, .5547]], ['buffer-y', [61.5, 52.5, .31623, .94868]],
    ['buffer-end', [14.5, 27.5, 0, 1]]
], null, 2) + '\n');

const countSprites = id => {
    const definition = ownedObjects.get(id);
    return definition?.type === 'spriteGroup' ? definition.sprites.length : definition?.type === 'sprite' ? 1 : 0;
};
const treeIds = new Set([520, 529, 530]);
let trees = 0, sprites = 0;
for (const column of grid) for (const id of column) {
    sprites += countSprites(id);
    const definition = ownedObjects.get(id);
    if (definition?.type === 'spriteGroup') trees += definition.sprites.filter(item => treeIds.has(item.spriteId)).length;
}
const lapLength = fixture.lap.slice(1).reduce((sum, [x, y], i) =>
    sum + Math.hypot(x - fixture.lap[i][0], y - fixture.lap[i][1]), 0);
console.log(`Pine Loop: ${width}x${height}, ${track.size} main-road cells, ${lapLength}-unit lap, ${trees} individual trees, ${sprites} sprites.`);

// Large-course authoring module used by build-pine-loop-map.mjs.
// Existing shared scenery JSON remains the source for trees, tires, and buffers.
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const repository = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const web = path.join(repository, 'WolfNET.Game/wwwroot');
const objectDirectory = path.join(web, 'objects/raceway');
const textureDirectory = path.join(web, 'textures/raceway');
const width = 256, height = 512;
const grid = Array.from({ length: width }, () => Array(height).fill(501));
const track = new Set();
const key = (x, y) => `${x},${y}`;
const definitions = new Map();

function object(name, id, type, fields) {
    const value = { type, id, raycastTransparent: true, collides: false,
        jumpable: false, crouchable: false, ...fields };
    fs.writeFileSync(path.join(objectDirectory, `${name}.json`), JSON.stringify(value, null, 2) + '\n');
    definitions.set(id, value);
}
const member = (spriteId, x, y, scale) => ({ spriteId,
    offset: { x, y, z: (scale - 1) / 2 }, scale: { x: scale, y: scale } });
const faces = texture => Object.fromEntries(['north', 'east', 'south', 'west'].map(face => [face, texture]));
function wall(name, id, texture, passageId = 501) {
    object(name, id, 'wall', { collides: true, raycastTransparent: false, passageId, textures: faces(texture) });
}
function passage(name, id, floorTexture, ceilingTexture = null) {
    object(name, id, 'passage', { floorTexture, ceilingMode: ceilingTexture ? 'texture' : 'sky',
        ...(ceilingTexture ? { ceilingTexture } : {}) });
}
object('group-grandstand-large-x', 590, 'spriteGroup', { collides: true, passageId: 501, sprites: [member(527, 0, 0, 6.4)] });
object('sprite-grandstand-y', 591, 'sprite', { passageId: 501, texture: 'textures/raceway/grandstand.png', textureLayer: 105, planeAxis: 'y' });
object('group-grandstand-large-y', 592, 'spriteGroup', { collides: true, passageId: 501, sprites: [member(591, 0, 0, 6.4)] });
passage('passage-spectator-plaza', 593, 'textures/glassworks/limestone-floor.png');
passage('passage-quarry', 594, 'textures/wolf/greystone.png');
passage('passage-village', 595, 'textures/wolf/wood.png', 'textures/wolf/wood.png');
wall('wall-village-wood', 596, 'textures/wolf/wood.png', 595);
wall('wall-yard-metal', 597, 'textures/glassworks/teal-wall.png', 504);
passage('passage-maintenance-yard', 615, 'textures/raceway/asphalt.png');
passage('passage-control-room', 618, 'textures/glassworks/checker-floor.png', 'textures/glassworks/plaster-ceiling.svg');
wall('wall-village-plaster', 617, 'textures/glassworks/limestone-wall.png', 595);
object('sprite-service-barrel', 609, 'sprite', { passageId: 501, texture: 'textures/wolf/barrel.png', textureLayer: 124 });
object('group-service-barrels', 608, 'spriteGroup', { collides: true, passageId: 615,
    sprites: [member(609, -.25, -.2, .6), member(609, .26, -.17, .6), member(609, .05, .26, .6)] });

const signDefinitions = [
    [598, 'paddock', 'PINE LOOP', 'PADDOCK', '#284a45'],
    [599, 'esses', 'TECHNICAL', 'ESSES', '#a57b36'],
    [600, 'quarry', 'STONE CUT', 'QUARRY TUNNEL', '#596866'],
    [601, 'village', 'MARSHAL', 'VILLAGE', '#76553a'],
    [602, 'plaza', 'SPECTATOR', 'PLAZA', '#425c72'],
    [603, 'yard', 'MAINTENANCE', 'YARD', '#536f73'],
    [604, 'ridge', 'NORTH RIDGE', 'FOREST RUN', '#365543'],
    [605, 'hairpins', 'WOODLAND', 'HAIRPINS', '#506343'],
    [606, 'south-loop', 'SOUTH LOOP', 'RETURN TO GRID', '#8b613c'],
    [616, 'control', 'RACE', 'CONTROL', '#234742'],
];
for (const [id, name, first, second, color] of signDefinitions) {
    const filename = `landmark-${name}.svg`;
    const image = `<svg xmlns="http://www.w3.org/2000/svg" width="512" height="512" viewBox="0 0 512 512"><rect width="512" height="512" fill="#c5c0a6"/><path d="M0 128H512M0 256H512M0 384H512" stroke="#aea98f" stroke-width="7"/><rect x="22" y="134" width="468" height="224" rx="8" fill="${color}" stroke="#e4dbb5" stroke-width="12"/><text x="256" y="226" text-anchor="middle" fill="#f3ecd6" font-family="Arial,sans-serif" font-weight="bold" font-size="45">${first}</text><text x="256" y="294" text-anchor="middle" fill="#f3ecd6" font-family="Arial,sans-serif" font-weight="bold" font-size="35">${second}</text><circle cx="48" cy="162" r="6" fill="#b3b5aa"/><circle cx="464" cy="330" r="6" fill="#b3b5aa"/></svg>\n`;
    fs.writeFileSync(path.join(textureDirectory, filename), image);
    wall(`wall-sign-${name}`, id, `textures/raceway/${filename}`);
}
for (const [id, color, name] of [[612, '#a4553e', 'red'], [613, '#375c70', 'blue']]) {
    const filename = `container-${name}.svg`;
    fs.writeFileSync(path.join(textureDirectory, filename), `<svg xmlns="http://www.w3.org/2000/svg" width="512" height="512"><rect width="512" height="512" fill="${color}"/>${Array.from({ length: 12 }, (_, i) => `<path d="M${i * 44} 0V512" stroke="#182d32" stroke-opacity=".3" stroke-width="10"/>`).join('')}<rect x="48" y="190" width="416" height="116" fill="#c6bd9b"/><text x="256" y="265" text-anchor="middle" font-family="Arial,sans-serif" font-weight="bold" font-size="54" fill="#273b3d">SERVICE</text></svg>\n`);
    wall(`wall-container-${name}`, id, `textures/raceway/${filename}`);
}

for (let x = 0; x < width; x++) grid[x][0] = grid[x][height - 1] = 510;
for (let y = 0; y < height; y++) grid[0][y] = grid[width - 1][y] = 510;

// The 54 deliberate major bends form a simple polygon with no crossings.
// Concave excursions distinguish the circuit's technical and scenic regions.
const corners = [[24, 454], [24, 358], [54, 358], [54, 330], [34, 330], [34, 300],
    [66, 300], [66, 266], [24, 266], [24, 218], [72, 218], [72, 188], [40, 188],
    [40, 154], [84, 154], [84, 120], [34, 120], [34, 80], [62, 80], [62, 44],
    [124, 44], [124, 72], [98, 72], [98, 108], [142, 108], [142, 26], [220, 26],
    [220, 94], [188, 94], [188, 130], [226, 130], [226, 176], [182, 176],
    [182, 212], [222, 212], [222, 250], [166, 250], [166, 290], [224, 290],
    [224, 330], [186, 330], [186, 366], [226, 366], [226, 414], [172, 414],
    [172, 452], [216, 452], [216, 480], [146, 480], [146, 430], [100, 430],
    [100, 474], [48, 474], [48, 454]];
const direction = (a, b) => [Math.sign(b[0] - a[0]), Math.sign(b[1] - a[1])];
const anchors = [];
for (let i = 0; i < corners.length; i++) {
    const previous = corners[(i + corners.length - 1) % corners.length], corner = corners[i], next = corners[(i + 1) % corners.length];
    const incoming = direction(previous, corner), outgoing = direction(corner, next), radius = 5;
    if (incoming[0] * outgoing[0] + incoming[1] * outgoing[1] !== 0) throw new Error('Expected a deliberate right-angle major bend');
    const entry = [corner[0] - incoming[0] * radius, corner[1] - incoming[1] * radius];
    anchors.push(entry);
    let position = entry;
    for (let step = 0; step < radius; step++) {
        position = [position[0] + incoming[0], position[1] + incoming[1]]; anchors.push(position);
        position = [position[0] + outgoing[0], position[1] + outgoing[1]]; anchors.push(position);
    }
}
anchors.push(anchors[0]);
for (let i = 1; i < anchors.length; i++) {
    const [ax, ay] = anchors[i - 1], [bx, by] = anchors[i];
    if (ax !== bx && ay !== by) throw new Error('Road stamps must join orthogonally');
    for (let x = Math.min(ax, bx); x <= Math.max(ax, bx); x++)
        for (let y = Math.min(ay, by); y <= Math.max(ay, by); y++)
            for (const ox of [0, 1]) for (const oy of [0, 1]) track.add(key(x + ox, y + oy));
}
for (let y = 395; y <= 449; y++) track.add(key(26, y));
for (const point of track) {
    const [x, y] = point.split(',').map(Number);
    for (const [dx, dy] of [[1, 0], [-1, 0], [0, 1], [0, -1]])
        if (!track.has(key(x + dx, y + dy))) grid[x + dx][y + dy] = 502;
}
for (const point of track) { const [x, y] = point.split(',').map(Number); grid[x][y] = 500; }
for (let x = 24; x <= 26; x++) grid[x][442] = 505;
for (let y = 400; y <= 448; y += 4) for (let x = 24; x <= 26; x++) grid[x][y] = 506;
grid[26][395] = 508;

const terrain = id => [501, 502, 593, 594].includes(id);
function rect(left, top, right, bottom, id, onlyTerrain = false) {
    for (let x = left; x <= right; x++) for (let y = top; y <= bottom; y++)
        if (!onlyTerrain || terrain(grid[x][y])) grid[x][y] = id;
}
function place(id, points) { for (const [x, y] of points) if (terrain(grid[x]?.[y])) grid[x][y] = id; }
const lineX = (left, right, y, step = 1) => Array.from({ length: Math.floor((right - left) / step) + 1 }, (_, i) => [left + i * step, y]);
const lineY = (x, top, bottom, step = 1) => Array.from({ length: Math.floor((bottom - top) / step) + 1 }, (_, i) => [x, top + i * step]);

// Start/paddock: seven larger inline grandstands, fenced apron, three garages.
rect(29, 396, 33, 450, 500);
rect(26, 450, 29, 451, 500);
for (const y of [410, 414, 418, 422]) rect(34, y, 40, y, 513);
rect(40, 410, 40, 422, 513);
for (const [bay, first] of [[1, 411], [2, 415], [3, 419]]) {
    rect(34, first, 39, first + 2, 504); grid[34][first] = 513 + bay; grid[34][first + 2] = 513;
    grid[38][first + 1] = 549;
}
rect(34, 388, 40, 394, 513); rect(35, 389, 39, 393, 618); grid[34][391] = 618; grid[34][390] = 616;
for (const y of [402, 410, 418, 426, 434, 442, 450]) {
    rect(13, y - 3, 20, y + 3, 507); grid[17][y] = 590;
}
place(546, [...lineY(23, 396, 454), ...lineY(27, 399, 445)]);
place(598, [[31, 394]]);
// A clear paddock-side buffer supports reproducible front/side visual review.
place(544, [[28, 404]]);

// Stone Cut: pale quarry floor, rock piles, and a long covered rock tunnel.
rect(70, 33, 112, 59, 594, true);
for (let x = 82; x <= 103; x++) {
    grid[x][44] = grid[x][45] = 503; grid[x][43] = grid[x][46] = 511;
}
for (const x of [81, 104]) grid[x][43] = grid[x][46] = 512;
for (const bounds of [[73, 52, 76, 54], [107, 35, 110, 38], [108, 53, 111, 56]]) rect(...bounds, 511);
place(600, [[77, 51]]);

function house(left, top, right, bottom, alternate = false) {
    rect(left, top, right, bottom, alternate ? 617 : 596); rect(left + 1, top + 1, right - 1, bottom - 1, 595);
    grid[left][top + 3] = grid[left][top + 4] = 595;
}
// Marshal Village: two accessible wooden huts and a small spectator row.
rect(184, 181, 214, 210, 593, true); house(190, 184, 196, 190); house(205, 196, 211, 202, true);
rect(184, 186, 190, 188, 593, true); place(601, [[187, 183]]);
for (const x of [192, 201]) { rect(x - 3, 206, x + 3, 209, 507); grid[x][207] = 592; }

// Spectator Plaza: tiled forecourt and three inline grandstand modules.
rect(174, 261, 200, 279, 593, true); rect(168, 268, 174, 270, 593, true);
for (const x of [178, 187, 196]) { rect(x - 3, 257, x + 3, 260, 507); grid[x][258] = 592; }
place(602, [[171, 265]]); place(547, lineX(174, 200, 253));

// Maintenance Yard: colored ribbed container blocks, a clear center aisle,
// workshop floors, and grouped service barrels.
rect(192, 336, 218, 360, 615, true); rect(188, 347, 192, 349, 615, true);
for (const [x, y, id] of [[198, 338, 612], [208, 338, 613], [198, 352, 613], [208, 352, 612]]) rect(x, y, x + 4, y + 6, id);
for (const point of [[195, 340], [216, 341], [194, 357], [216, 356]]) grid[point[0]][point[1]] = 608;
place(603, [[192, 344]]);
place(599, [[57, 345]]); place(604, [[209, 31]]); place(605, [[75, 204]]); place(606, [[152, 477]]);

// Tires protect each major apex; additional runoffs and buffer approach
// markers distinguish the many corner complexes without narrowing asphalt.
for (let i = 0; i < corners.length; i++) {
    const [x, y] = corners[i];
    rect(x - 2, y - 2, x + 2, y + 2, 502, true);
    place(i % 3 === 0 ? 585 : 581, [[x, y]]);
    const previous = corners[(i + corners.length - 1) % corners.length], incoming = direction(previous, corners[i]);
    const normal = [-incoming[1], incoming[0]];
    for (const side of [-1, 1]) {
        const offset = [x - incoming[0] * 13 + normal[0] * side * 3, y - incoming[1] * 13 + normal[1] * side * 3];
        place(incoming[0] ? 543 : 580, [offset]);
    }
    if (i % 2 === 0) place(incoming[0] ? 545 : 544,
        [[x - incoming[0] * 19 + normal[0] * 3, y - incoming[1] * 19 + normal[1] * 3]]);
}
// Isolated stack samples remain useful editor presets and mark the paddock exit.
place(583, [[28, 451]]); place(584, [[28, 452]]); place(582, [[43, 449]]);

function hash(x, y) { return ((Math.imul(x + 101, 73856093) ^ Math.imul(y + 67, 19349663)) >>> 0); }
// Multi-source Chebyshev distance limits woodland to scenic corridors instead
// of filling all131,072cells with expensive transparent sprites.
const distances = Array.from({ length: width }, () => new Uint16Array(height).fill(65535));
const queue = [];
for (let x = 1; x < width - 1; x++) for (let y = 1; y < height - 1; y++)
    if ([500, 503, 504, 505, 506, 508, 593, 594, 595, 615, 618].includes(grid[x][y])) { distances[x][y] = 0; queue.push([x, y]); }
for (let head = 0; head < queue.length; head++) {
    const [x, y] = queue[head], next = distances[x][y] + 1;
    for (let dx = -1; dx <= 1; dx++) for (let dy = -1; dy <= 1; dy++) {
        const tx = x + dx, ty = y + dy;
        if (tx > 0 && tx < width - 1 && ty > 0 && ty < height - 1 && distances[tx][ty] > next) {
            distances[tx][ty] = next; queue.push([tx, ty]);
        }
    }
}
const treePresets = [540, 541, 570, 571, 572, 573, 574, 575, 576, 577, 578];
function reserved(x, y) { return x >= 12 && x <= 44 && y >= 385 && y <= 456; }
for (let x = 4; x < width - 4; x += 6) for (let y = 4; y < height - 4; y += 6) {
    const value = hash(x, y), tx = x + value % 3, ty = y + (value >>> 3) % 3;
    if (grid[tx][ty] === 501 && distances[tx][ty] >= 3 && distances[tx][ty] <= 9 && !reserved(tx, ty))
        grid[tx][ty] = treePresets[(value >>> 6) % treePresets.length];
}
const belts = [...lineX(3, width - 4, 3, 10), ...lineX(3, width - 4, height - 4, 10),
    ...lineY(3, 9, height - 10, 10), ...lineY(width - 4, 9, height - 10, 10),
    ...lineX(8, width - 9, 8, 10), ...lineX(8, width - 9, height - 9, 10),
    ...lineY(8, 14, height - 15, 10), ...lineY(width - 9, 14, height - 15, 10)];
place(579, belts);
for (let x = 14; x < width - 14; x += 22) for (let y = 14; y < height - 14; y += 22)
    if (grid[x][y] === 501 && distances[x][y] > 11 && hash(x, y) % 3 === 0 && !reserved(x, y))
        grid[x][y] = treePresets[(hash(x, y) >>> 7) % treePresets.length];
for (let x = 5; x < width - 5; x += 5) for (let y = 5; y < height - 5; y += 5)
    if (grid[x][y] === 501 && distances[x][y] >= 2 && distances[x][y] <= 8 && hash(x, y) % 5 === 0 && !reserved(x, y))
        grid[x][y] = hash(x, y) % 2 ? 542 : 550;

const objects = {};
for (const name of fs.readdirSync(objectDirectory).filter(name => name.endsWith('.json')).sort()) {
    const value = JSON.parse(fs.readFileSync(path.join(objectDirectory, name), 'utf8'));
    objects[value.id] = `../objects/raceway/${name}`; definitions.set(value.id, value);
}
const map = { objects, worldMap: grid, playerStart: { x: 25.5, y: 445.5, directionX: 0, directionY: -1,
    moveSpeed: 3.2, rotSpeed: 3, collisionRadius: .15, jumpRise: .4, jumpLength: 2,
    jumpSpeedMultiplier: 1.25, crouchDrop: .25, crouchSpeedMultiplier: .5 },
    sky: { texture: 'textures/skyboxes/skybox.jpg', rotationDegrees: 110 } };
const pretty = JSON.stringify(map, null, 2), before = pretty.indexOf('  "worldMap": ['), after = pretty.indexOf('  "playerStart":', before);
const columns = '  "worldMap": [\n' + grid.map(column => '    [' + column.join(', ') + ']').join(',\n') + '\n  ],\n';
fs.writeFileSync(path.join(web, 'maps/pine-loop.json'), pretty.slice(0, before) + columns + pretty.slice(after) + '\n');

const zones = [
    { name: 'Paddock and spectator straight', checkpoint: [25, 426], bounds: [12, 385, 44, 456] },
    { name: 'Technical esses', checkpoint: [55, 345], bounds: [24, 266, 66, 358] },
    { name: 'Woodland hairpins', checkpoint: [73, 204], bounds: [24, 120, 84, 218] },
    { name: 'Stone Cut quarry and tunnel', checkpoint: [92, 45], bounds: [62, 33, 124, 80] },
    { name: 'North ridge', checkpoint: [200, 27], bounds: [142, 26, 220, 108] },
    { name: 'Marshal Village', checkpoint: [183, 194], bounds: [182, 176, 226, 212] },
    { name: 'Spectator Plaza', checkpoint: [167, 270], bounds: [166, 250, 222, 290] },
    { name: 'Maintenance Yard', checkpoint: [187, 348], bounds: [186, 330, 226, 366] },
    { name: 'South loop hairpins', checkpoint: [147, 455], bounds: [100, 414, 226, 480] },
];
const fixture = { dimensions: [width, height], lap: anchors.map(([x, y]) => [x + 1, y + 1]),
    majorCorners: corners, zones, tunnel: { left: 82, right: 103, top: 44, bottom: 45 },
    pitEntrances: [[34, 412], [34, 416], [34, 420]], bufferCoordinate: [28, 404], bufferSpriteId: 525,
    poiApproaches: [
        { name: 'Covered pit box', from: [31.5, 416.5], to: [35.5, 416.5] },
        { name: 'Marshal Village hut', from: [183.5, 187.5], to: [191.5, 187.5] },
        { name: 'Spectator Plaza', from: [167.5, 269.5], to: [175.5, 269.5] },
        { name: 'Maintenance Yard aisle', from: [187.5, 348.5], to: [194.5, 348.5] },
    ] };
fs.mkdirSync(path.join(repository, 'Tests/Fixtures'), { recursive: true });
fs.mkdirSync(path.join(repository, 'artifacts'), { recursive: true });
const views = [['start', [25.5, 445.5, 0, -1]], ['narrowing', [25.5, 399.5, 0, -1]],
    ['grandstand-row', [25.5, 438.5, -.8, -.6]], ['grandstand-front', [25.5, 418.5, -1, 0]],
    ['pits', [31.5, 416.5, 1, 0]], ['race-control', [25.5, 391.5, 1, 0]],
    ['technical-esses', [55, 345, 0, -1]], ['woodland-hairpins', [73, 204, 0, -1]],
    ['quarry', [76.5, 45, 0, 1]], ['tunnel-entry', [79.5, 45, 1, 0]], ['tunnel-interior', [92.5, 45, 1, 0]],
    ['north-ridge', [200, 27, 1, 0]], ['village', [183.5, 187.5, 1, 0]],
    ['village-stand', [201.5, 213, 0, -1]], ['spectator-plaza', [187.5, 251, 0, 1]],
    ['plaza-courtyard', [175.5, 269.5, .8, -.6]], ['maintenance-yard', [187.5, 348.5, 1, 0]],
    ['south-loop', [147, 455, 0, 1]], ['tires', [54.5, 333.5, -.7, -.7]],
    ['buffer-front', [30.5, 404.5, -1, 0]], ['buffer-side', [28.5, 406.5, 0, -1]]];
fixture.views = views;
fs.writeFileSync(path.join(repository, 'Tests/Fixtures/pine-loop-lap.json'), JSON.stringify(fixture, null, 2) + '\n');
fs.writeFileSync(path.join(repository, 'artifacts/pine-loop-views.json'), JSON.stringify(views, null, 2) + '\n');

// A vector overview is an authoring diagram; it does not become a game asset.
const overviewScale = 2, linePoints = fixture.lap.map(([x, y]) => `${x * overviewScale},${y * overviewScale}`).join(' ');
const labels = zones.map((zone, i) => `<circle cx="${zone.checkpoint[0] * 2}" cy="${zone.checkpoint[1] * 2}" r="9" fill="#e6bb68"/><text x="${zone.checkpoint[0] * 2}" y="${zone.checkpoint[1] * 2 + 4}" text-anchor="middle" fill="#182d29" font-family="Arial" font-size="12" font-weight="bold">${i + 1}</text><text x="540" y="${100 + i * 43}" fill="#eee6cc" font-family="Arial" font-size="16">${i + 1}. ${zone.name}</text>`).join('');
const trees = new Set([520, 529, 530]); let treeCount = 0, spriteCount = 0, grandstands = 0;
for (const column of grid) for (const id of column) {
    const value = definitions.get(id);
    if (value?.type === 'spriteGroup') { spriteCount += value.sprites.length; treeCount += value.sprites.filter(child => trees.has(child.spriteId)).length; }
    else if (value?.type === 'sprite') spriteCount++;
    if ([590, 592].includes(id)) grandstands++;
}
const lapLength = fixture.lap.slice(1).reduce((sum, [x, y], i) => sum + Math.hypot(x - fixture.lap[i][0], y - fixture.lap[i][1]), 0);
const overview = `<svg xmlns="http://www.w3.org/2000/svg" width="930" height="1080" viewBox="0 0 930 1080"><rect width="930" height="1080" fill="#182f2a"/><rect x="1" y="1" width="510" height="1022" fill="#345441" stroke="#6b8266"/><polyline points="${linePoints}" fill="none" stroke="#a88d5b" stroke-width="8" stroke-linejoin="round"/><polyline points="${linePoints}" fill="none" stroke="#3b4444" stroke-width="4" stroke-linejoin="round"/><path d="M48 884H54" stroke="#f4e9cf" stroke-width="6"/>${labels}<text x="540" y="42" fill="#f3ead1" font-family="Arial" font-size="28" font-weight="bold">PINE LOOP</text><text x="540" y="69" fill="#b3c4b5" font-family="Arial" font-size="16">256 × 512 · ${lapLength} unit lap</text><text x="540" y="530" fill="#c4d1c1" font-family="Arial" font-size="16">${corners.length} major bends · ${treeCount} trees</text><text x="540" y="558" fill="#c4d1c1" font-family="Arial" font-size="16">${grandstands} larger grandstand modules</text><text x="540" y="605" fill="#c4d1c1" font-family="Arial" font-size="15">North is up. Route is two tiles wide.</text><text x="540" y="631" fill="#c4d1c1" font-family="Arial" font-size="15">Start: 3 lanes, narrowing to 2.</text></svg>\n`;
fs.writeFileSync(path.join(repository, 'artifacts/pine-loop-overview.svg'), overview);
console.log(`Pine Loop: ${width}x${height}, ${track.size} main-road cells, ${lapLength}-unit lap, ${corners.length} major bends, ${treeCount} trees, ${spriteCount} sprites, ${grandstands} grandstand modules.`);

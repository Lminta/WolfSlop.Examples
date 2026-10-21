// Large-course authoring module used by build-pine-loop-map.mjs.
// Existing shared scenery JSON remains the source for trees, tires, and buffers.
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { writeRacewayEventProps } from './build-raceway-event-props.mjs';
import { writeRacewayTireProps } from './build-raceway-tire-props.mjs';

const repository = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const web = path.join(repository, 'WolfNET.Game/wwwroot');
const objectDirectory = path.join(web, 'objects/raceway');
const textureDirectory = path.join(web, 'textures/raceway');
const width = 256, height = 512;
const grid = Array.from({ length: width }, () => Array(height).fill(501));
const track = new Set();
const key = (x, y) => `${x},${y}`;
const definitions = new Map();
writeRacewayTireProps();
for (const filename of fs.readdirSync(objectDirectory).filter(name => name.endsWith('.json'))) {
    const definition = JSON.parse(fs.readFileSync(path.join(objectDirectory, filename), 'utf8'));
    definitions.set(definition.id, definition);
}

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
function passage(name, id, floorTexture, ceilingTexture = null, surface = 'mud') {
    object(name, id, 'passage', { floorTexture, surface, ceilingMode: ceilingTexture ? 'texture' : 'sky',
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
passage('passage-maintenance-yard', 615, 'textures/raceway/asphalt.png', null, 'asphalt');
passage('passage-control-room', 618, 'textures/glassworks/checker-floor.png', 'textures/glassworks/plaster-ceiling.svg');
wall('wall-village-plaster', 617, 'textures/glassworks/limestone-wall.png', 595);
object('sprite-service-barrel', 609, 'sprite', { passageId: 501, texture: 'textures/wolf/barrel.png', textureLayer: 124 });
object('group-service-barrels', 608, 'spriteGroup', { collides: true, passageId: 615,
    sprites: [member(609, -.25, -.2, .6), member(609, .26, -.17, .6), member(609, .05, .26, .6)] });
// Repeating rail sections share the existing transparent fence artwork. The
// corner section closes both visual planes while every occupied tile collides.
object('group-safety-rail-x', 620, 'spriteGroup', { collides: true, passageId: 502,
    sprites: [member(523, 0, 0, 1.1)] });
object('group-safety-rail-y', 621, 'spriteGroup', { collides: true, passageId: 502,
    sprites: [member(524, 0, 0, 1.1)] });
object('group-safety-rail-corner', 622, 'spriteGroup', { collides: true, passageId: 502,
    sprites: [member(523, 0, 0, 1.1), member(524, 0, 0, 1.1)] });

const signDefinitions = [
    [598, 'paddock', 'PINE LOOP', 'PADDOCK', '#284a45'],
    [599, 'esses', 'MEADOW', 'LOOP', '#a57b36'],
    [600, 'quarry', 'STONE CUT', 'QUARRY TUNNEL', '#596866'],
    [601, 'village', 'MARSHAL', 'VILLAGE', '#76553a'],
    [602, 'plaza', 'SPECTATOR', 'PLAZA', '#425c72'],
    [603, 'yard', 'MAINTENANCE', 'YARD', '#536f73'],
    [604, 'ridge', 'EASTERN', 'FOREST STRAIGHT', '#365543'],
    [605, 'hairpins', 'WOODLAND', 'LOOP', '#506343'],
    [606, 'south-loop', 'SOUTH LOOP', 'RETURN TO GRID', '#8b613c'],
    [616, 'control', 'RACE', 'CONTROL', '#234742'],
    [626, 'shortcut-entry', 'ONE SHORTCUT', 'MUD EXPRESS →', '#9a6d32'],
    [627, 'shortcut-exit', 'MUD EXPRESS', 'REJOIN WITH CARE', '#9a6d32'],
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

// Large interlocking infield loops, a northern bowl, and one uninterrupted
// eastern straight. This simple polygon has no crossings or repeated chicanes.
// The western straight keeps the four-wide grid and pit facilities.
const corners = [[24, 478], [24, 280], [100, 280], [100, 360],
    [64, 360], [64, 408], [140, 408], [140, 248], [64, 248],
    [64, 184], [176, 184], [176, 344], [208, 344], [208, 116],
    [24, 116], [24, 44], [232, 44], [232, 478]];
const direction = (a, b) => [Math.sign(b[0] - a[0]), Math.sign(b[1] - a[1])];
const anchors = [];
const bendRadii = [];
const bendEntries = [], bendMidpoints = [], bendExits = [];
function appendRoundedPoint(point) {
    const target = point.map(Math.round);
    if (!anchors.length) { anchors.push(target); return; }
    // Connected tile steps retain two clear lanes even on the round arcs.
    const previous = anchors.at(-1);
    let [x, y] = previous;
    while (x !== target[0] || y !== target[1]) {
        if (x !== target[0]) { x += Math.sign(target[0] - x); anchors.push([x, y]); }
        if (y !== target[1]) { y += Math.sign(target[1] - y); anchors.push([x, y]); }
    }
}
for (let i = 0; i < corners.length; i++) {
    const previous = corners[(i + corners.length - 1) % corners.length], corner = corners[i], next = corners[(i + 1) % corners.length];
    const incoming = direction(previous, corner), outgoing = direction(corner, next);
    const radius = Math.min(i >= 14 || i === 0 ? 30 : 24,
        Math.floor(Math.hypot(corner[0] - previous[0], corner[1] - previous[1]) * .44),
        Math.floor(Math.hypot(next[0] - corner[0], next[1] - corner[1]) * .44));
    bendRadii.push(radius);
    if (incoming[0] * outgoing[0] + incoming[1] * outgoing[1] !== 0) throw new Error('Expected a deliberate right-angle major bend');
    const entry = [corner[0] - incoming[0] * radius, corner[1] - incoming[1] * radius];
    bendEntries.push(entry);
    appendRoundedPoint(entry);
    const center = [entry[0] + outgoing[0] * radius, entry[1] + outgoing[1] * radius];
    for (let step = 1; step <= radius * 8; step++) {
        const angle = step / (radius * 8) * Math.PI / 2;
        appendRoundedPoint([center[0] - outgoing[0] * radius * Math.cos(angle) + incoming[0] * radius * Math.sin(angle),
            center[1] - outgoing[1] * radius * Math.cos(angle) + incoming[1] * radius * Math.sin(angle)]);
        if (step === radius * 4) bendMidpoints.push(anchors.at(-1));
    }
    bendExits.push(anchors.at(-1));
}
appendRoundedPoint(anchors[0]);
// Alternate passing sections and narrow lanes along the straights. Keep broad
// bends and the fixed quarry tunnel two blocks wide, with staged transitions.
const straights = bendExits.map((from, i) => ({ from, to: bendEntries[(i + 1) % corners.length] }));
function roadWidth(x, y) {
    if (x === 24 && y >= 400 && y <= 449) return 4;
    if (x === 24 && y >= 392 && y < 400) return 3;
    if (x === 24 && y >= 380 && y < 392) return 2;
    if (y === 44 && x >= 70 && x <= 115) return 2;
    for (const { from, to } of straights) {
        const length = Math.abs(to[0] - from[0]) + Math.abs(to[1] - from[1]);
        if (length < 64) continue;
        const along = Math.abs(x - from[0]) + Math.abs(y - from[1]);
        const remaining = Math.abs(x - to[0]) + Math.abs(y - to[1]);
        if (along + remaining !== length || along < 12 || remaining < 12) continue;
        const repeats = Math.max(1, Math.floor((length - 24) / 120));
        const phase = ((along - 12) / (length - 24) * repeats) % 1;
        return phase < .12 ? 2 : phase < .43 ? 3 : phase < .55 ? 2 : phase < .83 ? 1 : 2;
    }
    return 2;
}
for (let i = 1; i < anchors.length; i++) {
    const [ax, ay] = anchors[i - 1], [bx, by] = anchors[i];
    if (ax !== bx && ay !== by) throw new Error('Road stamps must join orthogonally');
    for (let x = Math.min(ax, bx); x <= Math.max(ax, bx); x++)
        for (let y = Math.min(ay, by); y <= Math.max(ay, by); y++)
            {
                const lanes = roadWidth(x, y);
                const offsets = lanes === 1 ? [1] : Array.from({ length: lanes }, (_, offset) => offset);
                for (const ox of offsets) for (const oy of offsets) track.add(key(x + ox, y + oy));
            }
}
for (const point of track) {
    const [x, y] = point.split(',').map(Number);
    for (const [dx, dy] of [[1, 0], [-1, 0], [0, 1], [0, -1]])
        if (!track.has(key(x + dx, y + dy))) grid[x + dx][y + dy] = 502;
}
for (const point of track) { const [x, y] = point.split(',').map(Number); grid[x][y] = 500; }
for (let x = 24; x <= 27; x++) grid[x][442] = 505;
for (let y = 400; y <= 448; y += 4) for (let x = 24; x <= 27; x++) grid[x][y] = 506;

const terrain = id => [501, 502, 593, 594].includes(id);
function rect(left, top, right, bottom, id, onlyTerrain = false) {
    for (let x = left; x <= right; x++) for (let y = top; y <= bottom; y++)
        if (!onlyTerrain || terrain(grid[x][y])) grid[x][y] = id;
}
function place(id, points) { for (const [x, y] of points) if (terrain(grid[x]?.[y])) grid[x][y] = id; }
// Optional scenery must leave the full road and its one-block shoulder clear.
function placeTrackside(id, points) {
    place(id, points.filter(([x, y]) => {
        for (let dx = -1; dx <= 1; dx++) for (let dy = -1; dy <= 1; dy++)
            if (track.has(key(x + dx, y + dy))) return false;
        return true;
    }));
}
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
place(546, lineY(23, 396, 454));
place(598, [[31, 394]]);
// A clear paddock-side buffer supports reproducible front/side visual review.
placeTrackside(544, [[35, 404]]);

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
rect(184, 181, 205, 210, 593, true); house(190, 184, 196, 190); house(198, 196, 204, 202, true);
rect(184, 186, 190, 188, 593, true); place(601, [[187, 183]]);
for (const x of [192, 201]) { rect(x - 3, 206, x + 3, 209, 507); grid[x][207] = 592; }

// Spectator Plaza: tiled forecourt and three inline grandstand modules.
rect(179, 261, 205, 279, 593, true); rect(178, 268, 184, 270, 593, true);
for (const x of [185, 194, 203]) { rect(x - 3, 257, x + 3, 260, 507); grid[x][258] = 592; }
place(602, [[182, 265]]); place(547, lineX(181, 204, 253));

// Maintenance Yard: colored ribbed container blocks, a clear center aisle,
// workshop floors, and grouped service barrels.
rect(192, 372, 218, 396, 615, true); rect(219, 383, 231, 385, 615, true);
for (const [x, y, id] of [[198, 374, 612], [208, 374, 613], [198, 388, 613], [208, 388, 612]]) rect(x, y, x + 4, y + 6, id);
for (const point of [[195, 376], [216, 377], [194, 393], [216, 392]]) grid[point[0]][point[1]] = 608;
place(603, [[222, 380]]);
place(604, [[237, 224]]); place(605, [[69, 213]]); place(606, [[152, 472]]);

// Tires protect each major apex; additional runoffs and buffer approach
// markers distinguish the many corner complexes without narrowing asphalt.
for (let i = 0; i < corners.length; i++) {
    const [x, y] = corners[i];
    const [mx, my] = bendMidpoints[i];
    const apex = [mx + Math.sign(x - mx) * 3, my + Math.sign(y - my) * 3];
    rect(apex[0] - 2, apex[1] - 2, apex[0] + 2, apex[1] + 2, 502, true);
    placeTrackside(i % 3 === 0 ? 585 : 581, [apex]);
    const previous = corners[(i + corners.length - 1) % corners.length], incoming = direction(previous, corners[i]);
    const [entryX, entryY] = bendEntries[i];
    const normal = [-incoming[1], incoming[0]];
    for (const side of [-1, 1]) {
        const offset = [entryX - incoming[0] * 13 + normal[0] * side * 3, entryY - incoming[1] * 13 + normal[1] * side * 3];
        placeTrackside(incoming[0] ? 543 : 580, [offset]);
    }
    if (i % 2 === 0) placeTrackside(incoming[0] ? 545 : 544,
        [[entryX - incoming[0] * 19 + normal[0] * 3, entryY - incoming[1] * 19 + normal[1] * 3]]);
}
// Keep the paddock tire pile beyond the road and shoulder.
placeTrackside(582, [[43, 449]]);

// One deliberate mud link is the only way to skip an infield loop. All other
// scenery stays visible behind continuous rails rather than being driveable
// open grass. Landmark pockets have a single side open to their own road leg.
const shortcut = { name: 'Mud Express', left: 102, right: 139, top: 320, bottom: 321,
    from: [101, 321], to: [141, 321], seal: [120, 317, 120, 324] };
rect(shortcut.left, shortcut.top, shortcut.right, shortcut.bottom, 502);
const drivingCore = new Set(track);
function allowDriving(left, top, right, bottom) {
    for (let x = left; x <= right; x++) for (let y = top; y <= bottom; y++) drivingCore.add(key(x, y));
}
allowDriving(shortcut.left, shortcut.top, shortcut.right, shortcut.bottom);
allowDriving(24, 388, 41, 453); // Grid, pit apron, garages, and race control.
allowDriving(70, 33, 112, 59); // Quarry runoff contains only this road section.
allowDriving(182, 180, 207, 215); // Village opens onto X208, separated from X176.
allowDriving(179, 261, 203, 279); // Plaza opens onto X176, separated from X208.
allowDriving(178, 268, 184, 270);
allowDriving(192, 372, 218, 396); // Yard is a cul-de-sac from the eastern straight.
allowDriving(219, 383, 231, 385);
// A one-tile shoulder follows the varying road width. The surrounding
// eight-neighbour ring also closes diagonal gaps for the production collider.
const drivingArea = new Set();
for (const point of drivingCore) {
    const [x, y] = point.split(',').map(Number);
    for (let dx = -1; dx <= 1; dx++) for (let dy = -1; dy <= 1; dy++) drivingArea.add(key(x + dx, y + dy));
}
const railCells = new Set();
const bendBounds = corners.map((corner, i) => {
    const points = [corner, bendEntries[i], bendExits[i]];
    return [Math.min(...points.map(point => point[0])) - 5, Math.min(...points.map(point => point[1])) - 5,
        Math.max(...points.map(point => point[0])) + 6, Math.max(...points.map(point => point[1])) + 6];
});
const bendRailCells = [];
for (const point of drivingArea) {
    const [x, y] = point.split(',').map(Number);
    for (let dx = -1; dx <= 1; dx++) for (let dy = -1; dy <= 1; dy++) {
        const tx = x + dx, ty = y + dy;
        if (!drivingArea.has(key(tx, ty))) railCells.add(key(tx, ty));
    }
}
for (const point of railCells) {
    const [x, y] = point.split(',').map(Number);
    if (definitions.get(grid[x][y])?.collides) continue;
    const coreEastWest = drivingArea.has(key(x - 1, y)) || drivingArea.has(key(x + 1, y));
    const coreNorthSouth = drivingArea.has(key(x, y - 1)) || drivingArea.has(key(x, y + 1));
    if (bendBounds.some(([left, top, right, bottom]) => x >= left && x <= right && y >= top && y <= bottom)) {
        const alongY = coreEastWest && !coreNorthSouth;
        grid[x][y] = (x + y) % 2 === 0 ? (alongY ? 660 : 623) : (alongY ? 661 : 624);
        bendRailCells.push([x, y]);
    } else grid[x][y] = coreEastWest && coreNorthSouth ? 622 : coreEastWest ? 620 : coreNorthSouth ? 621 : 623;
}
// Cross-sections are away from POI pockets. Sealing the marked shortcut and
// these gates must split the production navigation map into18 corner sections.
const topologyGates = bendExits.map((exit, i) => {
    const entry = bendEntries[(i + 1) % corners.length];
    const x = Math.round((exit[0] + entry[0]) / 2), y = Math.round((exit[1] + entry[1]) / 2);
    return exit[0] === entry[0] ? [x - 6, y, x + 7, y] : [x, y - 6, x, y + 7];
});
topologyGates[10] = [170, 230, 183, 230]; // Above the spectator-plaza access pocket.
const eventProps = writeRacewayEventProps();
for (const [id, definition] of eventProps.definitions) definitions.set(id, definition);
const eventPlacements = [];
for (const placement of eventProps.placements) {
    const candidates = [];
    for (let dx = -placement.searchRadius; dx <= placement.searchRadius; dx++)
        for (let dy = -placement.searchRadius; dy <= placement.searchRadius; dy++) {
            const x = placement.x + dx, y = placement.y + dy;
            if (x <= 1 || x >= width - 1 || y <= 1 || y >= height - 1 || ![501, 502, 593, 594, 615].includes(grid[x][y])) continue;
            if (track.has(key(x, y))) continue;
            // Keep the billboard/pole width out of the racing lanes.
            let clear = true;
            for (let ox = -1; ox <= 1; ox++) for (let oy = -1; oy <= 1; oy++)
                if (track.has(key(x + ox, y + oy))) clear = false;
            if (clear) candidates.push({ x, y, distance: dx * dx + dy * dy });
        }
    candidates.sort((a, b) => a.distance - b.distance || a.x - b.x || a.y - b.y);
    if (!candidates.length) throw new Error(`No free event prop position for ${placement.label}`);
    const { x, y } = candidates[0];
    grid[x][y] = placement.id;
    eventPlacements.push({ ...placement, x, y });
}

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
    if (grid[tx][ty] === 501 && distances[tx][ty] >= 3 && distances[tx][ty] <= 18 && !reserved(tx, ty) && !drivingArea.has(key(tx, ty)))
        grid[tx][ty] = treePresets[(value >>> 6) % treePresets.length];
}
const belts = [...lineX(3, width - 4, 3, 10), ...lineX(3, width - 4, height - 4, 10),
    ...lineY(3, 9, height - 10, 10), ...lineY(width - 4, 9, height - 10, 10),
    ...lineX(8, width - 9, 8, 10), ...lineX(8, width - 9, height - 9, 10),
    ...lineY(8, 14, height - 15, 10), ...lineY(width - 9, 14, height - 15, 10)];
place(579, belts);
for (let x = 14; x < width - 14; x += 13) for (let y = 14; y < height - 14; y += 13)
    if (grid[x][y] === 501 && distances[x][y] > 18 && hash(x, y) % 3 !== 0 && !reserved(x, y) && !drivingArea.has(key(x, y)))
        grid[x][y] = treePresets[(hash(x, y) >>> 7) % treePresets.length];
for (let x = 5; x < width - 5; x += 6) for (let y = 5; y < height - 5; y += 6)
    if (grid[x][y] === 501 && distances[x][y] >= 3 && distances[x][y] <= 14 && hash(x, y) % 5 === 0 && !reserved(x, y) && !drivingArea.has(key(x, y)))
        grid[x][y] = hash(x, y) % 2 ? 542 : 550;

const objects = {};
for (const name of fs.readdirSync(objectDirectory).filter(name => name.endsWith('.json')).sort()) {
    const value = JSON.parse(fs.readFileSync(path.join(objectDirectory, name), 'utf8'));
    objects[value.id] = `../objects/raceway/${name}`; definitions.set(value.id, value);
}
// Audit every asphalt tile, including passing lanes outside the lap centerline.
for (const point of track) {
    const [x, y] = point.split(',').map(Number);
    const definition = definitions.get(grid[x][y]);
    if (!definition || definition.collides || ![500, 503, 505, 506, 508].includes(definition.id))
        throw new Error(`Scenery intrudes into the racing surface at ${point}: ${grid[x][y]}`);
}
// Timing gates are invisible metadata. Skip the Meadow-loop section bypassed
// by Mud Express so the deliberately authored shortcut remains a valid lap.
const race = {
    startFinish: { center: [25.5, 442.5], direction: [0, -1], halfWidth: 3.5 },
    checkpoints: bendExits.flatMap((exit, index) => {
        if (index >= 2 && index <= 6) return [];
        const entry = bendEntries[(index + 1) % corners.length];
        return [{ center: [(exit[0] + entry[0]) / 2 + 1, (exit[1] + entry[1]) / 2 + 1],
            direction: direction(exit, entry), halfWidth: 3.5 }];
    })
};
const map = { objects, race, worldMap: grid, playerStart: { controller: 'car', x: 25.5, y: 445.5, directionX: 0, directionY: -1,
    moveSpeed: 3.2, rotSpeed: 3, collisionRadius: .15, jumpRise: .4, jumpLength: 2,
    jumpSpeedMultiplier: 1.25, crouchDrop: .25, crouchSpeedMultiplier: .5 },
    sky: { texture: 'textures/skyboxes/countryside_sky.jpg', rotationDegrees: 110 },
    raceMinimap: { route: anchors.map(([x, y]) => [x + 1, y + 1]),
        startFinish: [25.5, 442.5], shortcuts: [[shortcut.from, shortcut.to]] } };
const pretty = JSON.stringify(map, null, 2), before = pretty.indexOf('  "worldMap": ['), after = pretty.indexOf('  "playerStart":', before);
const columns = '  "worldMap": [\n' + grid.map(column => '    [' + column.join(', ') + ']').join(',\n') + '\n  ],\n';
fs.writeFileSync(path.join(web, 'maps/pine-loop.json'), pretty.slice(0, before) + columns + pretty.slice(after) + '\n');

const zones = [
    { name: 'Paddock and spectator straight', checkpoint: [25, 426], bounds: [12, 385, 44, 456] },
    { name: 'Meadow loop', checkpoint: [101, 320], bounds: [24, 280, 100, 408] },
    { name: 'Woodland loop', checkpoint: [65, 215], bounds: [64, 184, 176, 248] },
    { name: 'Stone Cut quarry and tunnel', checkpoint: [92, 45], bounds: [62, 33, 124, 80] },
    { name: 'Northern bowl', checkpoint: [150, 45], bounds: [24, 44, 232, 116] },
    { name: 'Marshal Village', checkpoint: [209, 194], bounds: [184, 181, 214, 212] },
    { name: 'Spectator Plaza / central loop', checkpoint: [177, 270], bounds: [140, 248, 208, 344] },
    { name: 'Eastern straight / service yard', checkpoint: [233, 384], bounds: [192, 116, 234, 448] },
    { name: 'Southern sweep', checkpoint: [147, 479], bounds: [24, 448, 232, 480] },
];
const fixture = { dimensions: [width, height], lap: anchors.map(([x, y]) => [x + 1, y + 1]),
    majorCorners: corners, bendRadii, zones, shortcut, topologyGates, eventPlacements, bendRailCells,
    cornerCheckpoints: bendMidpoints.map(([x, y]) => [x + 1, y + 1]),
    safetyRailCells: [...railCells].map(point => point.split(',').map(Number)),
    tunnel: { left: 82, right: 103, top: 44, bottom: 45 },
    pitEntrances: [[34, 412], [34, 416], [34, 420]], bufferCoordinate: [35, 404], bufferSpriteId: 525,
    poiApproaches: [
        { name: 'Covered pit box', from: [31.5, 416.5], to: [35.5, 416.5] },
        { name: 'Marshal Village hut', from: [183.5, 187.5], to: [191.5, 187.5] },
        { name: 'Spectator Plaza', from: [177.5, 269.5], to: [185.5, 269.5] },
        { name: 'Maintenance Yard aisle', from: [233.5, 384.5], to: [215.5, 384.5] },
    ] };
fs.mkdirSync(path.join(repository, 'Tests/Fixtures'), { recursive: true });
fs.mkdirSync(path.join(repository, 'artifacts'), { recursive: true });
const views = [['start', [25.5, 445.5, 0, -1]], ['narrowing', [25.5, 399.5, 0, -1]],
    ['grandstand-row', [25.5, 438.5, -.8, -.6]], ['grandstand-front', [25.5, 418.5, -1, 0]],
    ['pits', [31.5, 416.5, 1, 0]], ['race-control', [25.5, 391.5, 1, 0]],
    ['meadow-loop', [101, 320, 0, 1]], ['mud-express', [101, 321, 1, 0]], ['woodland-loop', [65, 215, 0, -1]],
    ['quarry', [76.5, 45, 0, 1]], ['tunnel-entry', [79.5, 45, 1, 0]], ['tunnel-interior', [92.5, 45, 1, 0]],
    ['northern-bowl', [150, 45, 1, 0]], ['village', [183.5, 187.5, 1, 0]],
    ['village-stand', [201.5, 213, 0, -1]], ['spectator-plaza', [177, 270, 1, 0]],
    ['plaza-courtyard', [185.5, 269.5, .8, -.6]], ['maintenance-yard', [233.5, 384.5, -1, 0]],
    ['eastern-straight', [233, 150, 0, 1]], ['central-loop', [141, 320, 0, -1]],
    ['south-loop', [147, 479, -1, 0]], ['tires', [100.5, 365.5, -.7, -.7]],
    ['tire-bend', [...fixture.cornerCheckpoints[2], Math.SQRT1_2, Math.SQRT1_2]],
    ['buffer-front', [37.5, 404.5, -1, 0]], ['buffer-side', [35.5, 406.5, 0, -1]]];
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
const shortcutLine = `<path d="M202 642H282" stroke="#e6bb68" stroke-width="5" stroke-dasharray="6 4"/><text x="208" y="630" font-family="Arial" font-size="12" fill="#ffe4a6">Mud Express</text>`;
const overview = `<svg xmlns="http://www.w3.org/2000/svg" width="930" height="1080" viewBox="0 0 930 1080"><rect width="930" height="1080" fill="#182f2a"/><rect x="1" y="1" width="510" height="1022" fill="#345441" stroke="#6b8266"/><polyline points="${linePoints}" fill="none" stroke="#a88d5b" stroke-width="8" stroke-linejoin="round"/><polyline points="${linePoints}" fill="none" stroke="#3b4444" stroke-width="4" stroke-linejoin="round"/>${shortcutLine}<path d="M48 884H54" stroke="#f4e9cf" stroke-width="6"/>${labels}<text x="540" y="42" fill="#f3ead1" font-family="Arial" font-size="28" font-weight="bold">PINE LOOP</text><text x="540" y="69" fill="#b3c4b5" font-family="Arial" font-size="16">256 × 512 · ${lapLength} unit lap</text><text x="540" y="530" fill="#c4d1c1" font-family="Arial" font-size="16">${corners.length} major bends · ${treeCount} trees</text><text x="540" y="558" fill="#c4d1c1" font-family="Arial" font-size="16">${grandstands} larger grandstand modules</text><text x="540" y="586" fill="#c4d1c1" font-family="Arial" font-size="16">${eventPlacements.length} event props · continuous safety rails</text><text x="540" y="630" fill="#e6bb68" font-family="Arial" font-size="15">Dashed gold: the only loop shortcut.</text><text x="540" y="665" fill="#c4d1c1" font-family="Arial" font-size="15">North is up. Road width varies: 1–3 blocks.</text><text x="540" y="691" fill="#c4d1c1" font-family="Arial" font-size="15">Start: 4 lanes, narrowing in stages.</text></svg>\n`;
fs.writeFileSync(path.join(repository, 'artifacts/pine-loop-overview.svg'), overview);
console.log(`Pine Loop: ${width}x${height}, ${track.size} main-road cells, ${lapLength}-unit lap, ${corners.length} major bends, ${treeCount} trees, ${spriteCount} sprites, ${grandstands} grandstand modules.`);

// Reusable spectator/event props for the raceway. Geometry is authored through
// existing sprites/groups; only the flags and crowd use timed atlas frames.
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { sizeRacewayObject } from './size-raceway-props.mjs';

const repository = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const web = path.join(repository, 'WolfNET.Game/wwwroot');
const objectDirectory = path.join(web, 'objects/raceway');
const textureDirectory = path.join(web, 'textures/raceway');

export const racewayEventIds = Object.freeze({
    flag: 641, flagAlternate: 656, flagThird: 657,
    crowd: 642, crowdAlternate: 643, crowdThird: 644, startLight: 645,
    gripX: 646, gripY: 647, brakeX: 648, brakeY: 649,
    mudX: 650, mudY: 651, apexX: 652, apexY: 653,
});

const atlas = () => ({ directions: 1, frames: [0, 1, 2].map(index => ({
    x: index * 512, y: 0, width: 512, height: 512,
})) });

function boardArt({ background, accent, title, size, lines, icon }) {
    const checks = Array.from({ length: 24 }, (_, i) => `<rect x="${24 + i * 20}" y="104" width="20" height="20" fill="${i % 2 ? '#243237' : '#f6efd5'}"/>`).join('');
    return `<svg xmlns="http://www.w3.org/2000/svg" width="512" height="512" viewBox="0 0 512 512">
<defs><linearGradient id="post" x2="1"><stop stop-color="#334047"/><stop offset=".45" stop-color="#849191"/><stop offset="1" stop-color="#3b4b50"/></linearGradient><linearGradient id="face" x2="0" y2="1"><stop stop-color="${background}"/><stop offset="1" stop-color="#17262d"/></linearGradient></defs>
<path d="M100 334H124V504H100ZM388 334H412V504H388Z" fill="url(#post)"/>
<path d="M90 503H134V511H90ZM378 503H422V511H378Z" fill="#3b403d"/>
<rect x="14" y="94" width="484" height="262" rx="5" fill="#253138"/>
<rect x="21" y="101" width="470" height="246" rx="3" fill="url(#face)" stroke="#d3d5c1" stroke-width="7"/>
${checks}<path d="M24 126H488" stroke="${accent}" stroke-width="5"/>
<text x="256" y="192" text-anchor="middle" fill="#fff4d4" font-family="Arial,sans-serif" font-weight="900" font-size="${size}" letter-spacing="-1">${title}</text>
${icon}
${lines.map((line, index) => `<text x="256" y="${292 + index * 26}" text-anchor="middle" fill="#fff4d4" font-family="Arial,sans-serif" font-weight="700" font-size="22">${line}</text>`).join('')}
<path d="M29 338H483" stroke="${accent}" stroke-width="6"/>
${[[33, 137], [479, 137], [33, 329], [479, 329]].map(([x, y]) => `<circle cx="${x}" cy="${y}" r="4" fill="#a9b9b9"/><path d="M${x - 2} ${y + 2}l4 -4" stroke="#37484e"/>`).join('')}
</svg>\n`;
}

const advertisements = [
    { name: 'grip-happens', title: 'GRIP HAPPENS', size: 48, background: '#195954', accent: '#e9bf51',
        lines: ['TIRES WITH A', 'POSITIVE ATTITUDE.'],
        icon: '<g transform="translate(256 235)"><circle r="27" fill="#1a282b" stroke="#d9e5cd" stroke-width="7"/><circle r="12" fill="#cf9b3c"/><path d="M-40 0H-64M40 0H64M-33 -16H-52M33 16H52" stroke="#e9bf51" stroke-width="7" stroke-linecap="round"/></g>' },
    { name: 'brake-ish', title: 'BRAKE-ish', size: 64, background: '#9a4530', accent: '#ffc45b',
        lines: ["WE'LL STOP", 'EVENTUALLY.'],
        icon: '<g transform="translate(256 235)"><circle r="29" fill="#e1d2ae"/><circle r="19" fill="#6c352b"/><circle r="6" fill="#e1d2ae"/><path d="M10 -29H28V15H10Z" fill="#ffc45b"/><path d="M-48 -14H-77M-44 0H-88M-48 14H-67" stroke="#ffc45b" stroke-width="6" stroke-linecap="round"/></g>' },
    { name: 'mud-club', title: 'MUD CLUB', size: 62, background: '#705735', accent: '#c9d568',
        lines: ['FREE DIRT.', 'STRICT DRESS CODE.'],
        icon: '<g fill="#c9d568"><path d="M184 258l35 -36 31 20 30 -32 50 48Z"/><circle cx="207" cy="212" r="6"/><circle cx="278" cy="199" r="5"/><circle cx="312" cy="228" r="8"/></g>' },
    { name: 'apex-academy', title: 'APEX ACADEMY', size: 45, background: '#315b79', accent: '#f0a855',
        lines: ['CUT TUITION.', 'NOT CORNERS.'],
        icon: '<path d="M196 254l35 -43 26 34 27 -29 32 38" fill="none" stroke="#f0a855" stroke-width="11" stroke-linejoin="round"/><path d="M306 243l11 11 -3 -18" fill="#f0a855"/>' },
];

function startLightArt() {
    return `<svg xmlns="http://www.w3.org/2000/svg" width="512" height="512" viewBox="0 0 512 512">
<defs><linearGradient id="metal" x2="1"><stop stop-color="#1d2b31"/><stop offset=".5" stop-color="#7c9090"/><stop offset="1" stop-color="#263c43"/></linearGradient><radialGradient id="lamp"><stop stop-color="#fff0ba"/><stop offset=".25" stop-color="#ff7a4d"/><stop offset=".7" stop-color="#d72820"/><stop offset="1" stop-color="#721b20"/></radialGradient></defs>
<path d="M245 145H267V505H245Z" fill="url(#metal)"/>
<path d="M226 499H286V511H226Z" fill="#333f3e"/>
<path d="M234 174L76 111M278 174L436 111" stroke="#32464e" stroke-width="9"/>
<rect x="24" y="45" width="464" height="114" rx="9" fill="#17272e" stroke="#879a94" stroke-width="5"/>
<path d="M35 54H477" stroke="#d4c8a4" stroke-width="5"/>
${[72, 164, 256, 348, 440].map(x => `<circle cx="${x}" cy="105" r="35" fill="#101d25" stroke="#3a4c50" stroke-width="5"/><path d="M${x - 35} 100a35 35 0 0 1 70 0" fill="none" stroke="#0c1920" stroke-width="12"/><circle cx="${x}" cy="108" r="24" fill="url(#lamp)"/><circle cx="${x - 6}" cy="100" r="5" fill="#ffd2a7" opacity=".75"/>`).join('')}
<rect x="190" y="205" width="132" height="53" rx="4" fill="#f0dcb2" stroke="#32464e" stroke-width="5"/><text transform="translate(512 0) scale(-1 1)" x="256" y="240" text-anchor="middle" font-family="Arial,sans-serif" font-size="28" font-weight="900" fill="#293e44">START</text>
<path d="M249 280H263V300H249ZM249 320H263V340H249ZM249 360H263V380H249Z" fill="#e3bb5b"/>
</svg>\n`;
}

/** Write the shared objects/art, and return suggested trackside placements.
 * The map builder chooses the closest free terrain within searchRadius, allowing
 * rails, the driving surface, and POI entrances to retain priority.
 */
export function writeRacewayEventProps() {
    fs.mkdirSync(objectDirectory, { recursive: true });
    fs.mkdirSync(textureDirectory, { recursive: true });
    const definitions = new Map();
    function object(name, id, type, fields) {
        const value = { type, id, raycastTransparent: true, collides: false,
            jumpable: false, crouchable: false, ...fields };
        sizeRacewayObject(value);
        fs.writeFileSync(path.join(objectDirectory, `${name}.json`), JSON.stringify(value, null, 2) + '\n');
        definitions.set(id, value);
    }
    function group(name, id, spriteId, scale, baseline = 512, xScale = scale) {
        object(name, id, 'spriteGroup', { passageId: 501, sprites: [{ spriteId,
            offset: { x: 0, y: 0, z: -.5 + scale * (baseline / 512 - .5) },
            scale: { x: xScale, y: scale } }] });
    }
    for (const [spriteId, groupId, phase, suffix] of [[630, 641, 0, ''], [654, 656, .25, '-alternate'], [655, 657, .5, '-third']]) {
        object(`sprite-festival-flag${suffix}`, spriteId, 'sprite', { passageId: 501,
            texture: 'textures/raceway/festival-flags-atlas.png', textureLayer: 130,
            atlas: atlas(), animation: { framesPerSecond: 4, phaseSeconds: phase } });
        group(`group-festival-flag${suffix}`, groupId, spriteId, 3.4, 496);
    }
    for (const [spriteId, groupId, phase, suffix] of [[631, 642, 0, ''], [658, 643, .25, '-alternate'], [659, 644, .5, '-third']]) {
        object(`sprite-cheering-crowd${suffix}`, spriteId, 'sprite', { passageId: 501,
            texture: 'textures/raceway/festival-crowd-atlas.png', textureLayer: 133,
            atlas: atlas(), animation: { framesPerSecond: 4, phaseSeconds: phase } });
        group(`group-cheering-crowd${suffix}`, groupId, spriteId, 1.2, 496);
    }
    fs.writeFileSync(path.join(textureDirectory, 'race-start-light.svg'), startLightArt());
    const redLight = startLightArt();
    const greenLight = redLight.replace('<svg ', '<svg x="512" ')
        .replaceAll('metal', 'green-metal').replaceAll('lamp', 'green-lamp')
        .replaceAll('#fff0ba', '#dcffbf').replaceAll('#ff7a4d', '#9fff62')
        .replaceAll('#d72820', '#29d538').replaceAll('#721b20', '#146b26')
        .replaceAll('#ffd2a7', '#d2ffba');
    fs.writeFileSync(path.join(textureDirectory, 'race-start-light-atlas.svg'),
        `<svg xmlns="http://www.w3.org/2000/svg" width="1024" height="512" viewBox="0 0 1024 512">${redLight}${greenLight}</svg>\n`);
    object('sprite-race-start-light', 632, 'sprite', { passageId: 501,
        texture: 'textures/raceway/race-start-light-atlas.svg', textureLayer: 180, planeAxis: 'y', raceStartLight: true,
        atlas: { directions: 1, frames: [0, 512].map(x => ({x, y: 0, width: 512, height: 512})) } });
    group('group-race-start-light', 645, 632, 3.4);
    advertisements.forEach((advertisement, index) => {
        const filename = `billboard-${advertisement.name}.svg`;
        fs.writeFileSync(path.join(textureDirectory, filename), boardArt(advertisement));
        for (const [axisIndex, axis] of ['x', 'y'].entries()) {
            const spriteId = 633 + index * 2 + axisIndex, groupId = 646 + index * 2 + axisIndex;
            object(`sprite-billboard-${advertisement.name}-${axis}`, spriteId, 'sprite', {
                passageId: 501, texture: `textures/raceway/${filename}`, textureLayer: 137 + index, planeAxis: axis });
            group(`group-billboard-${advertisement.name}-${axis}`, groupId, spriteId, 3.6);
        }
    });

    const placements = [];
    const add = (id, x, y, label, searchRadius = 4) => placements.push({ id, x, y, label, searchRadius });
    add(645, 20, 438, 'Starting signal', 3);
    // Five-wide start and pit aisle stay clear; spectators face the racing line
    // from behind its barriers and around the larger spectator facilities.
    for (const [index, y] of [397, 413, 429, 447].entries()) {
        add([641, 656, 657][index % 3], 34, y, 'Paddock flag', 6);
        add([642, 643, 644][index % 3], 20, y + 3, 'Paddock spectators', 2);
    }
    add(646, 20, 391, 'Grip Happens / paddock');
    add(648, 29, 459, 'BRAKE-ish / grid approach');
    add(652, 30, 374, 'Apex Academy / paddock exit');
    for (const [index, x] of [181, 188, 196, 203].entries()) {
        add([642, 643, 644][index % 3], x, 263, 'Plaza cheering crowd', 3);
        add([641, 656, 657][index % 3], x, 280, 'Plaza flags', 3);
    }
    add(651, 188, 277, 'Mud Club / spectator plaza');
    add(649, 199, 281, 'BRAKE-ish / spectator plaza');
    for (const [index, [x, y]] of [[184, 193], [187, 204], [199, 211], [207, 190]].entries()) {
        add([642, 643, 644][index % 3], x, y, 'Village spectators', 3);
    }
    add(641, 183, 184, 'Village flag');
    add(657, 205, 207, 'Village flag');
    add(646, 214, 199, 'Grip Happens / village');
    add(653, 194, 179, 'Apex Academy / village');
    for (const [index, [x, y]] of [[76, 37], [109, 50], [119, 43], [92, 55]].entries()) {
        add([641, 656, 657][index % 3], x, y, 'Quarry flags', 3);
        if (index < 2) add([642, 643][index], x, y + 5, 'Quarry spectators', 3);
    }
    add(651, 113, 51, 'Mud Club / quarry');
    add(649, 60, 37, 'BRAKE-ish / northern bowl');
    add(647, 152, 39, 'Grip Happens / northern bowl');
    // Long-straight hoardings make its length easy to read from the cockpit.
    for (const [index, y] of [142, 196, 258, 318, 361, 425].entries()) {
        add([646, 648, 650, 652][index % 4], 238, y, 'Forest straight advertisement', 3);
        add([641, 656, 657][index % 3], 238, y + 7, 'Forest straight flag', 3);
        if (index % 2 === 0) add([642, 643, 644][index % 3], 238, y + 13, 'Forest straight spectators', 3);
    }
    add(656, 222, 387, 'Service yard flag');
    add(650, 224, 394, 'Mud Club / service yard');
    add(644, 217, 382, 'Service crew cheering', 3);
    // Individual event pockets mark the otherwise unoccupied interior loops.
    for (const [index, [x, y]] of [[96, 304], [107, 345], [60, 383], [134, 335], [68, 222], [146, 254], [170, 297], [202, 323]].entries()) {
        add([642, 643, 644][index % 3], x, y, 'Infield cheering pocket', 3);
        add([641, 656, 657][index % 3], x + (index % 2 ? 0 : 3), y + 4, 'Infield loop flag', 3);
    }
    add(646, 95, 326, 'Grip Happens / meadow loop');
    add(650, 69, 394, 'Mud Club / meadow loop');
    add(652, 134, 285, 'Apex Academy / central loop');
    add(649, 121, 178, 'BRAKE-ish / woodland loop');
    add(647, 108, 474, 'Grip Happens / southern sweep');
    add(653, 182, 484, 'Apex Academy / southern sweep');
    return { ids: racewayEventIds, definitions, placements };
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
    const { definitions, placements } = writeRacewayEventProps();
    console.log(`Raceway event props: ${definitions.size} reusable objects, ${placements.length} suggested placements.`);
}

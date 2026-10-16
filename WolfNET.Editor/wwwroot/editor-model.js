export const copy = value => structuredClone(value);
export const kinds = ["passage", "wall", "sprite", "spriteGroup"];

export function checkDefinition(definition, objects) {
    const d = definition;
    if (!d || !kinds.includes(d.type) || !Number.isInteger(d.id) || d.id < 0)
        throw Error("An object needs a supported type and a nonnegative integer id.");
    if (typeof d.collides !== "boolean" || typeof d.raycastTransparent !== "boolean")
        throw Error("collides and raycastTransparent must both be true or false.");
    if (d.type !== "passage" && (!Number.isInteger(d.passageId) || objects[d.passageId]?.type !== "passage"))
        throw Error("passageId must reference a passage in the object library.");
    if (d.type === "wall" && ("floorTexture" in d || "ceilingTexture" in d))
        throw Error("Wall surfaces use passageId. Put floorTexture and ceilingTexture on the passage.");
    if (d.type === "sprite" && (!Number.isInteger(d.textureLayer) || d.textureLayer < 0 || d.textureLayer > 255))
        throw Error("A sprite textureLayer must be an integer from 0 to 255.");
    if (d.type === "spriteGroup") {
        if (!Array.isArray(d.sprites)) throw Error("A sprite group needs a sprites array.");
        for (const member of d.sprites)
            if (!member || objects[member.spriteId]?.type !== "sprite" ||
                !Number.isFinite(member.offset?.x) || !Number.isFinite(member.offset?.y))
                throw Error("Each group member needs a valid spriteId and a finite { x, y } offset.");
    }
    const paths = d.type === "passage" ? [d.floorTexture, d.ceilingTexture] :
        d.type === "wall" ? Object.values(d.textures ?? {}) : d.type === "sprite" ? [d.texture] : [];
    if (paths.some(path => path != null && typeof path !== "string")) throw Error("Texture paths must be strings or null.");
    return d;
}

export function checkGrid(map) {
    const grid = map?.worldMap;
    if (!Array.isArray(grid) || grid.length < 3 || grid.length > 64 || !Array.isArray(grid[0]) || grid[0].length < 3 || grid[0].length > 64 ||
        grid.some(column => !Array.isArray(column) || column.length !== grid[0].length || column.some(id => !Number.isInteger(id) || id < 0)))
        throw Error("The map needs a rectangular worldMap[x][y] grid, 3–64 cells per side, containing nonnegative object IDs.");
    const p = map.playerStart;
    if (!p || [p.x, p.y, p.directionX, p.directionY, p.moveSpeed, p.rotSpeed].some(value => !Number.isFinite(value)) ||
        p.moveSpeed < 0 || p.rotSpeed < 0 || Math.hypot(p.directionX, p.directionY) === 0)
        throw Error("playerStart needs finite coordinates, a nonzero direction, and nonnegative movement/turn speeds.");
    return map;
}

export function dependencies(d) {
    return d.type === "passage" ? [] : d.type === "spriteGroup" ? [d.passageId, ...d.sprites.map(s => s.spriteId)] : [d.passageId];
}

export function usedObjects(document) {
    checkGrid(document.map);
    const result = {}, pending = [...new Set(document.map.worldMap.flat())];
    for (let index = 0; index < pending.length; index++) {
        const id = pending[index];
        if (Object.hasOwn(result, id)) continue;
        const d = document.objects[id];
        if (!d || d.id !== id) throw Error(`Missing definition for object ${id}.`);
        checkDefinition(d, document.objects);
        result[id] = d;
        pending.push(...dependencies(d));
    }
    return result;
}

export function flood(grid, x, y, replacement) {
    const original = grid[x][y];
    if (original === replacement) return false;
    const pending = [[x, y]];
    grid[x][y] = replacement;
    for (let i = 0; i < pending.length; i++) {
        const [cx, cy] = pending[i];
        for (const [nx, ny] of [[cx - 1, cy], [cx + 1, cy], [cx, cy - 1], [cx, cy + 1]])
            if (nx >= 0 && ny >= 0 && nx < grid.length && ny < grid[0].length && grid[nx][ny] === original) {
                grid[nx][ny] = replacement;
                pending.push([nx, ny]);
            }
    }
    return true;
}

export function resizeGrid(grid, width, height, floor, wall) {
    if (![width, height].every(n => Number.isInteger(n) && n >= 3 && n <= 64)) throw Error("Dimensions must be integers from 3 to 64.");
    return Array.from({ length: width }, (_, x) => Array.from({ length: height }, (_, y) => {
        if (x === 0 || y === 0 || x === width - 1 || y === height - 1) return wall;
        // Former boundaries become floor when expanding.
        if (x > 0 && y > 0 && x < grid.length - 1 && y < grid[0].length - 1) return grid[x][y];
        return floor;
    }));
}

export function line(x0, y0, x1, y1, visit) {
    const dx = Math.abs(x1 - x0), dy = -Math.abs(y1 - y0), sx = x0 < x1 ? 1 : -1, sy = y0 < y1 ? 1 : -1;
    let error = dx + dy;
    while (true) {
        visit(x0, y0);
        if (x0 === x1 && y0 === y1) break;
        const twice = 2 * error;
        if (twice >= dy) { error += dy; x0 += sx; }
        if (twice <= dx) { error += dx; y0 += sy; }
    }
}

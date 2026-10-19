import { copy, checkDefinition, checkGrid, dependencies, usedObjects, flood, resizeGrid, line, objectUsage, removeObject } from "./editor-model.js";

const $ = id => document.getElementById(id);
const canvas = $("map-canvas"), ctx = canvas.getContext("2d");
let doc, library, brush = 0, tool = "brush", cellSize = 32, dirty = false, definitionDirty = false;
let undo = [], redo = [], gesture, hover, drawing = false, renderPending = false, toastTimer, draftTimer;
let savedObjectState = "", mapNames = [], busy = false;
const images = new Map(), draftKey = "wolfnet-editor-draft-v1", maxHistory = 80;

async function api(path, body) {
    const response = await fetch(path, body === undefined ? {} : {
        method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify(body)
    });
    if (!response.ok) {
        const text = await response.text();
        let message = text;
        try { message = JSON.parse(text).error ?? text; } catch {}
        throw Error(message || `Request failed (${response.status}).`);
    }
    return response.json();
}

function toast(message) {
    $("toast").textContent = message;
    $("toast").hidden = false;
    clearTimeout(toastTimer);
    toastTimer = setTimeout(() => $("toast").hidden = true, 4500);
}
function report(error) {
    $("validation").className = "validation error";
    $("validation").textContent = error.message;
    toast(error.message);
}
const act = fn => async event => { try { await fn(event); } catch (error) { report(error); } };
function assertReady() {
    if (busy) throw Error("Wait for the current file operation to finish.");
    if (!doc) throw Error("Wait for the map to finish loading.");
    if (definitionDirty) throw Error("Apply the object definition or select it again to discard its text changes first.");
}
function allowDiscard() {
    return !(dirty || definitionDirty) || confirm("Discard the current unsaved changes?");
}

function objectState() { return JSON.stringify(doc.objects); }
function commit(before) {
    if (JSON.stringify(before) === JSON.stringify(doc)) return;
    updateRegistry();
    undo.push(before);
    if (undo.length > maxHistory) undo.shift();
    redo = [];
    dirty = true;
    $("validation").textContent = "Changes have not been validated.";
    $("validation").className = "validation";
    refresh();
    queueDraft();
}
function change(fn) {
    assertReady();
    const before = copy(doc);
    fn();
    commit(before);
}
function queueDraft() {
    clearTimeout(draftTimer);
    draftTimer = setTimeout(() => {
        try { localStorage.setItem(draftKey, JSON.stringify({ document: doc, savedObjectState })); }
        catch { toast("Browser draft storage is unavailable. Save or export your work."); }
    }, 400);
}
function clearDraft() {
    clearTimeout(draftTimer);
    try { localStorage.removeItem(draftKey); } catch {}
    $("draft-banner").hidden = true;
}
function history(backward) {
    assertReady();
    const source = backward ? undo : redo, destination = backward ? redo : undo;
    if (!source.length) return;
    destination.push(copy(doc));
    doc = source.pop();
    dirty = true;
    $("validation").textContent = "Changes have not been validated.";
    $("validation").className = "validation";
    if (!doc.objects[brush]) brush = Number(Object.keys(doc.objects)[0]);
    refresh(true);
    queueDraft();
}

function title(id) {
    const path = doc.map.objects[id] ?? library.objects.find(entry => entry.definition.id === Number(id))?.path;
    const filename = path?.split("/").at(-1)?.replace(/\.json$/, "");
    return filename && !/^\d+-[a-f0-9]+$/.test(filename) ? filename.replace(/-/g, " ") : `${doc.objects[id]?.type ?? "object"} ${id}`;
}
function texture(d) {
    if (!d) return null;
    if (d.type === "passage") return d.floorTexture;
    if (d.type === "wall") return d.textures?.north ?? d.textures?.east;
    if (d.type === "spriteGroup") return texture(doc.objects[d.sprites?.[0]?.spriteId]);
    return d.texture;
}
function textureUrl(path) {
    if (!path) return null;
    const url = new URL(path.replaceAll("\\", "/"), `${location.origin}/engine/`);
    return url.origin === location.origin && url.pathname.startsWith("/engine/") ? url.href : null;
}
function image(path) {
    const url = textureUrl(path);
    if (!url) return null;
    if (!images.has(url)) {
        const img = new Image();
        img.onload = requestDraw;
        img.onerror = requestDraw;
        img.src = url;
        images.set(url, img);
    }
    const img = images.get(url);
    return img.complete && img.naturalWidth > 0 ? img : null;
}
function palette() {
    const query = $("search").value.toLowerCase(), type = $("type-filter").value;
    const fragment = document.createDocumentFragment();
    let count = 0;
    for (const d of Object.values(doc.objects).sort((a, b) => a.id - b.id)) {
        const name = title(d.id);
        if ((type && d.type !== type) || !`${name} ${d.id} ${d.type}`.toLowerCase().includes(query)) continue;
        count++;
        const button = document.createElement("button");
        button.className = `palette-item${d.id === brush ? " active" : ""}`;
        button.dataset.id = d.id;
        button.setAttribute("aria-pressed", String(d.id === brush));
        const thumb = document.createElement("img");
        thumb.className = "object-thumb";
        thumb.alt = "";
        const url = textureUrl(texture(d));
        if (url) thumb.src = url; else thumb.hidden = true;
        thumb.onerror = () => thumb.hidden = true;
        const text = document.createElement("span"), heading = document.createElement("span"), meta = document.createElement("span");
        heading.className = "object-title";
        heading.textContent = name;
        meta.className = "object-meta";
        meta.textContent = `#${d.id} · ${d.type} · ${d.collides ? "solid" : "walkable"}`;
        text.append(heading, meta);
        button.append(thumb, text);
        button.onclick = () => select(d.id);
        fragment.append(button);
    }
    if (!count) { const message = document.createElement("p"); message.textContent = "No matching objects."; fragment.append(message); }
    $("palette").replaceChildren(fragment);
    $("object-count").textContent = count;
}
function select(id, force = false) {
    if (!force && definitionDirty && !confirm("Discard unapplied definition text?")) return;
    brush = Number(id);
    definitionDirty = false;
    const d = doc.objects[brush];
    $("selected-id").textContent = `#${brush}`;
    $("object-json").value = JSON.stringify(d, null, 2);
    $("object-summary").textContent = `${title(brush)} · ${d.collides ? "Blocks movement" : "Walkable"} · ${d.raycastTransparent ? "Rays pass through" : "Stops rays"} · ${d.jumpable ? "Jumpable" : "Not jumpable"} · ${d.crouchable ? "Crouchable" : "Not crouchable"}`;
    const fields = d.type === "passage" ? ["floorTexture", "ceilingTexture"] : d.type === "wall" ? ["north", "east", "south", "west"] : d.type === "sprite" ? ["texture"] : [];
    $("texture-picker").hidden = !fields.length;
    $("texture-target").replaceChildren(...fields.map(field => {
        const option = document.createElement("option"); option.value = field; option.textContent = field; return option;
    }));
    palette();
    requestDraw();
}
function refresh(resetInspector = false) {
    if (!doc) return;
    const map = doc.map, p = map.playerStart;
    $("document-title").textContent = doc.name;
    if (mapNames.includes(doc.name)) $("map-list").value = doc.name;
    $("dirty-dot").hidden = !dirty;
    $("map-dimensions").textContent = `${map.worldMap.length} × ${map.worldMap[0].length}`;
    $("map-name").value = doc.name;
    $("map-width").value = map.worldMap.length;
    $("map-height").value = map.worldMap[0].length;
    $("spawn-x").value = p.x;
    $("spawn-y").value = p.y;
    $("move-speed").value = p.moveSpeed;
    $("rot-speed").value = p.rotSpeed;
    $("collision-radius").value = p.collisionRadius ?? 0.15;
    $("jump-rise").value = p.jumpRise ?? 0.4;
    $("jump-length").value = p.jumpLength ?? 2;
    $("jump-speed-multiplier").value = p.jumpSpeedMultiplier ?? 1.25;
    $("crouch-drop").value = p.crouchDrop ?? 0.25;
    $("crouch-speed-multiplier").value = p.crouchSpeedMultiplier ?? 0.5;
    $("sky-texture").value = map.sky?.texture ?? "";
    $("sky-rotation").value = map.sky?.rotationDegrees ?? 0;
    $("sky-rotation").disabled = !map.sky;
    const facing = `${p.directionX},${p.directionY}`;
    $("spawn-facing").value = ["-1,0", "0,-1", "1,0", "0,1"].includes(facing) ? facing : "custom";
    $("undo").disabled = !undo.length;
    $("redo").disabled = !redo.length;
    $("zoom-label").textContent = `${Math.round(cellSize / 32 * 100)}%`;
    if (resetInspector) select(brush, true);
    requestDraw();
}

function requestDraw() {
    if (!renderPending) { renderPending = true; requestAnimationFrame(() => { renderPending = false; draw(); }); }
}
function drawTexture(path, x, y, size, alpha = 1) {
    const img = image(path);
    ctx.save(); ctx.globalAlpha = alpha;
    if (img) ctx.drawImage(img, x, y, size, size);
    else { ctx.fillStyle = path ? "#9d477e" : "#424452"; ctx.fillRect(x, y, size, size); }
    ctx.restore();
}
function draw() {
    if (!doc) return;
    const grid = doc.map.worldMap, width = grid.length, height = grid[0].length, s = cellSize, dpr = devicePixelRatio || 1;
    canvas.style.width = `${width * s}px`; canvas.style.height = `${height * s}px`;
    if (canvas.width !== Math.round(width * s * dpr) || canvas.height !== Math.round(height * s * dpr)) {
        canvas.width = Math.round(width * s * dpr); canvas.height = Math.round(height * s * dpr);
    }
    ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
    ctx.imageSmoothingEnabled = false;
    ctx.clearRect(0, 0, width * s, height * s);
    for (let x = 0; x < width; x++) for (let y = 0; y < height; y++) {
        const d = doc.objects[grid[x][y]], px = x * s, py = y * s;
        if (!d) { ctx.fillStyle = "#f04db5"; ctx.fillRect(px, py, s, s); continue; }
        const passage = d.type === "passage" ? d : doc.objects[d.passageId];
        drawTexture(passage?.floorTexture, px, py, s, .4);
        if (d.type === "wall") {
            if (!d.raycastTransparent) {
                drawTexture(texture(d), px + 1, py + 1, s - 2, .95);
                ctx.strokeStyle = "#c3d2df70"; ctx.strokeRect(px + 1.5, py + 1.5, s - 3, s - 3);
            } else {
                ctx.strokeStyle = "#70dfc4"; ctx.setLineDash([s / 7, s / 7]);
                ctx.strokeRect(px + 3, py + 3, s - 6, s - 6); ctx.setLineDash([]);
            }
        }
        if ($("show-collision").checked && d.collides) {
            ctx.fillStyle = "#fb9b653b"; ctx.fillRect(px, py, s, s);
            ctx.strokeStyle = "#ffb078"; ctx.lineWidth = 1;
            ctx.beginPath(); ctx.moveTo(px + 4, py + 4); ctx.lineTo(px + s - 4, py + s - 4);
            ctx.moveTo(px + s - 4, py + 4); ctx.lineTo(px + 4, py + s - 4); ctx.stroke();
        }
        if ($("show-rays").checked && !d.raycastTransparent) {
            ctx.strokeStyle = "#e494f1"; ctx.lineWidth = 2; ctx.strokeRect(px + 2, py + 2, s - 4, s - 4); ctx.lineWidth = 1;
        }
    }
    function sprite(d, x, y, scale = {}) {
        const img = image(d?.texture), size = s * .72;
        const width = size * (scale.x ?? 1), height = size * (scale.y ?? 1);
        if (width === 0 || height === 0) return;
        if (img) ctx.drawImage(img, x * s - width / 2, y * s - height / 2, width, height);
        else { ctx.fillStyle = "#efb760"; ctx.beginPath(); ctx.arc(x * s, y * s, s * .15, 0, Math.PI * 2); ctx.fill(); }
    }
    for (let x = 0; x < width; x++) for (let y = 0; y < height; y++) {
        const d = doc.objects[grid[x][y]];
        if (d?.type === "sprite") sprite(d, x + .5, y + .5);
        if (d?.type === "spriteGroup") for (const member of d.sprites ?? [])
            sprite(doc.objects[member.spriteId], x + .5 + member.offset.x, y + .5 + member.offset.y, member.scale);
    }
    if ($("show-grid").checked) {
        ctx.strokeStyle = "#10182375"; ctx.lineWidth = 1; ctx.beginPath();
        for (let x = 0; x <= width; x++) { ctx.moveTo(x * s + .5, 0); ctx.lineTo(x * s + .5, height * s); }
        for (let y = 0; y <= height; y++) { ctx.moveTo(0, y * s + .5); ctx.lineTo(width * s, y * s + .5); }
        ctx.stroke();
    }
    if ($("show-ids").checked && s >= 20) {
        ctx.font = `${Math.min(12, s * .3)}px Consolas, monospace`; ctx.textAlign = "center"; ctx.textBaseline = "middle";
        for (let x = 0; x < width; x++) for (let y = 0; y < height; y++) {
            ctx.fillStyle = "#08121cb0"; ctx.fillRect(x * s + 2, y * s + 2, s - 4, s - 4);
            ctx.fillStyle = "#e3edf6"; ctx.fillText(grid[x][y], (x + .5) * s, (y + .5) * s);
        }
    }
    const p = doc.map.playerStart;
    ctx.save(); ctx.translate(p.x * s, p.y * s); ctx.rotate(Math.atan2(p.directionY, p.directionX));
    ctx.fillStyle = "#8df5de"; ctx.strokeStyle = "#102e31"; ctx.lineWidth = 2;
    ctx.beginPath(); ctx.moveTo(s * .4, 0); ctx.lineTo(-s * .27, -s * .25); ctx.lineTo(-s * .17, 0); ctx.lineTo(-s * .27, s * .25); ctx.closePath(); ctx.fill(); ctx.stroke(); ctx.restore();
    if (hover) {
        ctx.strokeStyle = "#e7f4ff"; ctx.lineWidth = 2;
        ctx.strokeRect(hover.x * s + 1, hover.y * s + 1, s - 2, s - 2); ctx.lineWidth = 1;
    }
}

function setTool(value) {
    tool = value;
    for (const button of document.querySelectorAll("[data-tool]")) {
        button.classList.toggle("active", button.dataset.tool === tool);
        button.setAttribute("aria-pressed", String(button.dataset.tool === tool));
    }
}
function cellAt(event) {
    if (!doc) return null;
    const bounds = canvas.getBoundingClientRect(), x = Math.floor((event.clientX - bounds.left) / cellSize), y = Math.floor((event.clientY - bounds.top) / cellSize);
    return x >= 0 && y >= 0 && x < doc.map.worldMap.length && y < doc.map.worldMap[0].length ? { x, y } : null;
}
canvas.addEventListener("contextmenu", event => event.preventDefault());
canvas.addEventListener("pointerdown", act(event => {
    if (event.button !== 0 && event.button !== 2) return;
    assertReady();
    const cell = cellAt(event);
    if (!cell) return;
    event.preventDefault(); canvas.focus();
    const { x, y } = cell;
    if (event.button === 2 || event.altKey || tool === "pick") { select(doc.map.worldMap[x][y]); return; }
    if (tool === "fill") { change(() => flood(doc.map.worldMap, x, y, brush)); return; }
    if (tool === "spawn") {
        if (doc.objects[doc.map.worldMap[x][y]]?.collides) throw Error("Place the player in a walkable cell.");
        change(() => { doc.map.playerStart.x = x + .5; doc.map.playerStart.y = y + .5; }); return;
    }
    gesture = { before: copy(doc), last: cell };
    drawing = true;
    canvas.setPointerCapture(event.pointerId);
    doc.map.worldMap[x][y] = brush;
    requestDraw();
}));
canvas.addEventListener("pointermove", event => {
    const cell = cellAt(event); hover = cell;
    if (cell) {
        const id = doc.map.worldMap[cell.x][cell.y], d = doc.objects[id];
        $("cursor-status").textContent = `(${cell.x}, ${cell.y}) · #${id} ${d?.type ?? "unknown"} · ${d?.collides ? "solid" : "walkable"}`;
        if (drawing && gesture) {
            line(gesture.last.x, gesture.last.y, cell.x, cell.y, (x, y) => doc.map.worldMap[x][y] = brush);
            gesture.last = cell;
        }
    }
    requestDraw();
});
function endGesture() {
    if (!gesture) return;
    const before = gesture.before; gesture = null; drawing = false; commit(before);
}
canvas.addEventListener("pointerup", endGesture);
canvas.addEventListener("pointercancel", endGesture);
canvas.addEventListener("lostpointercapture", endGesture);
canvas.addEventListener("pointerleave", () => { hover = null; requestDraw(); });

function fit() {
    if (!doc) return;
    cellSize = Math.max(8, Math.min(64, Math.floor(Math.min(($("viewport").clientWidth - 70) / doc.map.worldMap.length, ($("viewport").clientHeight - 70) / doc.map.worldMap[0].length))));
    refresh();
}
function zoom(delta) { cellSize = Math.max(8, Math.min(80, cellSize + delta)); refresh(); }
function defaults() {
    const values = Object.values(doc.objects);
    const floor = values.find(d => d.type === "passage" && !d.collides && d.raycastTransparent)?.id;
    const wall = values.find(d => d.type === "wall" && d.collides && !d.raycastTransparent)?.id;
    if (floor == null || wall == null) throw Error("The library needs a walkable passage and a solid ray-blocking wall.");
    return { floor, wall };
}
function mergedObjects(objects, deletedIds = []) {
    const deleted = new Set(deletedIds);
    return { ...Object.fromEntries(library.objects.filter(entry => !deleted.has(entry.definition.id)).map(entry => [entry.definition.id, copy(entry.definition)])), ...objects };
}
function updateRegistry() {
    for (const d of Object.values(doc.objects))
        doc.map.objects[d.id] ??= library.objects.find(entry => entry.definition.id === d.id)?.path ?? `../objects/editor/${doc.name}/${d.id}.json`;
}
function install(document, unsaved = false) {
    checkGrid(document.map);
    const next = copy(document);
    next.map.objects ??= {};
    next.deletedObjectIds ??= next.map.editor?.deletedObjectIds ?? [];
    // Restore older browser drafts against the replacement texture library.
    const migrateTexture = path => typeof path === "string" && path.startsWith("pics/") &&
        library.textures.includes("textures/" + path.slice(5)) ? "textures/" + path.slice(5) : path;
    for (const d of Object.values(next.objects)) {
        for (const field of ["texture", "floorTexture", "ceilingTexture"])
            if (field in d) d[field] = migrateTexture(d[field]);
        if (d.textures) for (const field of Object.keys(d.textures)) d.textures[field] = migrateTexture(d.textures[field]);
    }
    next.objects = mergedObjects(next.objects, next.deletedObjectIds);
    usedObjects(next);
    doc = next;
    updateRegistry();
    undo = []; redo = []; dirty = unsaved; definitionDirty = false;
    savedObjectState = objectState();
    brush = doc.objects[0] ? 0 : Number(Object.keys(doc.objects)[0]);
    refresh(true); fit();
    $("validation").textContent = unsaved ? "Imported draft. Validate before saving." : "Map loaded. Ready to edit.";
    $("validation").className = "validation";
}
async function refreshMaps() {
    mapNames = await api("/api/maps");
    const options = mapNames.map(name => { const option = document.createElement("option"); option.value = name; option.textContent = name; return option; });
    $("map-list").replaceChildren(...options);
    if (doc && mapNames.includes(doc.name)) $("map-list").value = doc.name;
}
async function openMap(name, discardDraft = true) {
    if (busy) throw Error("Wait for the current file operation to finish.");
    busy = true;
    try {
        const next = await api(`/api/maps/${encodeURIComponent(name)}`);
        install(next);
        if (discardDraft) clearDraft();
    } finally { busy = false; }
}
async function validate() {
    assertReady();
    const result = await api("/api/validate", doc);
    $("validation").className = "validation success";
    $("validation").textContent = `Valid ${result.width} × ${result.height} map\n${result.objects} used objects · ${result.sprites} sprites`;
    return result;
}
async function save() {
    assertReady();
    const overwrite = mapNames.includes(doc.name);
    if (overwrite && !confirm(`Replace maps/${doc.name}.json? Other maps and their objects will stay unchanged.`)) return;
    busy = true;
    $("save").disabled = true;
    try {
        const result = await api("/api/save", { document: doc, overwrite });
        doc.map = result.map;
        dirty = false; savedObjectState = objectState(); clearDraft();
        refresh(); await refreshMaps();
        toast(`Saved ${result.path}. Reload it in the engine to play.`);
    } finally { busy = false; $("save").disabled = false; }
}
function download(blob, filename) {
    const link = document.createElement("a"), url = URL.createObjectURL(blob);
    link.href = url; link.download = filename; link.click(); setTimeout(() => URL.revokeObjectURL(url), 1000);
}
async function importFile(file) {
    if (!file || !allowDiscard()) return;
    const value = JSON.parse(await file.text());
    let next;
    if (value.map && value.objects) next = value;
    else {
        checkGrid(value);
        const objects = {}, pending = [...new Set(value.worldMap.flat())];
        for (let i = 0; i < pending.length; i++) {
            const id = pending[i];
            if (objects[id]) continue;
            const path = value.objects?.[id];
            if (typeof path !== "string") throw Error(`No object file for ID ${id}. Import a map with an objects index.`);
            const url = new URL(path, `${location.origin}/engine/maps/`);
            if (url.origin !== location.origin || !url.pathname.startsWith("/engine/objects/")) throw Error("Imported object paths must point into the engine's objects folder.");
            const response = await fetch(url);
            if (!response.ok) throw Error(`Cannot load ${path}. Extract its map bundle into wwwroot first.`);
            const d = await response.json();
            if (d.id !== id) throw Error(`Mismatched object ID ${id}.`);
            objects[id] = d;
            pending.push(...dependencies(d));
        }
        next = { name: file.name.replace(/\.json$/i, "").replace(/[^a-zA-Z0-9_-]/g, "-").slice(0, 64) || "imported-map", map: value, objects, deletedObjectIds: value.editor?.deletedObjectIds ?? [] };
    }
    install(next, true); queueDraft(); toast("Map imported. Validate before saving.");
}

for (const button of document.querySelectorAll("[data-tool]")) button.onclick = () => setTool(button.dataset.tool);
$("search").oninput = () => { if (doc) palette(); };
$("type-filter").onchange = () => { if (doc) palette(); };
$("undo").onclick = act(() => history(true));
$("redo").onclick = act(() => history(false));
$("zoom-in").onclick = () => zoom(4);
$("zoom-out").onclick = () => zoom(-4);
$("fit").onclick = fit;
for (const id of ["show-grid", "show-ids", "show-collision", "show-rays"]) $(id).onchange = requestDraw;
$("open").onclick = act(async () => { if (allowDiscard()) await openMap($("map-list").value); });
$("new").onclick = act(() => {
    assertReady();
    if (!allowDiscard()) return;
    const name = prompt("Name for the new map (letters, digits, underscores or hyphens):", "new-map");
    if (!name) return;
    if (!/^[a-zA-Z0-9][a-zA-Z0-9_-]{0,63}$/.test(name)) throw Error("Choose a valid map name.");
    const { floor, wall } = defaults(), before = copy(doc);
    doc.name = name;
    doc.map.worldMap = resizeGrid([], 24, 24, floor, wall);
    delete doc.map.sky;
    doc.map.playerStart = { x: 2.5, y: 2.5, directionX: -1, directionY: 0, moveSpeed: 5, rotSpeed: 3, collisionRadius: 0.15, jumpRise: 0.4, jumpLength: 2, jumpSpeedMultiplier: 1.25, crouchDrop: 0.25, crouchSpeedMultiplier: 0.5 };
    commit(before); fit(); toast("New 24 × 24 map created. Resize it in Map settings.");
});
$("resize").onclick = act(() => {
    const width = Number($("map-width").value), height = Number($("map-height").value);
    const { floor, wall } = defaults();
    if ((width < doc.map.worldMap.length || height < doc.map.worldMap[0].length) && !confirm("Shrink the map? Cells outside the new dimensions will be removed. Undo is available.")) return;
    change(() => {
        doc.map.worldMap = resizeGrid(doc.map.worldMap, width, height, floor, wall);
        const p = doc.map.playerStart;
        if (p.x < 1 || p.y < 1 || p.x >= width - 1 || p.y >= height - 1) {
            let found;
            for (let x = 1; x < width - 1 && !found; x++) for (let y = 1; y < height - 1 && !found; y++)
                if (!doc.objects[doc.map.worldMap[x][y]]?.collides) found = { x: x + .5, y: y + .5 };
            if (found) Object.assign(p, found);
        }
    });
    fit();
});
$("map-name").onchange = act(() => {
    const name = $("map-name").value.trim();
    if (!/^[a-zA-Z0-9][a-zA-Z0-9_-]{0,63}$/.test(name)) { refresh(); throw Error("Use 1–64 letters, digits, underscores or hyphens."); }
    change(() => doc.name = name);
});
for (const [id, property] of [["spawn-x", "x"], ["spawn-y", "y"], ["move-speed", "moveSpeed"], ["rot-speed", "rotSpeed"], ["collision-radius", "collisionRadius"], ["jump-rise", "jumpRise"], ["jump-length", "jumpLength"], ["jump-speed-multiplier", "jumpSpeedMultiplier"], ["crouch-drop", "crouchDrop"], ["crouch-speed-multiplier", "crouchSpeedMultiplier"]])
    $(id).onchange = act(() => {
        const value = Number($(id).value);
        if (!Number.isFinite(value) || value < 0 || $(id).value === "") { refresh(); throw Error("Enter a finite nonnegative value."); }
        if (property === "collisionRadius" && (value <= 0 || value >= 0.5)) { refresh(); throw Error("Collision radius must be greater than zero and less than 0.5."); }
        if (property === "jumpRise" && value >= 0.5) { refresh(); throw Error("Jump rise must be less than 0.5 map squares."); }
        if (property === "jumpLength" && value > 64) { refresh(); throw Error("Jump length must be at most 64 map squares."); }
        if (property === "jumpSpeedMultiplier" && value <= 0) { refresh(); throw Error("Jump speed multiplier must be positive."); }
        if (property === "crouchDrop" && (value <= 0 || value >= 0.5)) { refresh(); throw Error("Crouch drop must be greater than zero and less than 0.5."); }
        if (property === "crouchSpeedMultiplier" && value <= 0) { refresh(); throw Error("Crouch speed multiplier must be positive."); }
        change(() => doc.map.playerStart[property] = value);
    });
$("sky-texture").onchange = act(() => {
    const texture = $("sky-texture").value.trim();
    change(() => {
        if (texture) doc.map.sky = { texture, rotationDegrees: doc.map.sky?.rotationDegrees ?? 0 };
        else delete doc.map.sky;
    });
});
$("sky-rotation").onchange = act(() => {
    const rotation = Number($("sky-rotation").value);
    if (!Number.isFinite(rotation) || $("sky-rotation").value === "") throw Error("Enter a finite sky rotation.");
    if (doc.map.sky) change(() => doc.map.sky.rotationDegrees = rotation);
});
$("spawn-facing").onchange = act(() => {
    const [x, y] = $("spawn-facing").value.split(",").map(Number);
    change(() => { doc.map.playerStart.directionX = x; doc.map.playerStart.directionY = y; });
});
$("object-json").oninput = () => definitionDirty = true;
$("apply-object").onclick = act(() => {
    if (!doc) return;
    const d = JSON.parse($("object-json").value);
    if (d.id !== brush) throw Error("Keep the selected object's ID. Use Add object to create another ID.");
    checkDefinition(d, { ...doc.objects, [brush]: d });
    definitionDirty = false;
    change(() => doc.objects[brush] = d);
    select(brush, true);
});
$("add-object").onclick = act(() => {
    const type = $("new-type").value, id = Math.max(...Object.keys(doc.objects).map(Number), ...(doc.deletedObjectIds ?? [])) + 1;
    const { floor } = defaults();
    const d = { type, id, raycastTransparent: type !== "wall", collides: type === "wall", jumpable: false, crouchable: false };
    if (type === "passage") Object.assign(d, { floorTexture: "textures/mossy.png", ceilingTexture: "textures/wood.png" });
    else d.passageId = floor;
    if (type === "wall") d.textures = { north: "textures/wood.png", east: "textures/wood.png", south: "textures/wood.png", west: "textures/wood.png" };
    if (type === "sprite") {
        const used = new Set(Object.values(doc.objects).filter(o => o.type === "sprite").map(o => o.textureLayer));
        let layer = 0; while (used.has(layer)) layer++;
        if (layer > 255) throw Error("All 256 sprite texture layers are already in use.");
        Object.assign(d, { texture: "textures/barrel.png", textureLayer: layer });
    }
    if (type === "spriteGroup") d.sprites = [];
    change(() => { doc.objects[id] = d; doc.map.objects[id] = `../objects/editor/${doc.name}/${id}.json`; });
    select(id, true); toast(`Created ${type} #${id}. Edit its definition, then paint it onto the map.`);
});
$("delete-object").onclick = act(() => {
    assertReady();
    if (Object.keys(doc.objects).length <= 1) throw Error("Keep at least one object in the library.");
    const usage = objectUsage(doc, brush);
    const candidates = Object.values(doc.objects).filter(d => d.id !== brush &&
        (!usage.passages || d.type === "passage") && (!usage.members || d.type === "sprite"));
    if (usage.total && !candidates.length) throw Error("Add a replacement object of the same type before deleting this one.");
    $("delete-dialog").dataset.objectId = brush;
    $("delete-summary").textContent = `Delete #${brush} ${title(brush)}? Used in ${usage.cells} cells, ${usage.passages} passage references, and ${usage.members} group members.`;
    $("replacement-label").hidden = $("delete-replacement").hidden = !usage.total;
    $("delete-replacement").replaceChildren(...candidates.map(d => {
        const option = document.createElement("option"); option.value = d.id; option.textContent = `#${d.id} · ${title(d.id)} (${d.type})`; return option;
    }));
    $("delete-dialog").showModal();
});
$("confirm-delete").onclick = act(() => {
    assertReady();
    const id = Number($("delete-dialog").dataset.objectId);
    const replacement = $("delete-replacement").hidden ? undefined : Number($("delete-replacement").value);
    change(() => removeObject(doc, id, replacement));
    select(replacement ?? Number(Object.keys(doc.objects)[0]), true);
    $("delete-dialog").close();
    toast(`Deleted object #${id}. Undo restores it and its references.`);
});
$("validate").onclick = act(validate);
$("save").onclick = act(save);
$("play").onclick = act(() => {
    assertReady();
    if (dirty || !mapNames.includes(doc.name)) throw Error("Save the map before opening it in the engine.");
    const url = new URL($("engine-url").value);
    if (!["http:", "https:"].includes(url.protocol)) throw Error("Enter an HTTP or HTTPS engine URL.");
    url.searchParams.set("map", doc.name);
    window.open(url.href, "_blank", "noopener");
});
$("import").onclick = () => $("import-file").click();
$("import-file").onchange = act(async () => { try { await importFile($("import-file").files[0]); } finally { $("import-file").value = ""; } });
$("export").onclick = act(() => { assertReady(); $("export-dialog").showModal(); });
$("export-zip").onclick = act(async () => {
    assertReady(); await validate();
    const response = await fetch("/api/export", { method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify(doc) });
    if (!response.ok) throw Error((await response.json()).error ?? "Export failed.");
    download(await response.blob(), `${doc.name}.zip`); $("export-dialog").close();
});
$("export-json").onclick = act(async () => {
    assertReady(); await validate();
    if ((dirty || objectState() !== savedObjectState) && !confirm("This JSON includes object paths, but no definitions. Save first or export a ZIP to include edited definitions. Download only the map anyway?")) return;
    const map = { ...doc.map, editor: { deletedObjectIds: doc.deletedObjectIds ?? [] } };
    download(new Blob([JSON.stringify(map, null, 2)], { type: "application/json" }), `${doc.name}.json`); $("export-dialog").close();
});
$("restore-draft").onclick = act(() => {
    if (!allowDiscard()) return;
    const draft = JSON.parse(localStorage.getItem(draftKey));
    if (!draft?.document) throw Error("No draft is available.");
    install(draft.document, true); savedObjectState = draft.savedObjectState ?? "";
    $("draft-banner").hidden = true; toast("Browser draft restored.");
});
$("dismiss-draft").onclick = clearDraft;
window.addEventListener("beforeunload", event => {
    if (dirty || definitionDirty || gesture) { event.preventDefault(); event.returnValue = ""; }
});
window.addEventListener("keydown", act(async event => {
    const text = /INPUT|TEXTAREA|SELECT/.test(event.target.tagName);
    if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === "s") { event.preventDefault(); await save(); return; }
    if (text || document.querySelector("dialog[open]")) return;
    if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === "z") { event.preventDefault(); history(!event.shiftKey); return; }
    if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === "y") { event.preventDefault(); history(false); return; }
    if (event.ctrlKey || event.metaKey || event.altKey) return;
    const shortcut = { b: "brush", f: "fill", i: "pick", p: "spawn" }[event.key.toLowerCase()];
    if (shortcut) { event.preventDefault(); setTool(shortcut); }
}));
window.addEventListener("resize", requestDraw);

function chooseTexture(path, field = $("texture-target").value) {
    const d = JSON.parse($("object-json").value);
    if (d.type === "wall") { d.textures ??= {}; d.textures[field] = path; }
    else d[field] = path;
    $("object-json").value = JSON.stringify(d, null, 2);
    definitionDirty = true;
}
function renderTextures() {
    $("sky-textures").replaceChildren(...library.textures.filter(path => path.includes("/skyboxes/")).map(path => {
        const option = document.createElement("option"); option.value = path; return option;
    }));
    $("texture-options").replaceChildren();
    for (const path of library.textures) {
        const button = document.createElement("button"), thumb = document.createElement("img");
        button.type = "button"; button.title = path; button.setAttribute("aria-label", path);
        thumb.src = textureUrl(path); thumb.alt = path.split("/").at(-1); thumb.className = "object-thumb";
        button.append(thumb);
        button.onclick = act(() => {
            if (busy) throw Error("Wait for the current file operation to finish.");
            chooseTexture(path);
            toast("Texture selected. Apply the definition to update the map.");
        });
        $("texture-options").append(button);
    }
}
$("import-texture").onclick = act(() => {
    if (busy || !doc) throw Error("Wait for the current operation to finish.");
    $("texture-file").click();
});
$("texture-file").onchange = act(async () => {
    const file = $("texture-file").files[0];
    $("texture-file").value = "";
    if (!file) return;
    if (busy || !doc) throw Error("Wait for the current operation to finish.");
    if (file.size > 10 * 1024 * 1024) throw Error("Choose an image smaller than 10 MB.");
    if (!/\.(png|jpe?g|webp|bmp)$/i.test(file.name)) throw Error("Choose a PNG, JPEG, WebP or BMP image.");
    const targetId = brush, field = $("texture-target").value;
    busy = true; $("import-texture").disabled = true;
    try {
        let bitmap;
        try { bitmap = await createImageBitmap(file); }
        catch { throw Error("This file could not be decoded as an image."); }
        const output = document.createElement("canvas"); output.width = output.height = 64;
        try {
            const context = output.getContext("2d"); context.imageSmoothingEnabled = false;
            context.drawImage(bitmap, 0, 0, 64, 64);
        } finally { bitmap.close(); }
        const imported = await api("/api/textures", { name: file.name, png: output.toDataURL("image/png").split(",")[1] });
        if (!library.textures.includes(imported.path)) { library.textures.push(imported.path); library.textures.sort(); }
        renderTextures();
        if (brush === targetId) {
            chooseTexture(imported.path, field);
            toast("Texture imported as 64 × 64 PNG. Apply the definition to update the map.");
        } else toast("Texture imported. Choose it from the texture picker.");
        $("texture-picker").open = true;
    } finally { busy = false; $("import-texture").disabled = false; }
});

try {
    library = await api("/api/library");
    renderTextures();
    await refreshMaps();
    await openMap(mapNames.includes("showcase") ? "showcase" : mapNames[0], false);
    try { $("draft-banner").hidden = !localStorage.getItem(draftKey); } catch {}
} catch (error) { report(error); }

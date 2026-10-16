// Exercises the actual HTTP API against an isolated copy of the engine assets.
import assert from "node:assert/strict";
import fs from "node:fs/promises";
import path from "node:path";
import os from "node:os";
import { spawn } from "node:child_process";
import { request } from "node:http";
import { deflateSync } from "node:zlib";
import { fileURLToPath } from "node:url";

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const editor = path.join(root, "WolfNET.Editor");
const assets = await fs.mkdtemp(path.join(os.tmpdir(), "wolfnet-editor-api-"));
function rgbaPng(width, height) {
    function chunk(type, data) {
        const buffer = Buffer.alloc(data.length + 12);
        buffer.writeUInt32BE(data.length); buffer.write(type, 4); data.copy(buffer, 8);
        let crc = 0xffffffff;
        for (const value of buffer.subarray(4, -4)) {
            crc ^= value;
            for (let bit = 0; bit < 8; bit++) crc = (crc >>> 1) ^ ((crc & 1) ? 0xedb88320 : 0);
        }
        buffer.writeUInt32BE((~crc) >>> 0, buffer.length - 4);
        return buffer;
    }
    const header = Buffer.alloc(13); header.writeUInt32BE(width); header.writeUInt32BE(height, 4); header[8] = 8; header[9] = 6;
    return Buffer.concat([Buffer.from([137,80,78,71,13,10,26,10]), chunk("IHDR", header), chunk("IDAT", deflateSync(Buffer.alloc((width * 4 + 1) * height))), chunk("IEND", Buffer.alloc(0))]);
}
let server;
try {
    for (const folder of ["maps", "objects", "pics"])
        await fs.cp(path.join(root, "WolfNET.Game", "wwwroot", folder), path.join(assets, folder), { recursive: true });
    const originalPassage = await fs.readFile(path.join(assets, "objects/passages/passage-showcase-color-stone.json"), "utf8");
    server = spawn("dotnet", ["bin/Debug/net10.0/WolfNET.Editor.dll", "--urls", "http://127.0.0.1:0", "--EngineRoot", assets], { cwd: editor, windowsHide: true });
    const base = await new Promise((resolve, reject) => {
        let logs = "";
        const timer = setTimeout(() => reject(Error(`Editor did not start: ${logs}`)), 15000);
        server.on("error", error => { clearTimeout(timer); reject(error); });
        server.on("exit", code => { clearTimeout(timer); reject(Error(`Editor exited (${code}): ${logs}`)); });
        server.stdout.on("data", data => {
            logs += data;
            const match = logs.match(/Now listening on: (http:\/\/127\.0\.0\.1:\d+)/);
            if (match) { clearTimeout(timer); resolve(match[1]); }
        });
        server.stderr.on("data", data => logs += data);
    });
    async function get(route) {
        const response = await fetch(base + route);
        assert.equal(response.status, 200, `${route}: ${await response.clone().text()}`);
        return response.json();
    }
    const post = (route, body) => fetch(base + route, { method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify(body) });
    const textureBytes = await fs.readFile(path.join(assets, "pics/mossy.png"));
    const textureImport = { name: "../Custom floor.png", png: textureBytes.toString("base64") };
    const uploaded = await post("/api/textures", textureImport);
    assert.equal(uploaded.status, 200, await uploaded.clone().text());
    const texturePath = (await uploaded.json()).path;
    assert.match(texturePath, /^pics\/imported-custom-floor-[a-f0-9]{12}\.png$/);
    assert.deepEqual(await fs.readFile(path.join(assets, texturePath)), textureBytes);
    assert.equal((await (await post("/api/textures", textureImport)).json()).path, texturePath, "Duplicate import should reuse the same texture");
    assert.equal((await post("/api/textures", { name: "broken.png", png: "invalid" })).status, 400);
    assert.equal((await post("/api/textures", { name: "text.png", png: Buffer.from("not an image").toString("base64") })).status, 400);
    const corrupt = Buffer.from(textureBytes); corrupt[40] ^= 1;
    assert.equal((await post("/api/textures", { name: "corrupt.png", png: corrupt.toString("base64") })).status, 400);
    assert.equal((await post("/api/textures", { name: "alpha.png", png: rgbaPng(64, 64).toString("base64") })).status, 200, "RGBA textures must be accepted");
    assert.equal((await post("/api/textures", { name: "small.png", png: rgbaPng(32, 64).toString("base64") })).status, 400, "Wrong dimensions must be rejected");
    const document = await get("/api/maps/showcase");
    document.name = "api-roundtrip";
    document.deletedObjectIds = [26];
    document.objects[11].floorTexture = texturePath;
    assert.equal((await post("/api/validate", document)).status, 200);
    const exported = await post("/api/export", document);
    assert.equal(exported.headers.get("content-type"), "application/zip");
    const zipBytes = Buffer.from(await exported.arrayBuffer());
    assert.equal(zipBytes.readUInt32LE(), 0x04034b50);
    assert.ok(zipBytes.includes(Buffer.from(texturePath)), "Export must include imported textures");
    const saved = await post("/api/save", { document, overwrite: false });
    assert.equal(saved.status, 200, await saved.clone().text());
    assert.equal((await saved.json()).path, "maps/api-roundtrip.json");
    const reopened = await get("/api/maps/api-roundtrip");
    assert.equal(reopened.objects[11].floorTexture, texturePath);
    assert.deepEqual(reopened.deletedObjectIds, [26], "Deleted library objects must stay deleted after reopening");
    assert.deepEqual(reopened.map.worldMap, document.map.worldMap);
    assert.ok((await get("/api/maps")).includes("api-roundtrip"));
    assert.equal(await fs.readFile(path.join(assets, "objects/passages/passage-showcase-color-stone.json"), "utf8"), originalPassage);
    assert.equal((await post("/api/save", { document, overwrite: false })).status, 400);
    assert.equal((await post("/api/save", { document, overwrite: true })).status, 200);
    const invalid = structuredClone(document);
    invalid.map.worldMap[0][0] = 0;
    assert.equal((await post("/api/validate", invalid)).status, 400);
    assert.equal((await post("/api/save", { document: { ...document, name: "../outside" } })).status, 400);
    const badHostStatus = await new Promise((resolve, reject) => {
        const call = request(base + "/api/maps", { headers: { Host: "external.example" } }, response => {
            response.resume(); resolve(response.statusCode);
        });
        call.on("error", reject); call.end();
    });
    assert.equal(badHostStatus, 403);
    const library = await get("/api/library");
    assert.ok(library.objects.some(entry => entry.definition.type === "wall" && Number.isInteger(entry.definition.passageId)));
    assert.ok(library.textures.includes("pics/mossy.png"));
    assert.ok(library.textures.includes(texturePath), "Imported texture missing from catalog");
    console.log("PASS: editor HTTP loading, validation, ZIP export, save/reopen, overwrite protection, isolated objects, invalid maps/paths, and local-host restriction.");
} finally {
    if (server && server.exitCode === null) {
        const stopped = new Promise(resolve => server.once("exit", resolve));
        server.kill();
        await stopped;
    }
    // Remove only the temporary tree created by this test.
    if (path.dirname(assets) === path.resolve(os.tmpdir()) && path.basename(assets).startsWith("wolfnet-editor-api-"))
        await fs.rm(assets, { recursive: true, force: true });
}

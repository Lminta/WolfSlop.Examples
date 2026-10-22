import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import { sizeRacewayObject } from '../Tools/size-raceway-props.mjs';

test('all shipped raceway presets retain their physical sizes after rebuilding', () => {
    const directory = new URL('../WolfNET.Game/wwwroot/objects/raceway/', import.meta.url);
    for (const file of fs.readdirSync(directory).filter(file => file.endsWith('.json'))) {
        const object = JSON.parse(fs.readFileSync(new URL(file, directory), 'utf8'));
        assert.deepEqual(sizeRacewayObject(structuredClone(object)), object, file);
    }
});

test('event props use plausible meter dimensions and padded artwork stays on the floor', () => {
    const read = name => JSON.parse(fs.readFileSync(new URL(`../WolfNET.Game/wwwroot/objects/raceway/${name}.json`, import.meta.url)));
    for (const [name, height, baseline] of [
        ['group-race-start-light', 3, 1], ['group-festival-flag', 3.6, 496 / 512],
        ['group-cheering-crowd', 2.7, 496 / 512], ['group-service-barrels', 1.06, 1200 / 1280],
        ['group-billboard-grip-happens-x', 2.4, 1], ['group-tires', .9, 1],
    ]) {
        for (const member of read(name).sprites) {
            assert.ok(Math.abs(member.scale.y * 3 - height) < .00001, name);
            const ground = member.offset.z - member.scale.y * (baseline - .5);
            assert.ok(Math.abs(ground + .5) < .00001, `${name} floats or sinks`);
        }
    }
    const tree = read('group-pine-tall').sprites[0];
    assert.equal(tree.scale.y * 3, 12);
    const fence = read('group-boundary-fence-east').sprites[0];
    assert.equal(fence.scale.x * 3, 3);
    assert.ok(Math.abs(fence.scale.y * 3 - 2.4) < .00001);
});

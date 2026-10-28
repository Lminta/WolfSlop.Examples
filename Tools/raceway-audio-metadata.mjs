// Author explicit runtime metadata. Filenames are used only by this authoring
// tool; the engine reads impactMaterial/ambientSound from object definitions.
import fs from 'node:fs';
import path from 'node:path';
import {fileURLToPath} from 'node:url';

export function applyRacewayAudioMetadata() {
    const directory = fileURLToPath(new URL('../WolfSlop.Examples/wwwroot/objects/raceway/', import.meta.url));
    let changed = 0;
    for (const name of fs.readdirSync(directory).filter(name => name.endsWith('.json'))) {
        const file = path.join(directory, name);
        const definition = JSON.parse(fs.readFileSync(file, 'utf8'));
        const before = JSON.stringify(definition);
        if (definition.collides && definition.impactMaterial === undefined) {
            if (/tire|buffer/.test(name)) definition.impactMaterial = 'rubber';
            else if (/rail|fence|metal|container|service-barrel|pit-structure|pit-bay|grandstand/.test(name)) definition.impactMaterial = 'metal';
            else if (/wood|tree|pine|oak|birch|forest/.test(name)) definition.impactMaterial = 'wood';
        }
        if (/cheering-crowd/.test(name)) definition.ambientSound ??= 'crowd';
        if (JSON.stringify(definition) !== before) {
            fs.writeFileSync(file, JSON.stringify(definition, null, 2) + '\n');
            changed++;
        }
    }
    return changed;
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url))
    console.log(`Updated audio metadata on ${applyRacewayAudioMetadata()} raceway objects.`);

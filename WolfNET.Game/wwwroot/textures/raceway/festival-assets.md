# Festival animation artwork

Both assets were generated with the built-in imagegen tool, then packed with
`Tools/build-festival-atlases.ps1`. The packing step preserves alpha and aligns
the three frames to baseline row 496. Original generated sheets are preserved.

| Asset | Game atlas | Original sheet | Exact generation prompt | Frame layout |
| --- | --- | --- | --- | --- |
| Waving checkered flagpole | [festival-flags-atlas.png](festival-flags-atlas.png) | [festival-flags-source.png](festival-flags-source.png) | [Flag prompt](festival-flags-atlas.prompt.txt) | [Layout](festival-flags-atlas.layout.json) |
| Five cheering spectators | [festival-crowd-atlas.png](festival-crowd-atlas.png) | [festival-crowd-source.png](festival-crowd-source.png) | [Crowd prompt](festival-crowd-atlas.prompt.txt) | [Layout](festival-crowd-atlas.layout.json) |

Each game atlas is 1536 × 512 pixels, with three horizontal 512 × 512 frames.
Object definitions play these frames at four frames per second with three phase
presets. Billboard and signal artwork is editable SVG authored by
`Tools/build-raceway-event-props.mjs`.

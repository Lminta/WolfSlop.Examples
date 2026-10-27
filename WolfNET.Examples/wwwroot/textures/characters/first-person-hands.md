# First-person hands

`first-person-hands.png` was generated with the built-in `image_gen` tool in
transparent-background mode. The character atlas was an identity and style
reference; its blue jacket and skin palette carry into the hands. Original
generated alpha is preserved.

The 1536 x 1024 image contains the left hand in its left half and the right hand
in its right half. `ExampleHud` supplies cropped `UiImage` controls and independent
transforms to the engine UI renderer, which composites both halves over the world.
Simulation-driven swing, bob and strafe settle when actual
movement stops; jumping raises the hands and crouching lowers them. The overlay
appears only for the human first-person camera.
Pressing F also plays a 360 ms right-hand reach, even without an interaction
target. The gesture uses the existing sprite and blends with movement poses.
Animation state travels with `RenderFrame` every frame; no DOM image overlays or
Blazor animation events are used. GPU textures belong to the scene and are released on map changes.

## Generation prompt

Use case: stylized-concept
Asset type: transparent first-person hands sprite for WolfNET raycasting game, animated in code.
Create ONE 1536x1024 landscape PNG with genuine transparent alpha. Reference image is ONLY character identity / painterly sprite style: human with light warm skin and blue work jacket with rolled cuffs. Output only a matched pair of bare hands and blue-sleeved forearms viewed from the character's OWN EYES, looking forward; LEFT anatomical hand at lower left and RIGHT hand at lower right. Relaxed loosely curled fingers in a natural walking/exploring pose, palms angled inward/down, thumbs toward center. Forearms come from bottom corners toward hands inward and above. Hands around x25% and x75%, y58%, blue forearms continue to bottom edge. All content strictly in left 42% and right 42%, keep center 16% fully transparent. Top 30% fully transparent. Balanced equal scale, believable anatomy and foreshortening, soft neutral light, clean outlined painted game sprite style matching character reference. This pair will be split at center into independently animated left/right sprites, so neither arm may cross center. No torso, face, full person, weapon, props, wheel, scenery, text, grid or drop shadow. Transparent everywhere except hands and sleeves. No checkerboard pixels.

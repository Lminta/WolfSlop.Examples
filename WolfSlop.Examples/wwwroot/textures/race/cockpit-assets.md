# Cockpit assets

Generated with the built-in `image_gen` tool, transparent-background mode.
The existing `car-game-atlas.png` was a car/style reference for the cockpit;
the generated cockpit was the materials reference for the wheel. References
were not overwritten. Alpha is preserved in both project PNGs.

`ExampleHud` lays out both images as engine `UiImage` controls. The UI renderer
composites them over the world with the same layout/input system used by the HUD.
Steering rotation and camera visibility come from the captured presentation state.
Decoded images are cached by the UI host and released on scene switches.

## cockpit.png — generation prompt

Create one production game HUD bitmap asset for a raycasting racing game:
first-person interior cockpit overlay matching the supplied reference red 1990s
wedge sports car. Reference image is STYLE AND CAR IDENTITY REFERENCE only, do
not reproduce the car sprite sheet. Wide 16:9 overlay composition, dashboard
occupies bottom 22 percent with black textured leather, restrained analogue
gauges, vents, thin red bodywork accents; narrow windshield A-pillars at extreme
left/right edges and a very thin roof trim along top. Driver's-eye perspective
with vanishing point at center. All windshield and window areas and entire
center upper 75 percent MUST be genuinely transparent alpha, no road, landscape,
sky, reflections, tint, text, logos or people. CRITICAL: no steering wheel, no
hands, leave a clear attachment area in lower left-center around horizontal42
percent for a separate steering wheel sprite added by game. Dashboard must
extend fully to left/right/bottom canvas edges, no exterior backdrop, no isolated
car body. Realistic painted sprite style consistent with reference. Export clean
transparent PNG, large landscape resolution.

## steering-wheel.png — generation prompt

Generate one transparent game sprite asset: a single isolated realistic 1990s
red sports car steering wheel viewed directly face-on from driver's eyes,
circular black textured leather rim, dark metallic three spokes (left/right
horizontal and bottom vertical), small red center cap without any text or logos,
tiny red centering stripe at top of rim. Match materials, lighting and realistic
sprite art of the reference cockpit. Reference is style only; output ONLY THE
WHEEL. Perfectly centered circular geometry suitable for flat rotation around
exact image center, wheel occupies 90% of square image. Genuine transparent
alpha outside the rim and between the three spokes, no dashboard, column,
hands, car, background, shadow outside wheel, landscape, text or watermark.
Square 1024x1024 PNG.

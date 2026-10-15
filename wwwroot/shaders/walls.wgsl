
struct Camera {
    position: vec2f,
    direction: vec2f,
    plane: vec2f,
    screenSize: vec2f,
};

struct RayHit {
    distance: f32,
    wallU: f32,
    wallType: u32,
    side: u32,
};

struct VertexOutput {
    @builtin(position) position: vec4f,
};

@group(0) @binding(0)
var<storage, read> rayHits: array<RayHit>;

@group(0) @binding(1)
var<uniform> camera: Camera;

@group(0) @binding(2)
var wallTextures: texture_2d_array<f32>;

// Zero-based layers in WebGpuRenderer.TexturePaths.
const FLOOR_TEXTURE: i32 = 5;   // mossy.png
const CEILING_TEXTURE: i32 = 6; // wood.png

@vertex
fn vertexMain(
    @builtin(vertex_index) index: u32
) -> VertexOutput {
    var positions = array<vec2f, 3>(
        vec2f(-1.0, -1.0),
        vec2f( 3.0, -1.0),
        vec2f(-1.0,  3.0)
    );

    var output: VertexOutput;
    output.position =
        vec4f(positions[index], 0.0, 1.0);

    return output;
}

fn floorCeilingColor(position: vec2f) -> vec4f {
    let horizonOffset = position.y - camera.screenSize.y * 0.5;

    // Rays on the horizon run parallel to both planes.
    if (abs(horizonOffset) < 0.0001) {
        return vec4f(0.0, 0.0, 0.0, 1.0);
    }

    // The camera is halfway between the floor (z=0) and ceiling (z=1).
    let distance = camera.screenSize.y * 0.5 / abs(horizonOffset);
    // Match the column ray used by the compute shader.
    let cameraX = 2.0 * floor(position.x) / camera.screenSize.x - 1.0;
    let rayDir = camera.direction + camera.plane * cameraX;
    let worldPosition = camera.position + rayDir * distance;

    // Repeat one texture tile per map cell, including negative coordinates.
    let dimensions = textureDimensions(wallTextures, 0);
    let texel = vec2i(min(
        fract(worldPosition) * vec2f(dimensions),
        vec2f(dimensions) - vec2f(1.0)
    ));
    let layer = select(CEILING_TEXTURE, FLOOR_TEXTURE, horizonOffset > 0.0);
    return vec4f(textureLoad(wallTextures, texel, layer, 0).rgb, 1.0);
}

@fragment
fn fragmentMain(
    input: VertexOutput
) -> @location(0) vec4f {
    let x = u32(input.position.x);

    if (x >= u32(camera.screenSize.x)) {
        return vec4f(0.0, 0.0, 0.0, 1.0);
    }

    let hit = rayHits[x];

    if (hit.wallType == 0u || hit.distance <= 0.0) {
        return floorCeilingColor(input.position.xy);
    }

    let screenHeight = camera.screenSize.y;
    let lineHeight = screenHeight / hit.distance;

    let drawStart = max(
        0.0,
        -lineHeight * 0.5 + screenHeight * 0.5
    );

    let drawEnd = min(
        screenHeight,
        lineHeight * 0.5 + screenHeight * 0.5
    );

    let y = input.position.y;

    if (y < drawStart || y >= drawEnd) {
        return floorCeilingColor(input.position.xy);
    }

    // Use the full projected wall, even when its top is above the screen.
    let wallV = (y - screenHeight * 0.5) / lineHeight + 0.5;
    let dimensions = textureDimensions(wallTextures, 0);
    let texel = vec2i(clamp(
        vec2f(hit.wallU, wallV) * vec2f(dimensions),
        vec2f(0.0),
        vec2f(dimensions) - vec2f(1.0)
    ));
    let layer = i32(min(hit.wallType - 1u, textureNumLayers(wallTextures) - 1u));
    var color = textureLoad(wallTextures, texel, layer, 0).rgb;

    if (hit.side == 1u) {
        color *= 0.5;
    }

    return vec4f(color, 1.0);
}

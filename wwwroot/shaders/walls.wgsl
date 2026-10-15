
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
        return vec4f(0.0, 0.0, 0.0, 1.0);
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
        return vec4f(0.0, 0.0, 0.0, 1.0);
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


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

fn wallColor(wallType: u32) -> vec3f {
    switch (wallType) {
        case 1u: { return vec3f(1.0, 0.0, 0.0); }
        case 2u: { return vec3f(0.0, 1.0, 0.0); }
        case 3u: { return vec3f(0.0, 0.0, 1.0); }
        case 4u: { return vec3f(1.0, 1.0, 1.0); }
        default: { return vec3f(1.0, 1.0, 0.0); }
    }
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

    var color = wallColor(hit.wallType);

    if (hit.side == 1u) {
        color *= 0.5;
    }

    return vec4f(color, 1.0);
}

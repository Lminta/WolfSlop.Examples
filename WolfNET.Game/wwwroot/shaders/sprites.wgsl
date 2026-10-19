struct Camera {
    position: vec2f,
    direction: vec2f,
    plane: vec2f,
    screenSize: vec2f,
};

struct RayHit {
    distance: f32,
    wallU: f32,
    hitCell: u32,
    side: u32,
};

struct Sprite {
    position: vec2f,
    textureLayer: u32,
    ownerCell: u32,
};

struct VertexOutput {
    @builtin(position) position: vec4f,
    @location(0) uv: vec2f,
    @location(1) @interpolate(flat) depth: f32,
    @location(2) @interpolate(flat) textureLayer: u32,
    @location(3) @interpolate(flat) ownerCell: u32,
};

@group(0) @binding(0) var<storage, read> rayHits: array<RayHit>;
@group(0) @binding(1) var<uniform> camera: Camera;
@group(0) @binding(2) var spriteTextures: texture_2d_array<f32>;
@group(0) @binding(3) var<storage, read> sprites: array<Sprite>;

@vertex
fn vertexMain(
    @builtin(vertex_index) vertex: u32,
    @builtin(instance_index) instance: u32
) -> VertexOutput {
    let corners = array<vec2f, 6>(
        vec2f(0.0, 0.0), vec2f(1.0, 0.0), vec2f(0.0, 1.0),
        vec2f(0.0, 1.0), vec2f(1.0, 0.0), vec2f(1.0, 1.0)
    );
    let sprite = sprites[instance];
    let relative = sprite.position - camera.position;

    // Lode's inverse camera matrix gives horizontal position and perpendicular depth.
    let inverseDet = 1.0 / (
        camera.plane.x * camera.direction.y - camera.direction.x * camera.plane.y
    );
    let cameraX = inverseDet * (
        camera.direction.y * relative.x - camera.direction.x * relative.y
    );
    let depth = inverseDet * (
        -camera.plane.y * relative.x + camera.plane.x * relative.y
    );

    var output: VertexOutput;
    output.uv = corners[vertex];
    output.depth = depth;
    output.textureLayer = sprite.textureLayer;
    output.ownerCell = sprite.ownerCell;
    // Collapse sprites behind or on the camera plane to an offscreen point.
    output.position = vec4f(2.0, 2.0, 0.0, 1.0);
    if (depth <= 0.0001) {
        return output;
    }

    // Square billboards, centered vertically, with the same scale as unit walls.
    let focalLength = camera.screenSize.x / (2.0 * length(camera.plane));
    let size = focalLength / depth;
    let center = vec2f(
        camera.screenSize.x * 0.5 * (1.0 + cameraX / depth),
        camera.screenSize.y * 0.5
    );
    let pixel = center + (output.uv - vec2f(0.5)) * size;
    let ndc = pixel / camera.screenSize * vec2f(2.0, -2.0) + vec2f(-1.0, 1.0);
    output.position = vec4f(ndc, 0.0, 1.0);
    return output;
}

@fragment
fn fragmentMain(input: VertexOutput) -> @location(0) vec4f {
    let hit = rayHits[u32(input.position.x)];
    // The compute ray distances serve as Lode's one-dimensional Z buffer.
    if (hit.hitCell != 0u && hit.hitCell != input.ownerCell && input.depth >= hit.distance) {
        discard;
    }

    let dimensions = textureDimensions(spriteTextures, 0);
    let texel = vec2i(clamp(
        input.uv * vec2f(dimensions),
        vec2f(0.0),
        vec2f(dimensions) - vec2f(1.0)
    ));
    let color = textureLoad(spriteTextures, texel, i32(input.textureLayer), 0);
    if (color.a == 0.0) {
        discard;
    }
    return color;
}

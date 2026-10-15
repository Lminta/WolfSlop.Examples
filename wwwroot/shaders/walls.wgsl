
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

struct MapInfo {
    width: u32,
    height: u32,
    spriteCount: u32,
    padding: u32,
};

struct Sprite {
    position: vec2f,
    textureLayer: f32,
    padding: f32,
};

@group(0) @binding(0)
var<storage, read> rayHits: array<RayHit>;

@group(0) @binding(1)
var<uniform> camera: Camera;

@group(0) @binding(2)
var wallTextures: texture_2d_array<f32>;

// Shared with the raycasting and sprite passes; no separate minimap upload.
@group(0) @binding(3) var<storage, read> worldMap: array<u32>;
@group(0) @binding(4) var<uniform> mapInfo: MapInfo;
@group(0) @binding(5) var<storage, read> sprites: array<Sprite>;

// Zero-based layers in WebGpuRenderer.TexturePaths.
const FLOOR_TEXTURE: i32 = 5;   // mossy.png
const CEILING_TEXTURE: i32 = 6; // wood.png

// A final overlay pass on the game canvas, using the compute pass's DDA results.
@fragment
fn minimapMain(input: VertexOutput) -> @location(0) vec4f {
    let mapSize = vec2f(f32(mapInfo.width), f32(mapInfo.height));
    let panelLimit = min(256.0, min(camera.screenSize.x, camera.screenSize.y) * 0.32);
    let padding = min(10.0, panelLimit * 0.05);
    let scale = (panelLimit - 2.0 * padding) / max(mapSize.x, mapSize.y);
    let panelSize = mapSize * scale + vec2f(2.0 * padding);
    let pixel = input.position.xy - vec2f(16.0);
    let radius = min(8.0, padding);
    let corner = abs(pixel - panelSize * 0.5) - (panelSize * 0.5 - vec2f(radius));
    let edge = length(max(corner, vec2f(0.0))) + min(max(corner.x, corner.y), 0.0) - radius;
    if (edge > 0.0) {
        discard;
    }
    if (edge > -1.0) {
        return vec4f(0.51, 0.57, 0.62, 1.0);
    }

    let world = (pixel - vec2f(padding)) / scale;
    if (any(world < vec2f(0.0)) || any(world >= mapSize)) {
        return vec4f(0.067, 0.098, 0.137, 1.0);
    }

    let cell = vec2u(world);
    let wall = worldMap[cell.y * mapInfo.width + cell.x];
    var color = select(vec3f(0.137, 0.192, 0.239), vec3f(0.51, 0.57, 0.62), wall > 0u);
    let cellEdge = min(fract(world), vec2f(1.0) - fract(world)) * scale;
    if (min(cellEdge.x, cellEdge.y) < 0.3) {
        color *= 0.55;
    }

    // Project this map point back to a screen column and use that ray's depth
    // to show the actual visible region, clipped by the first wall hit.
    let relative = world - camera.position;
    let inverseDet = 1.0 / (camera.plane.x * camera.direction.y - camera.direction.x * camera.plane.y);
    let across = inverseDet * (camera.direction.y * relative.x - camera.direction.x * relative.y);
    let depth = inverseDet * (-camera.plane.y * relative.x + camera.plane.x * relative.y);
    if (depth > 0.0001 && abs(across) <= depth) {
        let column = u32(clamp(
            (across / depth + 1.0) * 0.5 * camera.screenSize.x,
            0.0, camera.screenSize.x - 1.0
        ));
        let hit = rayHits[column];
        if (hit.wallType == 0u || depth <= hit.distance) {
            color = mix(color, vec3f(0.27, 0.75, 1.0), 0.35);
        }
    }

    for (var i = 0u; i < mapInfo.spriteCount; i++) {
        let sprite = sprites[i];
        if (distance(world, sprite.position) < max(0.18, 1.5 / scale)) {
            color = vec3f(0.89, 0.68, 0.38);
            if (sprite.textureLayer == 9.0) {
                color = vec3f(0.85, 0.88, 0.91);
            } else if (sprite.textureLayer == 10.0) {
                color = vec3f(0.49, 0.90, 0.43);
            }
        }
    }

    let forward = normalize(camera.direction);
    let along = dot(relative, forward);
    let sideways = abs(dot(relative, vec2f(-forward.y, forward.x)));
    if (along >= -0.3 && along <= 0.55 && sideways <= (0.55 - along) * (0.32 / 0.85)) {
        color = vec3f(0.27, 0.75, 1.0);
    }
    return vec4f(color, 1.0);
}

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

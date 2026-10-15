
struct Camera {
    position: vec2f,
    direction: vec2f,
    plane: vec2f,
    screenSize: vec2f,
};

struct MapInfo {
    width: u32,
    height: u32,
    spriteCount: u32,
    padding: u32,
};

struct RayHit {
    distance: f32,
    wallU: f32,
    wallType: u32,
    side: u32,
};

@group(0) @binding(0)
var<uniform> camera: Camera;

@group(0) @binding(1)
var<storage, read> worldMap: array<u32>;

@group(0) @binding(2)
var<storage, read_write> rayHits: array<RayHit>;

@group(0) @binding(3)
var<uniform> mapInfo: MapInfo;

@compute @workgroup_size(64)
fn main(@builtin(global_invocation_id) id: vec3u) {
    let x = id.x;
    let screenWidth = u32(camera.screenSize.x);

    if (x >= screenWidth) {
        return;
    }

    let cameraX =
        2.0 * f32(x) / camera.screenSize.x - 1.0;

    let rayDir =
        camera.direction + camera.plane * cameraX;

    var mapX = i32(floor(camera.position.x));
    var mapY = i32(floor(camera.position.y));

    let deltaDistX = select(
        abs(1.0 / rayDir.x),
        1e30,
        rayDir.x == 0.0
    );

    let deltaDistY = select(
        abs(1.0 / rayDir.y),
        1e30,
        rayDir.y == 0.0
    );

    var stepX: i32;
    var stepY: i32;

    var sideDistX: f32;
    var sideDistY: f32;

    if (rayDir.x < 0.0) {
        stepX = -1;
        sideDistX =
            (camera.position.x - f32(mapX)) * deltaDistX;
    } else {
        stepX = 1;
        sideDistX =
            (f32(mapX) + 1.0 - camera.position.x) * deltaDistX;
    }

    if (rayDir.y < 0.0) {
        stepY = -1;
        sideDistY =
            (camera.position.y - f32(mapY)) * deltaDistY;
    } else {
        stepY = 1;
        sideDistY =
            (f32(mapY) + 1.0 - camera.position.y) * deltaDistY;
    }

    var side = 0u;
    var wallType = 0u;
    var distance = 1e30;

    // Protect against invalid maps or open boundaries.
    let maxSteps = mapInfo.width + mapInfo.height + 1u;

    for (var i = 0u; i < maxSteps; i++) {
        if (sideDistX < sideDistY) {
            sideDistX += deltaDistX;
            mapX += stepX;
            side = 0u;
        } else {
            sideDistY += deltaDistY;
            mapY += stepY;
            side = 1u;
        }

        if (mapX < 0 || mapY < 0 ||
            mapX >= i32(mapInfo.width) ||
            mapY >= i32(mapInfo.height)) {
            break;
        }

        // C# map[x][y] has been flattened row-major.
        let index =
            u32(mapY) * mapInfo.width + u32(mapX);

        let cell = worldMap[index];

        if (cell > 0u) {
            wallType = cell;

            distance = select(
                sideDistY - deltaDistY,
                sideDistX - deltaDistX,
                side == 0u
            );

            break;
        }
    }

    var wallU = 0.0;

    if (wallType != 0u) {
        let hitCoordinate = select(
            camera.position.x + distance * rayDir.x,
            camera.position.y + distance * rayDir.y,
            side == 0u
        );

        wallU = fract(hitCoordinate);

        // Keep opposite wall faces oriented as in Lode's textured raycaster.
        if ((side == 0u && rayDir.x > 0.0) ||
            (side == 1u && rayDir.y < 0.0)) {
            wallU = 1.0 - wallU;
        }
    }

    rayHits[x] = RayHit(
        distance,
        wallU,
        wallType,
        side
    );
}

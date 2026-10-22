struct FirstPerson {
    screen: vec4f, // canvas width, height, kind (0 none, 1 hands, 2 cockpit), wheel radians
    motion: vec4f, // hand swing, bob, strafe, action reach
    pose: vec4f,   // airborne, crouching, reserved, reserved
};

struct VertexOutput {
    @builtin(position) position: vec4f,
    @location(0) uv: vec2f,
    @location(1) @interpolate(flat) detail: u32,
};

@group(0) @binding(0) var<uniform> view: FirstPerson;
@group(0) @binding(1) var baseTexture: texture_2d<f32>;
@group(0) @binding(2) var detailTexture: texture_2d<f32>;
@group(0) @binding(3) var imageSampler: sampler;

fn rotate(point: vec2f, radians: f32) -> vec2f {
    let c = cos(radians);
    let s = sin(radians);
    return vec2f(c * point.x - s * point.y, s * point.x + c * point.y);
}

@vertex
fn vertexMain(@builtin(vertex_index) vertex: u32, @builtin(instance_index) instance: u32) -> VertexOutput {
    let corners = array<vec2f, 6>(
        vec2f(0.0, 0.0), vec2f(1.0, 0.0), vec2f(0.0, 1.0),
        vec2f(0.0, 1.0), vec2f(1.0, 0.0), vec2f(1.0, 1.0)
    );
    let corner = corners[vertex];
    let screen = view.screen.xy;
    var output: VertexOutput;
    output.uv = corner;
    output.detail = 0u;
    var pixel = corner * screen;

    if (view.screen.z == 1.0) {
        // Each quad samples only its own half of the transparent hands image.
        output.uv.x = (corner.x + f32(instance)) * 0.5;
        let dimensions = vec2f(textureDimensions(baseTexture));
        let imageHeight = screen.x * dimensions.y / dimensions.x;
        let pivot = vec2f((0.25 + 0.5 * f32(instance)) * screen.x, screen.y * 1.28);
        pixel = vec2f(output.uv.x * screen.x, screen.y * 1.28 - imageHeight + corner.y * imageHeight);
        let side = select(-1.0, 1.0, instance == 1u);
        let swing = view.motion.x * side;
        var offset = vec2f(swing * 0.007 - view.motion.z * 0.012,
            swing * 0.018 + view.motion.y * 0.007 + select(select(0.0, 0.03, view.pose.y > 0.0), -0.04, view.pose.x > 0.0));
        var degrees = swing * 3.0 + view.motion.z * 2.0 + select(0.0, side * -5.0, view.pose.x > 0.0);
        if (instance == 1u) {
            offset -= view.motion.w * vec2f(0.05, 0.08);
            degrees -= view.motion.w * 12.0;
        }
        pixel = pivot + rotate(pixel - pivot, radians(degrees)) + offset * screen;
    } else if (instance == 1u) {
        output.detail = 1u;
        let dimensions = vec2f(textureDimensions(detailTexture));
        let size = vec2f(screen.x * 0.28, screen.x * 0.28 * dimensions.y / dimensions.x);
        pixel = screen * vec2f(0.42, 0.86) + rotate((corner - 0.5) * size, view.screen.w);
    }

    output.position = vec4f(pixel / screen * vec2f(2.0, -2.0) + vec2f(-1.0, 1.0), 0.0, 1.0);
    return output;
}

@fragment
fn fragmentMain(input: VertexOutput) -> @location(0) vec4f {
    // Uploaded pixels are premultiplied; blend over the completed world without depth/fog.
    if (input.detail == 1u) {
        return textureSampleLevel(detailTexture, imageSampler, input.uv, 0.0);
    }
    return textureSampleLevel(baseTexture, imageSampler, input.uv, 0.0);
}

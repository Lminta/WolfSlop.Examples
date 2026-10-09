struct VertexOutput {
    @builtin(position) position: vec4f,
    @location(0) color: vec3f,
};

@vertex
fn vertexMain(
    @builtin(vertex_index) index: u32
) -> VertexOutput {
    var positions = array<vec2f, 3>(
        vec2f( 0.0,  0.7),
        vec2f(-0.7, -0.7),
        vec2f( 0.7, -0.7)
    );

    var colors = array<vec3f, 3>(
        vec3f(1.0, 0.0, 0.0),
        vec3f(0.0, 1.0, 0.0),
        vec3f(0.0, 0.0, 1.0)
    );

    var output: VertexOutput;

    output.position = vec4f(
        positions[index],
        0.0,
        1.0
    );

    output.color = colors[index];

    return output;
}

@fragment
fn fragmentMain(
    input: VertexOutput
) -> @location(0) vec4f {
    return vec4f(input.color, 1.0);
}
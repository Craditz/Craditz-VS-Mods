#version 330 core
uniform sampler2D leafTex;
uniform float opacity;
in vec2 uv;
layout(location = 0) out vec4 outColor;
layout(location = 1) out vec4 outGlow;
void main() {
    vec4 leaf = texture(leafTex, uv);
    if (leaf.a < 0.1) discard;
    outColor = vec4(leaf.rgb, leaf.a * opacity);
    outGlow = vec4(0.0, 0.0, 0.0, leaf.a * opacity);
}

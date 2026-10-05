#version 330 core

uniform sampler2D primaryFb;
uniform float brightnessLift;
uniform float grayStrength;
uniform float darknessStart;
uniform float darknessRange;
uniform float darknessCurve;
uniform float ambientStrength;
uniform float highlightProtection;

in vec2 uv;
out vec4 outColor;

void main()
{
    vec4 source = texture(primaryFb, uv);
    float luminance = dot(source.rgb, vec3(0.2126, 0.7152, 0.0722));
    float endOfDarkness = darknessStart + max(darknessRange, 0.001);
    float darkness = 1.0 - smoothstep(darknessStart, endOfDarkness, luminance);
    darkness = pow(clamp(darkness, 0.0, 1.0), max(darknessCurve, 0.05));
    darkness *= clamp(ambientStrength, 0.0, 1.0);

    float peak = max(max(source.r, source.g), source.b);
    float brightSpot = smoothstep(0.25, 0.65, peak);
    darkness *= 1.0 - brightSpot * clamp(highlightProtection, 0.0, 1.0);

    vec3 grayscale = vec3(luminance);
    vec3 adapted = mix(source.rgb, grayscale, darkness * clamp(grayStrength, 0.0, 1.0));
    float lift = darkness * max(brightnessLift, 0.0);
    float liftGamma = 1.0 / (1.0 + lift);
    adapted = pow(max(adapted, vec3(0.0)), vec3(liftGamma));

    outColor = vec4(adapted, source.a);
}

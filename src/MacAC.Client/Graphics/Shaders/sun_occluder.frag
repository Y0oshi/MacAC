#version 430 core

layout(location = 0) in vec2 vSt;
layout(location = 0) out vec4 oTint;

#include "haze_shared.glsl"

void main()
{
    float depth = MACAC_SAMPLE_2D(uTextureIndexA, vSt).r;
    float unobstructedHeavens = smoothstep(0.9975, 0.99995, depth);
    float enabled = uAtmosphereSunScreen.z * uAtmosphereWeather.w;
    oTint = vec4(vec3(unobstructedHeavens * enabled), 1.0);
}

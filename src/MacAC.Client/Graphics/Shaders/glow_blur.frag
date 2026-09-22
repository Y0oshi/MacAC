#version 430 core

layout(location = 0) in vec2 vSt;
layout(location = 0) out vec4 oTint;

#include "haze_shared.glsl"

void main()
{
    vec2 hopUv = uPackParams0.xy;
    vec3 value = MACAC_SAMPLE_2D(uTextureIndexA, vSt).rgb * 0.227027;
    value += MACAC_SAMPLE_2D(uTextureIndexA, vSt + hopUv * 1.384615).rgb * 0.316216;
    value += MACAC_SAMPLE_2D(uTextureIndexA, vSt - hopUv * 1.384615).rgb * 0.316216;
    value += MACAC_SAMPLE_2D(uTextureIndexA, vSt + hopUv * 3.230769).rgb * 0.070270;
    value += MACAC_SAMPLE_2D(uTextureIndexA, vSt - hopUv * 3.230769).rgb * 0.070270;
    oTint = vec4(value, 1.0);
}

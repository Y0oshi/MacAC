#version 430 core

layout(location = 0) in vec2 vSt;
layout(location = 0) out vec4 oTint;

#include "haze_shared.glsl"

float loFusedBitmaskTexel(ivec2 coordinate, vec2 bitmaskDims)
{
    ivec2 maximum = ivec2(bitmaskDims) - ivec2(1);
    ivec2 clampedCoordinate = clamp(coordinate, ivec2(0), maximum);
    vec2 zDepthUv = (vec2(clampedCoordinate) + vec2(0.5)) / bitmaskDims;
    float depth = MACAC_SAMPLE_2D(uTextureIndexA, zDepthUv).r;
    float unobstructedHeavens = smoothstep(0.9975, 0.99995, depth);
    float enabled = uAtmosphereSunScreen.z * uAtmosphereWeather.w;

    return round(clamp(unobstructedHeavens * enabled, 0.0, 1.0) * 255.0) / 255.0;
}

float specimenLoFusedBitmask(vec2 uv)
{
    vec2 bitmaskDims = uPackParams1.yz;
    vec2 texel = uv * bitmaskDims - vec2(0.5);
    ivec2 lower = ivec2(floor(texel));
    vec2 fraction = fract(texel);
    float topLeft = loFusedBitmaskTexel(lower, bitmaskDims);
    float topRight = loFusedBitmaskTexel(lower + ivec2(1, 0), bitmaskDims);
    float bottomLeft = loFusedBitmaskTexel(lower + ivec2(0, 1), bitmaskDims);
    float bottomRight = loFusedBitmaskTexel(lower + ivec2(1, 1), bitmaskDims);
    return mix(
        mix(topLeft, topRight, fraction.x),
        mix(bottomLeft, bottomRight, fraction.x),
        fraction.y);
}

void main()
{
    const int SampleCount = 48;
    float decay = uPackParams0.x;
    float wgt = uPackParams0.y;
    float density = uPackParams0.z;
    vec2 delta = (vSt - uAtmosphereSunScreen.xy) * (density / float(SampleCount));
    vec2 specimenUv = vSt;
    float illumination = 1.0;
    float sum = 0.0;
    for (int i = 0; i < SampleCount; ++i)
{
        specimenUv -= delta;
        if (any(lessThan(specimenUv, vec2(0.0))) || any(greaterThan(specimenUv, vec2(1.0))))
            break;
        float mask = uPackParams1.x > 0.5
            ? specimenLoFusedBitmask(specimenUv)
            : MACAC_SAMPLE_2D(uTextureIndexA, specimenUv).r;
        sum += mask * illumination;
        illumination *= decay;
    }
    vec3 rays = uAtmosphereSunColor.rgb * (sum * wgt / float(SampleCount));
    oTint = vec4(rays, 1.0);
}

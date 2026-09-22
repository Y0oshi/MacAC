#ifndef MACAC_DIRECTIONAL_SHADOW_RECEIVER_GLSL
#define MACAC_DIRECTIONAL_SHADOW_RECEIVER_GLSL

#include "sunshade_shared.glsl"

int macacShadeCascade(float lensGapMeters)
{
    int count = int(uShadowTextureAndFlags.y);
    for (int i = 0; i < count; ++i)
{
        if (lensGapMeters <= uShadowSplitFarMeters[i])
            return i;
    }
    return -1;
}

float macacShadeBiasScaling(int cascade)
{
    int farawayCascade = max(int(uShadowTextureAndFlags.y) - 1, 0);
    mat4 tierMatrix = uShadowWorldToClip[cascade];
    mat4 farawayMatrix = uShadowWorldToClip[farawayCascade];
    float tierDensity = 0.5 * (
        length(vec3(tierMatrix[0][0], tierMatrix[1][0], tierMatrix[2][0]))
        + length(vec3(tierMatrix[0][1], tierMatrix[1][1], tierMatrix[2][1])));
    float farawayDensity = 0.5 * (
        length(vec3(farawayMatrix[0][0], farawayMatrix[1][0], farawayMatrix[2][0]))
        + length(vec3(farawayMatrix[0][1], farawayMatrix[1][1], farawayMatrix[2][1])));
    return clamp(farawayDensity / max(tierDensity, 1e-7), 0.0, 1.0);
}

float macacShadeBilinearContrast(
    int cascade,
    vec2 uv,
    float recipientZDepth)
{
    float resolution = max(float(uShadowTextureAndFlags.z), 1.0);
    vec2 texelLocus = uv * resolution - vec2(0.5);
    vec2 blend = fract(texelLocus);
    vec4 gatheredZDepth = textureGather(
        MACAC_TEXTURE(uShadowTextureAndFlags.x),
        vec3(uv, float(cascade)),
        0);
    vec4 compared = step(vec4(recipientZDepth), gatheredZDepth);
    float lower = mix(compared.w, compared.z, blend.x);
    float upper = mix(compared.x, compared.y, blend.x);
    return mix(lower, upper, blend.y);
}

float macacShadeCascadePcf(
    int cascade,
    vec3 recipientRealmLocus,
    vec3 realmNorm,
    vec3 canvasToLamp)
{
    float ndl = clamp(dot(realmNorm, canvasToLamp), 0.0, 1.0);
    vec3 cascadeBiasMeters = max(
        uShadowBiasMeters.xyz * macacShadeBiasScaling(cascade),
        vec3(0.001));
    float zDepthBiasMeters = cascadeBiasMeters.x
        + cascadeBiasMeters.y * (1.0 - ndl);
    vec3 biasedRealmLocus = recipientRealmLocus
        + realmNorm * cascadeBiasMeters.z
        + canvasToLamp * zDepthBiasMeters;

    vec4 shadeClip = uShadowWorldToClip[cascade]
        * vec4(biasedRealmLocus, 1.0);
    vec3 shadeNdc = shadeClip.xyz / max(abs(shadeClip.w), 1e-7);
    vec2 uv = vec2(
        shadeNdc.x * 0.5 + 0.5,
        0.5 - shadeNdc.y * 0.5);
    float recipientZDepth = shadeNdc.z;
    if (uv.x <= 0.0 || uv.x >= 1.0
        || uv.y <= 0.0 || uv.y >= 1.0
        || recipientZDepth <= 0.0 || recipientZDepth >= 1.0)
        return 1.0;

    int radius = int((uShadowTextureAndFlags.w >> 8u) & 0xFu);
    radius = clamp(radius, 0, 2);
    float texel = 1.0 / max(float(uShadowTextureAndFlags.z), 1.0);
    float softness = max(uShadowControl.y, 1.0);
    if (radius == 0)
        return macacShadeBilinearContrast(cascade, uv, recipientZDepth);

    float shaded = 0.0;
    float weightTotal = 0.0;
    if (radius == 1)
{
        for (int y = 0; y < 2; ++y)
{
            for (int x = 0; x < 2; ++x)
{
                vec2 offset = (vec2(x, y) - vec2(0.5))
                    * softness * texel;
                shaded += macacShadeBilinearContrast(
                    cascade, uv + offset, recipientZDepth);
                weightTotal += 1.0;
            }
        }
    } else
{
        for (int y = -1; y <= 1; ++y)
{
            for (int x = -1; x <= 1; ++x)
{
                float wgt = float(2 - abs(x)) * float(2 - abs(y));
                vec2 offset = vec2(x, y) * 1.5 * softness * texel;
                shaded += macacShadeBilinearContrast(
                    cascade, uv + offset, recipientZDepth) * wgt;
                weightTotal += wgt;
            }
        }
    }
    return shaded / max(weightTotal, 1.0);
}

float macacDirectedShadeVis(
    vec3 recipientRealmLocus,
    vec3 realmNorm,
    vec3 lensRealmLocus,
    vec3 canvasToLamp)
{
    if ((uShadowTextureAndFlags.w & 1u) == 0u)
        return 1.0;

    float lensGapMeters = length(
        recipientRealmLocus - lensRealmLocus);
    if (lensGapMeters > uShadowControl.z)
        return 1.0;

    int cascade = macacShadeCascade(lensGapMeters);
    if (cascade < 0)
        return 1.0;

    float visibility = macacShadeCascadePcf(
        cascade,
        recipientRealmLocus,
        realmNorm,
        canvasToLamp);
    int cascadeTally = int(uShadowTextureAndFlags.y);
    if (cascade + 1 < cascadeTally)
{
        float split = uShadowSplitFarMeters[cascade];
        float widthMeters = max(uShadowControl.w, 1e-4);
        float blend = smoothstep(
            max(0.0, split - widthMeters),
            split,
            lensGapMeters);
        if (blend > 0.0)
{
            float upcomingVis = macacShadeCascadePcf(
                cascade + 1,
                recipientRealmLocus,
                realmNorm,
                canvasToLamp);
            visibility = mix(visibility, upcomingVis, blend);
        }
    }
    float reachFade = 1.0 - smoothstep(
        max(0.0, uShadowControl.z - max(uShadowControl.w, 1e-4)),
        uShadowControl.z,
        lensGapMeters);
    float shadeWeight = clamp(uShadowControl.x, 0.0, 1.0) * reachFade;
    return mix(1.0, visibility, shadeWeight);
}

#endif

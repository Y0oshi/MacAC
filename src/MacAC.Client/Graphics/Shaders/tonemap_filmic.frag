#version 430 core

layout(location = 0) in vec2 vSt;
layout(location = 0) out vec4 oTint;

#include "haze_shared.glsl"

vec3 acesFitted(vec3 value)
{
    const float a = 2.51;
    const float b = 0.03;
    const float c = 2.43;
    const float d = 0.59;
    const float e = 0.14;
    return clamp((value * (a * value + b)) / (value * (c * value + d) + e), 0.0, 1.0);
}

vec3 specimenBloom(vec2 uv)
{
    return MACAC_SAMPLE_2D(uTextureIndexB, uv).rgb;
}

vec3 loFusedTableau(vec2 uv)
{
    vec3 scene = macacUnpackReadout(MACAC_SAMPLE_2D(uTextureIndexA, uv).rgb)
        + macacUnpackReadout(MACAC_SAMPLE_2D(uTextureIndexB, uv).rgb);
    if (uPackParams2.w > 0.5)
        scene += macacUnpackReadout(MACAC_SAMPLE_2D(uTextureIndexC, uv).rgb);
    return scene;
}

vec3 loFusedBloomDistill(vec3 scene)
{
    float bright = dot(scene, vec3(0.2126, 0.7152, 0.0722));
    float threshold = uPackParams2.y;
    float knee = max(uPackParams2.z, 0.0001);
    float soft = clamp((bright - threshold + knee) / (2.0 * knee), 0.0, 1.0);
    soft = soft * soft;
    float contribution = max(bright - threshold, 0.0) + soft * knee;
    contribution /= max(bright, 0.0001);
    return scene * contribution * uPackParams2.x;
}

vec3 loFusedBloom(vec3 middleTableau)
{
    const float offsets[5] = float[5](
        -3.230769, -1.384615, 0.0, 1.384615, 3.230769);
    const float wgts[5] = float[5](
        0.070270, 0.316216, 0.227027, 0.316216, 0.070270);
    vec3 bloom = vec3(0.0);
    for (int y = 0; y < 5; ++y)
{
        for (int x = 0; x < 5; ++x)
{
            vec3 scene = x == 2 && y == 2
                ? middleTableau
                : loFusedTableau(vSt + vec2(offsets[x], offsets[y]) * uPackParams3.xy);
            bloom += loFusedBloomDistill(scene) * (wgts[x] * wgts[y]);
        }
    }
    return bloom;
}

void main()
{
    vec3 hdr;
    if (uPackParams1.z > 0.5)
{
        vec3 scene = loFusedTableau(vSt);
        hdr = scene + loFusedBloom(scene);
    }
    else
{
        hdr = macacUnpackReadout(MACAC_SAMPLE_2D(uTextureIndexA, vSt).rgb)
            + specimenBloom(vSt)
            + macacUnpackReadout(MACAC_SAMPLE_2D(uTextureIndexC, vSt).rgb);
        if (uPackParams1.y > 0.5)
            hdr += macacUnpackReadout(MACAC_SAMPLE_2D(uTextureIndexD, vSt).rgb);
    }
    vec3 exposed = max(hdr * uPackParams0.x, vec3(0.0));
    vec3 linearClamped = clamp(exposed, 0.0, 1.0);
    vec3 color = mix(linearClamped, acesFitted(exposed), clamp(uPackParams1.x, 0.0, 1.0));

    float luma = dot(color, vec3(0.2126, 0.7152, 0.0722));
    color = mix(vec3(luma), color, uPackParams0.y);
    const float LinearMidGrey = 0.18;
    color = (color - LinearMidGrey) * uPackParams0.z + LinearMidGrey;

    vec2 centered = vSt * 2.0 - 1.0;
    float vignette = smoothstep(1.25, 0.25, dot(centered, centered));
    color *= mix(1.0, vignette, clamp(uPackParams0.w, 0.0, 1.0));
    oTint = vec4(macacPackReadout(clamp(color, 0.0, 1.0)), 1.0);
}

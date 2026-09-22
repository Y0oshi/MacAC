#version 430 core

layout(location = 0) in vec2 vSt;
layout(location = 0) out vec4 oTint;

#include "haze_shared.glsl"
#include "sunshade_shared.glsl"

vec3 reconstructRealm(vec2 uv, float depth)
{
    vec4 clip = vec4(uv.x * 2.0 - 1.0, (1.0 - uv.y) * 2.0 - 1.0, depth, 1.0);
    vec4 world = uAtmosphereInverseViewProjection * clip;
    return world.xyz / max(abs(world.w), 1e-6);
}

float directedVis(vec3 realmLocusVal)
{

    if (uint(round(uShadowLightDirectionAndSource.w)) != 1u)
        return 1.0;
    uint cascadeTally = clamp(uShadowTextureAndFlags.y, 1u, 4u);
    vec3 canvasToSun = normalize(uShadowLightDirectionAndSource.xyz);
    vec3 biasedLocus = realmLocusVal
        + canvasToSun * max(uShadowBiasMeters.x, 0.0);
    for (uint cascade = 0u; cascade < cascadeTally; ++cascade)
{
        vec4 clip = uShadowWorldToClip[cascade] * vec4(biasedLocus, 1.0);
        vec3 ndc = clip.xyz / max(abs(clip.w), 1e-6);
        vec2 uv = vec2(ndc.x * 0.5 + 0.5, 0.5 - ndc.y * 0.5);
        if (all(greaterThanEqual(uv, vec2(0.0)))
            && all(lessThanEqual(uv, vec2(1.0)))
            && ndc.z >= 0.0 && ndc.z <= 1.0)
{
            float stored = MACAC_SAMPLE_ARRAY(
                uShadowTextureAndFlags.x,
                vec3(uv, float(cascade))).r;
            float visible = ndc.z <= stored ? 1.0 : 0.0;
            return mix(1.0, visible, clamp(uShadowControl.x, 0.0, 1.0));
        }
    }
    return 1.0;
}

void main()
{
    float tableauZDepth = MACAC_SAMPLE_2D(uTextureIndexA, vSt).r;
    if (tableauZDepth >= 0.999999 || uPackParams0.w <= 0.0)
{
        oTint = vec4(0.0);
        return;
    }

    vec3 nearbyRealm = reconstructRealm(vSt, 0.0);
    vec3 tableauRealm = reconstructRealm(vSt, tableauZDepth);
    int steps = clamp(int(uPackParams0.z + 0.5), 1, 64);
    float shaded = 0.0;
    float ign = fract(52.9829189 * fract(dot(gl_FragCoord.xy, vec2(0.06711056, 0.00583715))));
    for (int step = 0; step < 64; ++step)
{
        if (step >= steps)
            break;
        float t = (float(step) + ign) / float(steps);
        shaded += directedVis(mix(nearbyRealm, tableauRealm, t));
    }

    float integrated = shaded / float(steps);
    float extinction = 1.0 - exp(-uPackParams0.x * length(tableauRealm - nearbyRealm));
    vec3 color = uAtmosphereSunColor.rgb
        * (integrated * extinction * uPackParams0.y);
    oTint = vec4(max(color, vec3(0.0)), 1.0);
}

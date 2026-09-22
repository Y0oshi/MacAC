#version 430 core
#extension GL_ARB_bindless_texture : require

in vec3 vNorm;
in vec2 vBmpCoord;
in vec3 vRealmSpot;
in vec3 vAmbientOwnLit;
in vec3 vDirectedLit;
in flat uint  vBitmapOrdinal;
in flat uint  vBitmapStratum;
in flat float vDensityMultiplier;
in flat vec2  vPickIllumination;
in flat uint  vReceivesDirectedShade;
in flat float vCanvasDensity;
in flat uint  vLotFlagSet;
in flat uint  vSpecificsBucket;

#include "sunshade_receive.glsl"
#include "canon_detail_material.glsl"

uniform int uRenderPass;
uniform int uLightDebug;
uniform uint uTextureIndexA;
uniform float uParamA;
uniform float uParamB;

struct Light {
    vec4 posAndKind;
    vec4 dirAndRange;
    vec4 colorAndIntensity;
    vec4 coneAngleEtc;
};
layout(std140, MACAC_UBO_SET binding = 1) uniform SceneLighting {
    Light uLights[8];
    vec4  uCellAmbient;
    vec4  uFogParams;
    vec4  uFogColor;
    vec4  uCameraAndTime;
};

vec3 enactFog(vec3 shaded, vec3 realmSpot)
{
    int mode = int(uFogParams.w);
    if (mode == 0) return shaded;
    float d = length(realmSpot - uCameraAndTime.xyz);
    float fogBegin = uFogParams.x;
    float fogFinish   = uFogParams.y;
    float span = max(1e-3, fogFinish - fogBegin);
    float fog = clamp((d - fogBegin) / span, 0.0, 1.0);
    return mix(shaded, uFogColor.xyz, fog);
}

out vec4 FragTint;

bool isCanonClipRef(float value)
{
    return abs(value - (100.0 / 255.0)) < 0.000001
        || abs(value - (200.0 / 255.0)) < 0.000001;
}

void main()
{
    // A part whose texture has not finished uploading carries the no-texture sentinel. The
    // descriptor table is partially bound, so sampling that slot is out of bounds and returns
    // whatever the driver has there — a coloured flash for the frame or two before the upload
    // lands. Draw nothing instead: the part appears when its texture does.
    if (vBitmapOrdinal == MACAC_TEXTURE_NONE) discard;

    vec4 color = MACAC_SAMPLE_ARRAY(vBitmapOrdinal, vec3(vBmpCoord, float(vBitmapStratum)));
    float opacityCutoff = isCanonClipRef(uParamB) ? uParamB : 0.05;

    vec3 canvasToLamp = normalize(uShadowLightDirectionAndSource.xyz);
    float directedVis = vReceivesDirectedShade != 0u
        ? macacDirectedShadeVis(
            vRealmSpot,
            normalize(vNorm),
            uCameraAndTime.xyz,
            canvasToLamp)
        : 1.0;
    vec3 tableauLit = vAmbientOwnLit
        + vDirectedLit * directedVis;
    vec3 shaded = vec3(vPickIllumination.x)
        + vPickIllumination.y * tableauLit;

    if (uLightDebug == 3)
{
        if (color.a < opacityCutoff) discard;
        FragTint = vec4(min(shaded, vec3(1.0)), 1.0);
        return;
    }

    shaded += uFogParams.z * vec3(0.6, 0.6, 0.75);

    shaded = min(shaded, vec3(1.0));

    bool specificsEngaged = uParamA != 0.0
        && vSpecificsBucket != 0u
        && (vLotFlagSet & 1u) != 0u;
    vec3 rgb;
    float alpha;
    if (specificsEngaged)
{
        vec4 detail = MACAC_SAMPLE_ARRAY(
            uTextureIndexA,
            vec3(vBmpCoord * uParamA, 0.0));
        RetailDetailMaterialResult material = macacRetailDetailMaterial(
            color.rgb,
            shaded,
            detail,
            vCanvasDensity * vDensityMultiplier);
        rgb = material.rgb;
        alpha = material.alpha;
    } else
{
        rgb = color.rgb * shaded;
        alpha = color.a * vDensityMultiplier;
    }

    if (specificsEngaged
            ? isCanonClipRef(uParamB) && alpha < uParamB
            : color.a < opacityCutoff)
        discard;

    if (!specificsEngaged || (uRenderPass & 0x200) == 0)
        rgb = enactFog(rgb, vRealmSpot);
    FragTint = vec4(rgb, alpha);
}

#version 430 core
#extension GL_ARB_shader_draw_parameters : require

layout(location = 0) in vec3 aLocus;
layout(location = 1) in vec3 aNorm;
layout(location = 2) in vec2 aBmpCoord;

struct InstanceData {
    mat4 transform;
};

struct BatchData {
    uint  textureIndex;
    float surfaceOpacity;
    uint  textureLayer;
    uint  flags;
};

layout(std430, binding = 0) readonly buffer InstanceBuffer {
    InstanceData Instances[];
};

layout(std430, binding = 1) readonly buffer BatchBuffer {
    BatchData Batches[];
};

layout(std430, binding = 3) readonly buffer ClipSlotBuf {
    uint instanceClipSlot[];
};

struct GlobalLight {
    vec4 posAndKind;
    vec4 dirAndRange;
    vec4 colorAndIntensity;
    vec4 coneAngleEtc;
};
layout(std430, binding = 4) readonly buffer GlobalLightBuf {
    GlobalLight gLights[];
};
layout(std430, binding = 5) readonly buffer InstanceLightSetBuf {
    int instanceLightIdx[];
};

layout(std430, binding = 6) readonly buffer InstanceIndoorBuf {
    uint instanceIndoor[];
};

layout(std430, binding = 7) readonly buffer InstanceAlphaBuf {
    float instanceAlpha[];
};

layout(std430, binding = 8) readonly buffer InstanceSelectionLightingBuf {
    vec2 instanceSelectionLighting[];
};

layout(std430, binding = 9) readonly buffer InstanceDetailCategoryBuf {
    uint instanceDetailCategory[];
};

uniform mat4 uViewProjection;

uniform uint uTextureIndexB;

uniform int uDrawIDOffset;
uniform int uLightingMode;
uniform int uLightDebug;

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

vec3 ptContribution(vec3 N, vec3 realmSpot, GlobalLight L)
{
    int kind     = int(L.posAndKind.w);
    vec3 toLdir     = L.posAndKind.xyz - realmSpot;
    float distsq = dot(toLdir, toLdir);
    float d      = sqrt(distsq);
    float range  = L.dirAndRange.w;
    if (d >= range || range <= 1e-4) return vec3(0.0);
    float intensity = L.colorAndIntensity.w;
    vec3  rootTint   = L.colorAndIntensity.xyz;

    if (L.coneAngleEtc.y > 0.5)
{
        if (uLightDebug == 2) return vec3(0.0);
        vec3 Ldir = toLdir / max(d, 1e-4);
        float ndl = max(0.0, dot(N, Ldir));
        if (ndl <= 0.0) return vec3(0.0);
        if (kind == 2)
{
            if (dot(-Ldir, L.dirAndRange.xyz) <= cos(L.coneAngleEtc.x * 0.5)) return vec3(0.0);
        }
        return (intensity * ndl / max(d, 1e-3)) * rootTint;
    }

    float angular = (uLightingMode == 1)
        ? (1.0 / 1.5) * (dot(N, toLdir) + 0.5 * d)
        : max(0.0, dot(N, toLdir));
    if (angular <= 0.0) return vec3(0.0);
    float norm   = (distsq > 1.0) ? (distsq * d) : d;
    float scale  = (1.0 - d / range) * intensity * (angular / norm);
    if (kind == 2)
{
        vec3 Ldir = toLdir / max(d, 1e-4);
        float cos_edge = cos(L.coneAngleEtc.x * 0.5);
        float cos_l    = dot(-Ldir, L.dirAndRange.xyz);
        if (cos_l <= cos_edge) scale = 0.0;
    }
    return min(scale * rootTint, rootTint);
}

vec3 amassLamps(vec3 N, vec3 realmSpot, int instOrdinal)
{
    vec3 shaded = uCellAmbient.xyz;
    if (uLightDebug == 1) return shaded;

    if (uLightingMode == 0)
{
        if (instanceIndoor[instOrdinal] == 0u)
{
            int engagedLamps = int(uCellAmbient.w);
            for (int i = 0; i < 8; ++i)
{
                if (i >= engagedLamps) break;
                if (int(uLights[i].posAndKind.w) != 0) continue;
                vec3 Ldir = -uLights[i].dirAndRange.xyz;
                float ndl = max(0.0, dot(N, Ldir));
                shaded += uLights[i].colorAndIntensity.xyz * uLights[i].colorAndIntensity.w * ndl;
            }
        }
    }

    vec3 ptAcc = vec3(0.0);
    int lampStride = (uLightingMode == 1) ? 47 : 8;
    int base = instOrdinal * lampStride;
    for (int k = 0; k < lampStride; ++k)
{
        int gi = instanceLightIdx[base + k];
        if (gi < 0) continue;
        ptAcc += ptContribution(N, realmSpot, gLights[gi]);
    }
    shaded += min(ptAcc, vec3(1.0));

    return shaded;
}

out vec3 vNorm;
out vec2 vBmpCoord;
out vec3 vRealmSpot;
out vec3 vShaded;
out flat uint  vBitmapOrdinal;
out flat uint  vBitmapStratum;
out flat float vDensityMultiplier;
out flat vec2  vPickIllumination;
out flat float vCanvasDensity;
out flat uint  vLotFlagSet;
out flat uint  vSpecificsBucket;

void main()
{
    int xformOrdinal = gl_BaseInstanceARB + gl_InstanceID;
    int instOrdinal = xformOrdinal - int(uTextureIndexB);
    mat4 model = Instances[xformOrdinal].transform;
    vDensityMultiplier = instanceAlpha[instOrdinal];
    vPickIllumination = (uLightingMode == 0)
        ? instanceSelectionLighting[instOrdinal]
        : vec2(0.0, 1.0);

    vec4 realmSpot = model * vec4(aLocus, 1.0);
    gl_Position = uViewProjection * realmSpot;

    vRealmSpot = realmSpot.xyz;
    vNorm = normalize(mat3(model) * aNorm);
    vShaded = amassLamps(vNorm, vRealmSpot, instOrdinal);
    vBmpCoord = aBmpCoord;

    BatchData b = Batches[uDrawIDOffset + gl_DrawIDARB];
    vBitmapOrdinal = b.textureIndex;
    vBitmapStratum = b.textureLayer;
    vCanvasDensity = b.surfaceOpacity;
    vLotFlagSet = b.flags;
    vSpecificsBucket = instanceDetailCategory[instOrdinal];
}

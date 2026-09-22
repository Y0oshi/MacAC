#version 460 core
#extension GL_ARB_bindless_texture : require

layout(location = 0) in vec3  aSpot;
layout(location = 1) in vec3  aNorm;
layout(location = 2) in uvec4 aPacked0;
layout(location = 3) in uvec4 aPacked1;
layout(location = 4) in uvec4 aPacked2;
layout(location = 5) in uvec4 aPacked3;

uniform mat4 uViewProjection;

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

out vec2  vRootST;
out vec3  vRealmNorm;
out vec3  vRealmSpot;
out vec3  vIlluminationRGB;
out vec4  vOverlay0;
out vec4  vOverlay1;
out vec4  vOverlay2;
out vec4  vRoad0;
out vec4  vRoad1;
flat out float vBaseTexIdx;

const float MIN_FACTOR = 0.0;

vec4 unpackTopLayerStratum(uint bmpIndexU, uint alphaIndexU, uint rotIndex, vec2 rootST)
{
    float bmpIndex   = float(bmpIndexU);
    float alphaIndex = float(alphaIndexU);
    if (bmpIndex   >= 254.0) bmpIndex   = -1.0;
    if (alphaIndex >= 254.0) alphaIndex = -1.0;

    vec2 rotatedST = rootST;
    if      (rotIndex == 1u) rotatedST = vec2(1.0 - rootST.y,       rootST.x);
    else if (rotIndex == 2u) rotatedST = vec2(1.0 - rootST.x, 1.0 - rootST.y);
    else if (rotIndex == 3u) rotatedST = vec2(      rootST.y, 1.0 - rootST.x);

    return vec4(rotatedST.x, rotatedST.y, bmpIndex, alphaIndex);
}

void main()
{

    uint rotOvl0 = (aPacked3.x >> 2u) & 3u;
    uint rotOvl1 = (aPacked3.x >> 4u) & 3u;
    uint rotOvl2 = (aPacked3.x >> 6u) & 3u;
    uint rotRd0  =  aPacked3.y        & 3u;
    uint rotRd1  = (aPacked3.y >> 2u) & 3u;
    uint divideDirection= (aPacked3.y >> 4u) & 1u;

    int vIndex = gl_VertexID % 6;
    int corner = 0;
    if (divideDirection == 0u)
{
        if      (vIndex == 0) corner = 0;
        else if (vIndex == 1) corner = 1;
        else if (vIndex == 2) corner = 2;
        else if (vIndex == 3) corner = 0;
        else if (vIndex == 4) corner = 2;
        else                corner = 3;
    } else
{
        if      (vIndex == 0) corner = 0;
        else if (vIndex == 1) corner = 1;
        else if (vIndex == 2) corner = 3;
        else if (vIndex == 3) corner = 1;
        else if (vIndex == 4) corner = 2;
        else                corner = 3;
    }

    vec2 rootST;
    if      (corner == 0) rootST = vec2(0.0, 1.0);
    else if (corner == 1) rootST = vec2(1.0, 1.0);
    else if (corner == 2) rootST = vec2(1.0, 0.0);
    else                  rootST = vec2(0.0, 0.0);

    vRootST = rootST;
    vRealmSpot = aSpot;
    vRealmNorm = normalize(aNorm);

    vec3 sunDirection = uLights[0].dirAndRange.xyz;
    vec3 solTint = uLights[0].colorAndIntensity.xyz * uLights[0].colorAndIntensity.w;
    float L = max(dot(vRealmNorm, -sunDirection), MIN_FACTOR);
    vIlluminationRGB = solTint * L + uCellAmbient.xyz;

    float baseBmp = float(aPacked0.x);
    if (baseBmp >= 254.0) baseBmp = -1.0;
    vBaseTexIdx = baseBmp;

    vOverlay0 = unpackTopLayerStratum(aPacked0.z, aPacked0.w, rotOvl0, rootST);
    vOverlay1 = unpackTopLayerStratum(aPacked1.x, aPacked1.y, rotOvl1, rootST);
    vOverlay2 = unpackTopLayerStratum(aPacked1.z, aPacked1.w, rotOvl2, rootST);
    vRoad0    = unpackTopLayerStratum(aPacked2.x, aPacked2.y, rotRd0,  rootST);
    vRoad1    = unpackTopLayerStratum(aPacked2.z, aPacked2.w, rotRd1,  rootST);

    vec3 landSpot = vec3(aSpot.xy, aSpot.z - 0.01);
    gl_Position = uViewProjection * vec4(landSpot, 1.0);
}

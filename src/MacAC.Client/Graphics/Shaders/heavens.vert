#version 430 core

layout(location = 0) in vec3 aSpot;
layout(location = 1) in vec3 aNorm;
layout(location = 2) in vec2 aBmp;

layout(std140, MACAC_UBO_SET binding = 4) uniform SkyParams {
    mat4  uModel;
    mat4  uSkyView;
    mat4  uSkyProjection;

    vec3  uAmbientColor;
    float uEmissive;
    vec3  uSunColor;
    float uDiffuseFactor;
    vec3  uSunDir;
    float uTransparency;
    vec2  uUvScroll;
    float uApplyFog;
    float uSurfOpacity;
};

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

out vec2 vBmp;
out vec3 vTint;
out float vMistScalar;
out vec3 vDirection;

void main()
{
    vBmp = aBmp + uUvScroll;
    vDirection = aSpot;
    gl_Position = uSkyProjection * uSkyView * uModel * vec4(aSpot, 1.0);

    vec3 realmNorm = normalize(mat3(uModel) * aNorm);

    float diff = max(dot(realmNorm, uSunDir), 0.0);
    vec3 shaded = vec3(uEmissive)
             + uAmbientColor
             + (uSunColor * uDiffuseFactor) * diff;
    vTint = clamp(shaded, 0.0, 1.0);

    vec4  realmSpot = uModel * vec4(aSpot, 1.0);
    float dist = length(realmSpot.xyz);
    float fogBegin = uFogParams.x;
    float fogFinish   = uFogParams.y;
    float span     = max(fogFinish - fogBegin, 1e-3);
    vMistScalar = clamp((fogFinish - dist) / span, 0.0, 1.0);
}

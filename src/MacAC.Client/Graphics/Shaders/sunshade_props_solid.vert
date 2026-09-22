#version 460 core
#extension GL_ARB_shader_draw_parameters : require

#include "sunshade_shared.glsl"
#include "haze_shared.glsl"
#include "leaf_sway.glsl"

layout(location = 0) in vec3 aLocus;
layout(location = 1) in vec3 aNorm;
layout(location = 2) in vec2 aBmpCoord;

struct InstanceData { mat4 transform; };
struct BatchData {
    uint textureIndex;
    uint _pad;
    uint textureLayer;
    uint flags;
};
layout(std430, binding = 0) readonly buffer InstanceBuffer {
    InstanceData Instances[];
};
layout(std430, binding = 1) readonly buffer BatchBuffer {
    BatchData Batches[];
};

uniform int uDrawIDOffset;
uniform int uRenderPass;

void main()
{
    int instOrdinal = gl_BaseInstanceARB + gl_InstanceID;
    mat4 model = Instances[instOrdinal].transform;
    BatchData batch = Batches[uDrawIDOffset + gl_DrawIDARB];
    vec4 realmLocusVal = model * vec4(aLocus, 1.0);
    realmLocusVal.xyz = macacFoliageDisplace(
        realmLocusVal.xyz,
        model[3].xyz,
        batch.flags,
        uAtmosphereClockWind,
        uAtmosphereWindAmplitude);
    gl_Position = uShadowWorldToClip[uRenderPass] * realmLocusVal;
}

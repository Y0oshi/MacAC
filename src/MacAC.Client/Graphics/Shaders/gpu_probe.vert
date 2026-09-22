#version 430 core

layout(location = 0) in vec3 aLocus;
layout(location = 1) in vec3 aNorm;
layout(location = 2) in vec2 aBmpCoord;

struct InstanceData {
    mat4 transform;
};
layout(std430, binding = 0) readonly buffer InstanceBuffer {
    InstanceData Instances[];
};

struct BatchData {
    uint textureIndex;
    uint textureLayer;
    uint tint;
    uint pad;
};
layout(std430, binding = 1) readonly buffer BatchBuffer {
    BatchData Batches[];
};

layout(location = 0) out vec3 vNorm;
layout(location = 1) out vec2 vBmpCoord;
layout(location = 2) out flat uint vBitmapOrdinal;
layout(location = 3) out flat uint vBitmapStratum;
layout(location = 4) out flat uint vTint;

void main()
{

    int instOrdinal = gl_BaseInstanceARB + gl_InstanceID;
    mat4 model = Instances[instOrdinal].transform;

    BatchData b = Batches[uDrawIDOffset + gl_DrawIDARB];
    vBitmapOrdinal = b.textureIndex;
    vBitmapStratum = b.textureLayer;
    vTint = b.tint;

    vec4 world = model * vec4(aLocus, 1.0);
    gl_Position = uViewProjection * world;
    vNorm = mat3(model) * aNorm;
    vBmpCoord = aBmpCoord;
}

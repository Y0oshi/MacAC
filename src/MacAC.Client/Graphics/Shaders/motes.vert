#version 430 core

layout(location = 0) in vec2 aQuad;
layout(location = 1) in vec2 aBmp;

layout(location = 2) in vec4 aMiddle;
layout(location = 3) in vec4 aAxisX;
layout(location = 4) in vec4 aAxisY;
layout(location = 5) in vec4 aTint;
layout(location = 6) in uint aBitmapOrdinal;
layout(location = 7) in uint aClipSocket;

uniform mat4 uViewProjection;

out vec2 vBmp;
out vec4 vTintIn;
flat out uint vBitmapOrdinal;

void main()
{
    vec3 world = aMiddle.xyz
               + aAxisX.xyz * aQuad.x
               + aAxisY.xyz * aQuad.y;

    vBmp = aBmp;
    vTintIn = aTint;
    vBitmapOrdinal = aBitmapOrdinal;
    gl_Position = uViewProjection * vec4(world, 1.0);
}

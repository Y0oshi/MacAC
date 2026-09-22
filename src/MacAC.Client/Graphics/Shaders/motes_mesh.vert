#version 430 core

layout(location = 0) in vec3 aLocus;
layout(location = 1) in vec3 aNorm;
layout(location = 2) in vec2 aBmpCoord;
layout(location = 3) in mat4 aScheme;
layout(location = 7) in vec4 aTint;
layout(location = 8) in uint aClipSocket;

uniform mat4 uViewProjection;

out vec2 vBmpCoord;
out vec4 vTintIn;

void main()
{
    vBmpCoord = aBmpCoord;
    vTintIn = aTint;
    gl_Position = uViewProjection * aScheme * vec4(aLocus, 1.0);
}

#version 430 core
layout(location = 0) in vec3 aSpot;
layout(location = 1) in vec3 aTint;

uniform mat4 uViewProjection;

out vec3 vTintIn;

void main()
{
    vTintIn = aTint;
    gl_Position = uViewProjection * vec4(aSpot, 1.0);
}

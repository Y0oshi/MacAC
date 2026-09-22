#version 430 core
layout(location = 0) in vec2 aSpot;
layout(location = 1) in vec2 aSt;
layout(location = 2) in vec4 aTint;

uniform float uParamA;
uniform float uParamB;

out vec2 vSt;
out vec4 vTintIn;

void main()
{
    vec2 ndc = vec2(
        aSpot.x / uParamA * 2.0 - 1.0,
        1.0 - aSpot.y / uParamB * 2.0);
    gl_Position = vec4(ndc, 0.0, 1.0);
    vSt = aSt;
    vTintIn = aTint;
}

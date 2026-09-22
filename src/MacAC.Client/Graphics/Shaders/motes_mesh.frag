#version 430 core
#extension GL_ARB_bindless_texture : require

in vec2 vBmpCoord;
in vec4 vTintIn;
out vec4 fragTint;

uniform uint uTextureIndexA;
uniform float uParamA;

void main()
{
    vec4 color = MACAC_SAMPLE_ARRAY(uTextureIndexA, vec3(vBmpCoord, uParamA)) * vTintIn;
    if (color.a < 0.02)
        discard;
    fragTint = color;
}

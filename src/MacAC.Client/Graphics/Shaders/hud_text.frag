#version 430 core
#extension GL_ARB_bindless_texture : require
in vec2 vSt;
in vec4 vTintIn;
out vec4 FragTint;

uniform uint uTextureIndexA;
uniform uint uTextureIndexB;

const uint kUnassignedBitmapSocket = 0xFFFFFFFFu;

void main()
{
    if (uTextureIndexB != kUnassignedBitmapSocket)
{
        float coverage = MACAC_SAMPLE_2D(uTextureIndexB, vSt).r;
        FragTint = vec4(vTintIn.rgb, vTintIn.a * coverage);
    } else if (uTextureIndexA != kUnassignedBitmapSocket)
{

        FragTint = MACAC_SAMPLE_2D(uTextureIndexA, vSt) * vTintIn;
    } else
{
        FragTint = vTintIn;
    }
    if (FragTint.a < 0.005) discard;
}

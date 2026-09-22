#version 430 core
#extension GL_ARB_bindless_texture : require

in vec2 vBmp;
in vec4 vTintIn;
flat in uint vBitmapOrdinal;
out vec4 fragTint;

void main()
{
    vec4 texel;
    if (vBitmapOrdinal != MACAC_TEXTURE_NONE)
{
        texel = MACAC_SAMPLE_ARRAY(vBitmapOrdinal, vec3(vBmp, 0.0));
    } else
{
        vec2 d = vBmp - vec2(0.5, 0.5);
        float r = length(d) * 2.0;
        float fadeVal = smoothstep(1.0, 0.4, r);
        texel = vec4(1.0, 1.0, 1.0, fadeVal);
    }

    vec4 color = texel * vTintIn;
    if (color.a < 0.02)
        discard;

    fragTint = color;
}

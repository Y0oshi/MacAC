#version 430 core

layout(location = 0) in vec3 vNorm;
layout(location = 1) in vec2 vBmpCoord;
layout(location = 2) in flat uint vBitmapOrdinal;
layout(location = 3) in flat uint vBitmapStratum;
layout(location = 4) in flat uint vTint;

layout(location = 0) out vec4 FragTint;

void main()
{
    vec4 tint = vec4(
        float((vTint >> 24) & 0xFFu) / 255.0,
        float((vTint >> 16) & 0xFFu) / 255.0,
        float((vTint >> 8) & 0xFFu) / 255.0,
        float(vTint & 0xFFu) / 255.0);

    vec4 baseTint = tint;
    if (uLightingMode == 0)
{
        baseTint = texture(
            MACAC_TEXTURE(vBitmapOrdinal),
            vec3(vBmpCoord, float(vBitmapStratum))) * tint;
    }

    if (uLightingMode == 0)
{
        vec3 light = normalize(vec3(0.4, -0.6, 0.7));
        float lambert = 0.35 + (0.65 * max(dot(normalize(vNorm), light), 0.0));
        baseTint.rgb *= lambert;
    }

    if (baseTint.a < 0.004)
{
        discard;
    }

    FragTint = baseTint;
}

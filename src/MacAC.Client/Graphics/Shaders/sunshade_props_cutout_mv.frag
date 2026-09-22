#version 460 core
#extension GL_ARB_bindless_texture : require

in vec2 vShadeBmpCoord;
flat in uint vShadowTextureIndex;
flat in uint vShadowTextureLayer;

void main()
{
    vec4 texel = MACAC_SAMPLE_ARRAY(
        vShadowTextureIndex,
        vec3(vShadeBmpCoord, float(vShadowTextureLayer)));
    if (texel.a < 0.05)
        discard;
}

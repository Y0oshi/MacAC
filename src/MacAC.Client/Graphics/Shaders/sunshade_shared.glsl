#ifndef MACAC_DIRECTIONAL_SHADOW_COMMON_GLSL
#define MACAC_DIRECTIONAL_SHADOW_COMMON_GLSL

layout(std140, MACAC_PACK_UBO_SET binding = 6) uniform DirectionalShadow {
    mat4  uShadowWorldToClip[4];
    vec4  uShadowSplitFarMeters;
    vec4  uShadowControl;
    vec4  uShadowBiasMeters;
    uvec4 uShadowTextureAndFlags;
    vec4  uShadowLightDirectionAndSource;
};

#endif

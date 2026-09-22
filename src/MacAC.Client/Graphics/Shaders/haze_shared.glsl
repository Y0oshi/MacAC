#ifndef MACAC_ATMOSPHERIC_COMMON_GLSL
#define MACAC_ATMOSPHERIC_COMMON_GLSL

layout(std140, MACAC_PACK_UBO_SET binding = 5) uniform AtmosphericFrame {
    vec4 uAtmosphereSunScreen;
    vec4 uAtmosphereSunColor;
    vec4 uAtmosphereViewport;
    vec4 uAtmosphereWeather;
    vec4 uAtmosphereSunDirection;
    vec4 uAtmospherePolicy;
    mat4 uAtmosphereInverseViewProjection;
    vec4 uAtmosphereClockWind;
    vec4 uAtmosphereWindAmplitude;
};

layout(std140, MACAC_PACK_UBO_SET binding = 7) uniform PackPass {
    vec4 uPackParams0;
    vec4 uPackParams1;
    vec4 uPackParams2;
    vec4 uPackParams3;
};

layout(std140, MACAC_PACK_UBO_SET binding = 8) uniform PackSettings {
    vec4 uPackSettings[16];
};

vec3 macacUnpackReadout(vec3 c)
{
    return pow(max(c, vec3(0.0)), vec3(2.2));
}

vec3 macacPackReadout(vec3 c)
{
    return pow(max(c, vec3(0.0)), vec3(1.0 / 2.2));
}

#endif

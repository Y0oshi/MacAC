#version 430 core

layout(location = 0) in vec2 vSt;
layout(location = 0) out vec4 oTint;

#include "haze_shared.glsl"

void main()
{
    vec3 scene = macacUnpackReadout(MACAC_SAMPLE_2D(uTextureIndexA, vSt).rgb)
        + macacUnpackReadout(MACAC_SAMPLE_2D(uTextureIndexB, vSt).rgb);
    if (uPackParams0.w > 0.5)
        scene += macacUnpackReadout(MACAC_SAMPLE_2D(uTextureIndexC, vSt).rgb);
    float bright = dot(scene, vec3(0.2126, 0.7152, 0.0722));
    float threshold = uPackParams0.y;
    float knee = max(uPackParams0.z, 0.0001);
    float soft = clamp((bright - threshold + knee) / (2.0 * knee), 0.0, 1.0);
    soft = soft * soft;
    float contribution = max(bright - threshold, 0.0) + soft * knee;
    contribution /= max(bright, 0.0001);
    vec3 bloom = scene * contribution * uPackParams0.x;
    oTint = vec4(bloom, 1.0);
}

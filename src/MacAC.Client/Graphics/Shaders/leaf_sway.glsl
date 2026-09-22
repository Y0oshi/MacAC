#ifndef MACAC_FOLIAGE_WIND_GLSL
#define MACAC_FOLIAGE_WIND_GLSL

vec3 macacFoliageDisplace(
    vec3 realmSpot,
    vec3 instOrigin,
    uint lotFlagSet,
    vec4 timerWind,
    vec4 amp)
{
    if ((lotFlagSet & 0x6u) == 0u)
        return realmSpot;

    float t = timerWind.x;
    float mean = timerWind.y;
    float gust = timerWind.z;
    float directionA = timerWind.w;

    vec2 dir = vec2(cos(directionA), sin(directionA));
    vec2 perp = vec2(-dir.y, dir.x);

    float h = clamp((realmSpot.z - instOrigin.z) / max(amp.w, 0.5), 0.0, 1.0);
    float k = h * h;

    float ph = dot(instOrigin.xy, vec2(0.137, 0.291));

    float g = 0.5 + 0.5 * sin(0.05 * t + ph) + 0.25 * sin(0.13 * t + 1.7 * ph);
    float s = mean + gust * g;

    float lean = k * amp.x * s * (0.8 + 0.2 * sin(0.35 * t + ph));
    vec2 d = dir * lean;

    if ((lotFlagSet & 0x2u) != 0u)
{
        float branch = k * amp.y * s * sin(1.1 * t + ph + 2.0 * h);
        vec2 relativeXY = realmSpot.xy - instOrigin.xy;
        float vh = fract(sin(dot(relativeXY, vec2(12.9898, 78.233))) * 43758.5453);
        float flutter = h * amp.z * s * sin(6.0 * t + 7.0 * vh);
        d += dir * branch + perp * 0.35 * branch
            + vec2(cos(6.2832 * vh), sin(6.2832 * vh)) * flutter;
    }

    vec3 p = realmSpot;
    p.xy += d;

    p.z -= 0.5 * dot(d, d) / max(h * amp.w, 0.5);
    return p;
}

#endif

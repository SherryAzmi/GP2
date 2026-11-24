// Small procedural-noise helpers shared by the GP2 environment shaders (sand, sea, grass).
#ifndef GP2_NOISE_INCLUDED
#define GP2_NOISE_INCLUDED

float gp2Hash(float2 p)
{
    p = frac(p * float2(123.34, 456.21));
    p += dot(p, p + 45.32);
    return frac(p.x * p.y);
}

float2 gp2Hash2(float2 p)
{
    float n = gp2Hash(p);
    return float2(n, gp2Hash(p + n * 17.17));
}

float gp2Noise(float2 p)
{
    float2 i = floor(p), f = frac(p);
    float2 u = f * f * (3.0 - 2.0 * f);
    return lerp(lerp(gp2Hash(i), gp2Hash(i + float2(1, 0)), u.x),
                lerp(gp2Hash(i + float2(0, 1)), gp2Hash(i + float2(1, 1)), u.x), u.y);
}

// Fractal noise, 4 octaves, roughly 0..1.
float gp2Fbm(float2 p)
{
    float v = 0.0, a = 0.5;
    [unroll] for (int i = 0; i < 4; i++)
    {
        v += a * gp2Noise(p);
        p = p * 2.03 + float2(17.1, 3.7);
        a *= 0.5;
    }
    return v / 0.9375;
}

// Fades detail out with distance (and grazing angles) so fine patterns don't shimmer in VR.
float gp2DetailFade(float3 positionWS, float range)
{
    return saturate(1.0 - distance(positionWS, _WorldSpaceCameraPos) / range);
}

#endif

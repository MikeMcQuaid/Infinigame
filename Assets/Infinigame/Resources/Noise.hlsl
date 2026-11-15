// Value noise shared by the ground and sky shaders, where the sun is for the body being drawn,
// and what a world's air does to sunlight.
#ifndef INFINIGAME_NOISE
#define INFINIGAME_NOISE

// Set every frame by SolarSystem: the sun's position in the scene and the colour of its light in open space.
float4 _SunPosition;
half4 _SunLight, _SecondSunLight;
float4 _SecondSunPosition;
half3 SecondStarlight(float3 normal)
{
    float3 centre = float3(UNITY_MATRIX_M._m03, UNITY_MATRIX_M._m13, UNITY_MATRIX_M._m23);
    return _SecondSunLight.rgb * saturate(dot(normal, normalize(_SecondSunPosition.xyz - centre + 0.001)));
}

// The direction to the sun from the centre of the object being drawn. Each world is lit from where
// the sun stands for it, not for the camera, so far-off worlds show the right phase.
float3 SunDirection()
{
    return normalize(_SunPosition.xyz - float3(UNITY_MATRIX_M._m03, UNITY_MATRIX_M._m13, UNITY_MATRIX_M._m23));
}

// The colour a great depth of air scatters sunlight to, for air whose colour overhead is sky: far
// paler, since every colour is scattered in the end and dust scatters them all alike. It is the
// colour of the sky at the horizon and of haze. Shapes.Haze is the same sum.
float3 Pale(float3 sky)
{
    return lerp(pow(sky / max(max(sky.r, sky.g), max(sky.b, 1e-3)), 0.25), 1.0, 0.15);
}

// Sunlight scattered towards the eye by air of some thickness. Each colour scatters at its own
// rate, so a little air shows the sky's own deep colour and a lot of it, towards the horizon or
// along the limb, pales.
float3 Scattered(float thickness, float3 sky)
{
    return (1.0 - exp(-thickness * pow(sky / max(max(sky.r, sky.g), max(sky.b, 1e-3)), 0.7))) * Pale(sky);
}

// What a world's air does to the look of its surface from out in space: a veil of scattered
// sunlight, thin looking straight down and thick along the limb, where it runs into the halo that
// the Atmosphere shader draws beyond it. sky is the air's colour with its thickness in alpha.
// Nothing changes for an eye inside the air, where the scene's own haze does this.
half3 Veil(half3 colour, float3 positionWS, half4 sky, float airRadius)
{
    float3 centre = float3(UNITY_MATRIX_M._m03, UNITY_MATRIX_M._m13, UNITY_MATRIX_M._m23);
    float outside = saturate(sky.a) * smoothstep(0.8, 1.4, distance(_WorldSpaceCameraPos, centre) / airRadius);
    if (outside <= 0.0) return colour;
    float3 up = normalize(positionWS - centre);
    float3 glow = Scattered(0.17 * outside / max(dot(up, normalize(_WorldSpaceCameraPos - positionWS)), 0.12), sky.rgb);
    // what the air scatters towards the eye it has taken from the light coming up through it
    return colour * (1.0 - glow / Pale(sky.rgb)) + glow * smoothstep(-0.25, 0.15, dot(up, SunDirection()));
}

// A fixed grain, the same for a pixel every frame, to break up the steps that a smooth sky
// would otherwise show where the frame's colours run out of precision.
float Grain(float2 pixel)
{
    return frac(52.9829189 * frac(dot(pixel, float2(0.06711056, 0.00583715)))) - 0.5;
}

float Hash(float3 p)
{
    p = frac(p * 0.3183099 + 0.1) * 17.0;
    return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
}

float Noise(float3 p)
{
    float3 i = floor(p);
    float3 f = frac(p);
    f = f * f * (3.0 - 2.0 * f);
    return lerp(
        lerp(lerp(Hash(i), Hash(i + float3(1, 0, 0)), f.x), lerp(Hash(i + float3(0, 1, 0)), Hash(i + float3(1, 1, 0)), f.x), f.y),
        lerp(lerp(Hash(i + float3(0, 0, 1)), Hash(i + float3(1, 0, 1)), f.x), lerp(Hash(i + float3(0, 1, 1)), Hash(i + float3(1, 1, 1)), f.x), f.y),
        f.z);
}

float Layers(float3 p, int octaves)
{
    float sum = 0.0;
    float amplitude = 0.5;
    for (int i = 0; i < octaves; i++)
    {
        sum += amplitude * Noise(p);
        p = p * 2.02 + 7.3;
        amplitude *= 0.5;
    }
    return sum;
}

float Layers(float3 p)
{
    return Layers(p, 5);
}

#endif

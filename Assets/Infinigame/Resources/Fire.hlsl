// The colour of something hot enough to glow: dull red, then orange, yellow and white, and far
// brighter the hotter it is. Shared by everything that burns. Effects.Fire is the same sum.
#ifndef INFINIGAME_FIRE
#define INFINIGAME_FIRE

half3 Fire(float heat)
{
    float t = clamp(heat, 1e-4, 1.6);
    return half3(1.0, 0.9, 0.7) * pow(min(t, 1.0), float3(1.0, 2.2, 5.0)) * (14.0 * t * t);
}

#endif

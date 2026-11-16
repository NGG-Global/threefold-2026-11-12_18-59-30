#ifndef THREEFOLD_ILLUSTRATED_INPUT_INCLUDED
#define THREEFOLD_ILLUSTRATED_INPUT_INCLUDED

// Material properties of "Threefold/Illustrated Lit", shared by all of its passes. Every pass
// must see the same UnityPerMaterial block, or the SRP Batcher rejects the shader.

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/SurfaceInput.hlsl"

CBUFFER_START(UnityPerMaterial)
    float4 _BaseMap_ST;
    half4 _BaseColor;
    half4 _ShadeColor;
    half4 _HighlightColor;
    half4 _EmissionColor;
    half4 _OutlineColor;
    half4 _TopTint;
    half _Cutoff;
    half _Wrap;
    half _HighlightSize;
    half _PaintStrength;
    half _PaintScale;
    half _OutlineWidth;
    half _OutlineFadeStart;
    half _OutlineFadeEnd;
CBUFFER_END

// Set by the game for all materials at once: 0 draws outlines, 1 hides them. A shader global
// that is never set reads as 0, so outlines show without any script running.
half _ThreefoldOutlineSuppress;

#if defined(_PAINT_VARIATION)
TEXTURE2D(_PaintMap);
SAMPLER(sampler_PaintMap);

// Slow drifts of value and warmth across the world, as a painter varies a wash. Its main job is
// to hide the repetition of a tiled texture on large floors and walls. One texture sample serves
// any surface: height is skewed into both axes, so walls vary as floors do.
//
//
// The same sample also places weathering: on faces that look up (tops of walls, ledges, paving),
// in soft patches, the colour is multiplied by _TopTint, as moss and damp stain stone where rain
// lies. It needs no second texture and no second keyword; a strength of 0 leaves the colour as it
// was. The painted lines of the base map stay, because the tint multiplies.
//
// Keep in step with IllustratedLightingReference.ApplyPaintVariation and TopTintAmount in C#.
half3 ApplyPaintVariation(half3 albedo, float3 positionWS, half3 normalWS)
{
    float2 uv = (positionWS.xz + positionWS.y * float2(0.37, 0.61)) * _PaintScale;
    half2 paint = SAMPLE_TEXTURE2D(_PaintMap, sampler_PaintMap, uv).rg - 0.5;

    albedo *= 1.0 + paint.r * (2.0 * _PaintStrength);
    albedo *= 1.0 + paint.g * _PaintStrength * half3(1.0, 0.2, -1.0);

    half facesUp = saturate(normalWS.y * 2.5 - 1.0);
    half patch = saturate((paint.g + 0.07) * 5.0);
    albedo *= lerp(half3(1.0, 1.0, 1.0), _TopTint.rgb, _TopTint.a * facesUp * patch);
    return max(albedo, 0.0);
}
#endif

#endif

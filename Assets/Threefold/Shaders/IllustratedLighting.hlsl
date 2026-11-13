#ifndef THREEFOLD_ILLUSTRATED_LIGHTING_INCLUDED
#define THREEFOLD_ILLUSTRATED_LIGHTING_INCLUDED

// The light response of Soft Illustrated Realism (Docs/ArtBible.md, section 7):
//
//   soft, directional light that describes shape     a wrapped, eased ramp instead of N.L
//   controlled shadow transitions, no cartoon bands  one smooth curve, never a step
//   ambient fill that keeps painted colour visible   ambient is always added in full
//   warm and cool contrast                           the fill is tinted where light is absent
//   restrained highlights                            an optional, weak Blinn-Phong term
//
// The same arithmetic exists in C# (IllustratedLighting.cs), where it is tested and used to
// render the preview images. Change both together.

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

// How quickly the shadow map takes over on the lit side of the terminator.
#define ILLUSTRATED_SHADOW_FADE 8.0

// Share of a light that reaches a surface, from its angle alone. With wrap, light carries a
// little past the terminator; the easing removes the hard start and end of the ramp.
half IllustratedRamp(half ndl, half wrap)
{
    half wrapped = saturate((ndl + wrap) / (1.0 + wrap));
    return wrapped * wrapped * (3.0 - 2.0 * wrapped);
}

// Past the terminator a shadow map can only cut wrapped light off along a hard, aliased edge.
// There the ramp alone shapes the form, and the shadow map fades in on the lit side.
half IllustratedShadow(half ndl, half shadowAttenuation)
{
    return lerp(1.0, shadowAttenuation, saturate(ndl * ILLUSTRATED_SHADOW_FADE));
}

half IllustratedLightAmount(Light light, half3 normalWS, half wrap)
{
    half ndl = dot(normalWS, light.direction);
    return IllustratedRamp(ndl, wrap) * IllustratedShadow(ndl, light.shadowAttenuation) * light.distanceAttenuation;
}

#if defined(_HIGHLIGHT)
// A small, soft highlight. Its strength is the alpha of the highlight colour, which the material
// presets keep low: metal reads as metal without mirror-like shine.
half3 IllustratedHighlight(Light light, half3 normalWS, half3 viewDirectionWS, half amount)
{
    float3 halfVector = SafeNormalize(float3(light.direction) + float3(viewDirectionWS));
    half ndh = half(saturate(dot(normalWS, halfVector)));
    float exponent = exp2(10.0 * _HighlightSize + 1.0);
    half shape = half(pow(float(ndh), exponent));
    return light.color * _HighlightColor.rgb * (_HighlightColor.a * shape * amount);
}
#endif

// inputData must carry positionWS, normalWS, viewDirectionWS, shadowCoord, vertexLighting and
// normalizedScreenSpaceUV. The variable has to be called inputData: URP's light loop macros
// refer to it by that name.
half3 IllustratedLighting(InputData inputData, half3 albedo, half3 ambient)
{
    half4 shadowMask = CalculateShadowMask(inputData);
    Light mainLight = GetMainLight(inputData.shadowCoord, inputData.positionWS, shadowMask);

    half mainAmount = IllustratedLightAmount(mainLight, inputData.normalWS, _Wrap);
    half3 direct = mainLight.color * mainAmount;
    half3 highlight = half3(0.0, 0.0, 0.0);

#if defined(_HIGHLIGHT)
    highlight += IllustratedHighlight(mainLight, inputData.normalWS, inputData.viewDirectionWS, mainAmount);
#endif

    // Where the main light does not reach, the ambient fill takes the shade tint. A cool tint
    // against a warm sun gives the warm and cool contrast of the style.
    half3 fill = ambient * lerp(_ShadeColor.rgb, half3(1.0, 1.0, 1.0), mainAmount);

#if defined(_ADDITIONAL_LIGHTS)
    uint pixelLightCount = GetAdditionalLightsCount();

    #if USE_CLUSTER_LIGHT_LOOP
    [loop] for (uint lightIndex = 0; lightIndex < min(URP_FP_DIRECTIONAL_LIGHTS_COUNT, MAX_VISIBLE_LIGHTS); lightIndex++)
    {
        CLUSTER_LIGHT_LOOP_SUBTRACTIVE_LIGHT_CHECK

        Light light = GetAdditionalLight(lightIndex, inputData.positionWS, shadowMask);
        direct += light.color * IllustratedLightAmount(light, inputData.normalWS, _Wrap);
    }
    #endif

    LIGHT_LOOP_BEGIN(pixelLightCount)
        Light light = GetAdditionalLight(lightIndex, inputData.positionWS, shadowMask);
        direct += light.color * IllustratedLightAmount(light, inputData.normalWS, _Wrap);
    LIGHT_LOOP_END
#endif

#if defined(_ADDITIONAL_LIGHTS_VERTEX)
    direct += inputData.vertexLighting;
#endif

    return albedo * (direct + fill) + highlight;
}

#endif

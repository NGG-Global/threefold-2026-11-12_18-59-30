#ifndef THREEFOLD_ILLUSTRATED_FORWARD_PASS_INCLUDED
#define THREEFOLD_ILLUSTRATED_FORWARD_PASS_INCLUDED

// The lit pass of "Threefold/Illustrated Lit". Its structure follows URP's Simple Lit forward
// pass (URP 17.6); only the lighting function is its own. Ambient light is evaluated per vertex,
// which is cheap and exact for the flat-shaded environment.
//
// Not supported, by choice, to keep the shader small: lightmaps, light probes volumes, normal
// maps, decals, light cookies, light layers and transparency.

#include "IllustratedInput.hlsl"
#include "IllustratedLighting.hlsl"

struct Attributes
{
    float4 positionOS : POSITION;
    float3 normalOS   : NORMAL;
    float2 texcoord   : TEXCOORD0;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct Varyings
{
    float2 uv            : TEXCOORD0;
    float3 positionWS    : TEXCOORD1;
    half3 normalWS       : TEXCOORD2;
    half4 ambientAndFog  : TEXCOORD3; // xyz: ambient light, w: fog factor
#if defined(_ADDITIONAL_LIGHTS_VERTEX)
    half3 vertexLight    : TEXCOORD4;
#endif
#if defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR)
    float4 shadowCoord   : TEXCOORD5;
#endif
    float4 positionCS    : SV_POSITION;
    UNITY_VERTEX_INPUT_INSTANCE_ID
    UNITY_VERTEX_OUTPUT_STEREO
};

Varyings IllustratedVertex(Attributes input)
{
    Varyings output = (Varyings)0;

    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_TRANSFER_INSTANCE_ID(input, output);
    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

    VertexPositionInputs vertexInput = GetVertexPositionInputs(input.positionOS.xyz);
    VertexNormalInputs normalInput = GetVertexNormalInputs(input.normalOS);

    output.uv = TRANSFORM_TEX(input.texcoord, _BaseMap);
    output.positionWS = vertexInput.positionWS;
    output.positionCS = vertexInput.positionCS;
    output.normalWS = NormalizeNormalPerVertex(normalInput.normalWS);

#if defined(_FOG_FRAGMENT)
    half fogFactor = 0;
#else
    half fogFactor = ComputeFogFactor(vertexInput.positionCS.z);
#endif

    output.ambientAndFog = half4(SampleSH(output.normalWS), fogFactor);

#if defined(_ADDITIONAL_LIGHTS_VERTEX)
    output.vertexLight = VertexLighting(vertexInput.positionWS, normalInput.normalWS);
#endif

#if defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR)
    output.shadowCoord = GetShadowCoord(vertexInput);
#endif

    return output;
}

half4 IllustratedFragment(Varyings input) : SV_Target
{
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

    half4 albedoAlpha = SampleAlbedoAlpha(input.uv, TEXTURE2D_ARGS(_BaseMap, sampler_BaseMap));

    // Discards the pixel when alpha clipping is on and the pixel is below the cutoff.
    Alpha(albedoAlpha.a, _BaseColor, _Cutoff);

    half3 albedo = albedoAlpha.rgb * _BaseColor.rgb;

    InputData inputData = (InputData)0;
    inputData.positionWS = input.positionWS;
    inputData.normalWS = NormalizeNormalPerPixel(input.normalWS);

#if defined(_PAINT_VARIATION)
    albedo = ApplyPaintVariation(albedo, input.positionWS, inputData.normalWS);
#endif

    inputData.viewDirectionWS = GetWorldSpaceNormalizeViewDir(input.positionWS);

#if defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR)
    inputData.shadowCoord = input.shadowCoord;
#elif defined(MAIN_LIGHT_CALCULATE_SHADOWS)
    inputData.shadowCoord = TransformWorldToShadowCoord(inputData.positionWS);
#else
    inputData.shadowCoord = float4(0, 0, 0, 0);
#endif

    inputData.fogCoord = InitializeInputDataFog(float4(input.positionWS, 1.0), input.ambientAndFog.w);

#if defined(_ADDITIONAL_LIGHTS_VERTEX)
    inputData.vertexLighting = input.vertexLight;
#else
    inputData.vertexLighting = half3(0, 0, 0);
#endif

    inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);

    half3 color = IllustratedLighting(inputData, albedo, input.ambientAndFog.xyz);

#if defined(_EMISSION)
    color += _EmissionColor.rgb;
#endif

    color = MixFog(color, inputData.fogCoord);
    return half4(color, 1.0);
}

#endif

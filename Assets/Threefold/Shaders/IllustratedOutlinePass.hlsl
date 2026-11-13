#ifndef THREEFOLD_ILLUSTRATED_OUTLINE_PASS_INCLUDED
#define THREEFOLD_ILLUSTRATED_OUTLINE_PASS_INCLUDED

// The contour line of "Threefold/Illustrated Lit": the mesh is drawn a second time, turned
// inside out and pushed outwards along its normals, in one flat colour. Only the rim shows
// around the object. This is the "inverted hull" the art bible proposes for selected characters
// (Docs/ArtBible.md, section 6). It costs one extra draw of the mesh and no full-screen work.
//
//   The line has the same width on screen at any distance, given as a share of the screen's
//   height, so it does not thicken or thin as the camera moves and does not depend on resolution.
//   Beyond the fade distances the push shrinks to nothing: distant objects carry no outline.
//   Fog is applied, so a line never stands out against haze.
//
// Flat-shaded meshes have a separate normal on every face, which tears the hull open along every
// edge. With _OUTLINE_SMOOTH_NORMALS the push follows smoothed normals instead, which the model
// importer stores in the tangent channel (see CharacterModelPostprocessor). Unity skins tangents
// like normals, so this also holds while a character animates.
//
// Keep the width and fade arithmetic in step with OutlineRules in the C# reference.

#include "IllustratedInput.hlsl"

struct OutlineAttributes
{
    float4 positionOS : POSITION;
    float3 normalOS   : NORMAL;
    float4 tangentOS  : TANGENT;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct OutlineVaryings
{
    float4 positionCS : SV_POSITION;
    float3 positionWS : TEXCOORD0;
    half fogFactor    : TEXCOORD1;
    UNITY_VERTEX_INPUT_INSTANCE_ID
    UNITY_VERTEX_OUTPUT_STEREO
};

OutlineVaryings OutlineVertex(OutlineAttributes input)
{
    OutlineVaryings output = (OutlineVaryings)0;

    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_TRANSFER_INSTANCE_ID(input, output);
    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

#if defined(_OUTLINE_SMOOTH_NORMALS)
    float3 directionOS = input.tangentOS.xyz;
#else
    float3 directionOS = input.normalOS;
#endif

    VertexPositionInputs vertexInput = GetVertexPositionInputs(input.positionOS.xyz);
    float4 positionCS = vertexInput.positionCS;

    float3 directionWS = TransformObjectToWorldNormal(directionOS);
    float3 directionCS = TransformWorldToHClipDir(directionWS);

    // Work in screen proportions, so the line is equally wide in every direction.
    float aspect = _ScreenParams.x / _ScreenParams.y;
    float2 screenDirection = directionCS.xy * float2(aspect, 1.0);
    float screenLength = length(screenDirection);
    screenDirection = screenLength > 0.00001 ? screenDirection / screenLength : float2(0.0, 0.0);

    // positionCS.w is the distance in front of the camera, in metres.
    float fade = 1.0 - smoothstep(_OutlineFadeStart, _OutlineFadeEnd, positionCS.w);
    float width = _OutlineWidth * fade * (1.0 - saturate(_ThreefoldOutlineSuppress));

    // Clip space spans two units across the screen's height; multiplying by w undoes perspective.
    positionCS.xy += screenDirection * float2(1.0 / aspect, 1.0) * (2.0 * width * positionCS.w);

    output.positionCS = positionCS;
    output.positionWS = vertexInput.positionWS;

#if defined(_FOG_FRAGMENT)
    output.fogFactor = 0;
#else
    output.fogFactor = ComputeFogFactor(positionCS.z);
#endif

    return output;
}

half4 OutlineFragment(OutlineVaryings input) : SV_Target
{
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

    half fogCoord = InitializeInputDataFog(float4(input.positionWS, 1.0), input.fogFactor);
    half3 color = MixFog(_OutlineColor.rgb, fogCoord);
    return half4(color, 1.0);
}

#endif

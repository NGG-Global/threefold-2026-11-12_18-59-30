// The surface shader of Threefold's art direction, Soft Illustrated Realism (Docs/ArtBible.md).
//
// A painted base colour map is the foundation. Light falls on it softly: a wrapped, eased ramp
// instead of a hard terminator, ambient fill that takes a cool tint in shade, and an optional
// restrained highlight. An optional contour line (inverted hull) is drawn in a second pass.
//
// Written for URP 17.6 (Unity 6000.6), forward and forward+ rendering. See Docs/VisualStyle.md.
Shader "Threefold/Illustrated Lit"
{
    Properties
    {
        [MainTexture] _BaseMap("Painted Base Map", 2D) = "white" {}
        [MainColor] _BaseColor("Base Colour", Color) = (1, 1, 1, 1)

        [Header(Light response)]
        _ShadeColor("Shade Tint (ambient light in shade is multiplied by this)", Color) = (0.8, 0.86, 1, 1)
        _Wrap("Terminator Softness", Range(0, 1)) = 0.35

        [Header(Highlight)]
        [Toggle(_HIGHLIGHT)] _Highlight("Highlight", Float) = 0
        _HighlightColor("Colour (alpha is the strength)", Color) = (1, 0.97, 0.9, 0.25)
        _HighlightSize("Tightness", Range(0, 1)) = 0.5

        [Header(Paint variation across the world)]
        [Toggle(_PAINT_VARIATION)] _PaintVariation("Paint Variation", Float) = 0
        [NoScaleOffset] _PaintMap("Variation Map (R value, G warmth)", 2D) = "gray" {}
        _PaintStrength("Strength", Range(0, 0.5)) = 0.12
        _PaintScale("Repeats Per Metre", Range(0.01, 1)) = 0.08

        [Header(Emission)]
        [Toggle(_EMISSION)] _Emission("Emission", Float) = 0
        [HDR] _EmissionColor("Colour", Color) = (0, 0, 0, 1)

        [Header(Contour line)]
        _OutlineColor("Colour", Color) = (0.17, 0.11, 0.09, 1)
        _OutlineWidth("Width (share of screen height)", Range(0, 0.01)) = 0.0022
        _OutlineFadeStart("Fade Start (metres)", Float) = 14
        _OutlineFadeEnd("Fade End (metres)", Float) = 32
        [Toggle(_OUTLINE_SMOOTH_NORMALS)] _OutlineSmoothNormals("Smoothed Normals Are Stored In Tangents", Float) = 0

        [Header(Surface)]
        [Toggle(_ALPHATEST_ON)] _AlphaClip("Alpha Clipping", Float) = 0
        _Cutoff("Alpha Cutoff", Range(0, 1)) = 0.5
        [Enum(UnityEngine.Rendering.CullMode)] _Cull("Cull", Float) = 2
        [ToggleOff(_RECEIVE_SHADOWS_OFF)] _ReceiveShadows("Receive Shadows", Float) = 1
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "Queue" = "Geometry"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }
        LOD 200

        Pass
        {
            Name "ForwardLit"
            Tags
            {
                "LightMode" = "UniversalForward"
            }

            ZWrite On
            Cull [_Cull]

            HLSLPROGRAM
            #pragma target 2.0

            #pragma vertex IllustratedVertex
            #pragma fragment IllustratedFragment

            // Material keywords
            #pragma shader_feature_local_fragment _PAINT_VARIATION
            #pragma shader_feature_local_fragment _HIGHLIGHT
            #pragma shader_feature_local_fragment _EMISSION
            #pragma shader_feature_local_fragment _ALPHATEST_ON
            #pragma shader_feature_local _RECEIVE_SHADOWS_OFF

            // Universal Pipeline keywords
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fragment _ _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"

            // Unity keywords
            #pragma multi_compile_instancing

            #include "IllustratedForwardPass.hlsl"
            ENDHLSL
        }

        // The contour line. URP draws passes tagged SRPDefaultUnlit together with the lit pass.
        // It is switched on or off per material (Material.SetShaderPassEnabled), and is off on
        // everything except selected characters.
        Pass
        {
            Name "Outline"
            Tags
            {
                "LightMode" = "SRPDefaultUnlit"
            }

            ZWrite On
            Cull Front

            HLSLPROGRAM
            #pragma target 2.0

            #pragma vertex OutlineVertex
            #pragma fragment OutlineFragment

            #pragma shader_feature_local_vertex _OUTLINE_SMOOTH_NORMALS
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"
            #pragma multi_compile_instancing

            #include "IllustratedOutlinePass.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags
            {
                "LightMode" = "ShadowCaster"
            }

            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull [_Cull]

            HLSLPROGRAM
            #pragma target 2.0

            #pragma vertex ShadowPassVertex
            #pragma fragment ShadowPassFragment

            #pragma shader_feature_local _ALPHATEST_ON
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            #include "IllustratedInput.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/ShadowCasterPass.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags
            {
                "LightMode" = "DepthOnly"
            }

            ZWrite On
            ColorMask R
            Cull [_Cull]

            HLSLPROGRAM
            #pragma target 2.0

            #pragma vertex DepthOnlyVertex
            #pragma fragment DepthOnlyFragment

            #pragma shader_feature_local _ALPHATEST_ON
            #pragma multi_compile_instancing

            #include "IllustratedInput.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/DepthOnlyPass.hlsl"
            ENDHLSL
        }

        // Needed by renderer features that read scene normals, such as ambient occlusion.
        Pass
        {
            Name "DepthNormals"
            Tags
            {
                "LightMode" = "DepthNormals"
            }

            ZWrite On
            Cull [_Cull]

            HLSLPROGRAM
            #pragma target 2.0

            #pragma vertex DepthNormalsVertex
            #pragma fragment DepthNormalsFragment

            #pragma shader_feature_local _ALPHATEST_ON
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RenderingLayers.hlsl"
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            #pragma multi_compile_instancing

            #include "IllustratedInput.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/SimpleLitDepthNormalsPass.hlsl"
            ENDHLSL
        }
    }

    // Used when a device supports none of the SubShaders above. It does not cover compile errors:
    // a variant that fails to compile is drawn with Unity's error shader.
    FallBack "Universal Render Pipeline/Simple Lit"
}

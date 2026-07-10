// UI 圆形遮罩着色器（URP）。
// 在 Image/RawImage 上按 UV 到圆心距离裁出圆形，边缘用 fwidth 做屏幕空间自适应抗锯齿：
// 任意缩放与分辨率下边缘恒为约 1 像素平滑过渡，无锯齿感。
// 兼容 Unity UI 的 RectMask2D（_ClipRect）与 Mask（Stencil）裁剪，支持顶点色 Tint 与纹理采样。
//
// 注：URP 包不带 UnityUI.hlsl，故此处自包含声明 _ClipRect / _TextureSampleAdd 与
// UnityGet2DClipping（实现与内置 UnityUI.cginc 一致），不依赖额外 include。
Shader "GameLogic/UICircleMask"
{
    Properties
    {
        [PerRendererData]_MainTex("Sprite Texture", 2D) = "white" {}
        _Color("Tint", Color) = (1, 1, 1, 1)
        _Radius("Circle Radius (0~0.5)", Range(0.01, 0.5)) = 0.5
        _Softness("Edge Softness (0=1px AA, 越大越柔)", Range(0, 8)) = 0
        _Center("Circle Center UV", Vector) = (0.5, 0.5, 0, 0)
        // --- Unity UI 裁剪支持（与内置 UI/Default 一致）---
        _StencilComp("Stencil Comparison", Float) = 8
        _Stencil("Stencil ID", Float) = 0
        _StencilOp("Stencil Operation", Float) = 0
        _StencilWriteMask("Stencil Write Mask", Float) = 255
        _StencilReadMask("Stencil Read Mask", Float) = 255
        _ColorMask("Color Mask", Float) = 15
        [Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip("Use Alpha Clip", Float) = 0
        _ClipRect("Clip Rect", Vector) = (-32767, -32767, 32767, 32767)
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Transparent"
            "RenderType" = "Transparent"
            "IgnoreProjector" = "True"
            "CanUseSpriteAtlas" = "True"
        }

        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }

        Pass
        {
            Name "UICircleMask"

            Cull Off
            ZWrite Off
            ZTest [unity_GUIZTestMode]
            Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha
            ColorMask [_ColorMask]

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            // Unity UI 全局参数（由 Canvas / RectMask2D 下发）。_TextureSampleAdd 对 Image 通常为 0。
            float4 _ClipRect;
            float4 _TextureSampleAdd;

            // 矩形裁剪：矩形内返回 1、外返回 0（与内置 UnityGet2DClipping 实现一致）。
            float UnityGet2DClipping(float2 position, float4 clipRect)
            {
                return step(clipRect.x, position.x)
                     * step(position.x, clipRect.z)
                     * step(clipRect.y, position.y)
                     * step(position.y, clipRect.w);
            }

            struct Attributes
            {
                float4 positionOS : POSITION;
                half4  color      : COLOR;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionHCS   : SV_POSITION;
                half4  color         : COLOR;
                float2 uv            : TEXCOORD0;
                float4 worldPosition : TEXCOORD1; // UI 裁剪用 object-space 坐标，与内置 UI/Default 一致
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            CBUFFER_START(UnityPerMaterial)
                half4  _Color;
                float4 _MainTex_ST;
                float  _Radius;
                float  _Softness;
                float4 _Center;
            CBUFFER_END

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);

                output.positionHCS   = TransformObjectToHClip(input.positionOS.xyz);
                output.worldPosition = input.positionOS;
                output.uv            = TRANSFORM_TEX(input.uv, _MainTex);
                output.color         = input.color * _Color;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);

                half4 tex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv) + _TextureSampleAdd;

                // 圆形遮罩：dist 为 UV 空间到圆心的距离（0=圆心）。
                // fwidth 返回屏幕空间导数，约等于 1 像素对应的 dist 变化量；
                // 以此作为 smoothstep 过渡宽度，缩放/分辨率变化时边缘始终约 1px，无锯齿。
                // _Softness 用于在抗锯齿基础上额外放大边缘过渡，做柔边圆。
                float2 d    = input.uv - _Center.xy;
                float  dist = length(d);
                float  aa   = fwidth(dist);
                float  edge = aa * (1.0 + _Softness);
                float  circleAlpha = 1.0 - smoothstep(_Radius - edge, _Radius + edge, dist);

                half4 color = tex * input.color;
                color.a *= (half)circleAlpha;

                #ifdef UNITY_UI_CLIP_RECT
                color.a *= UnityGet2DClipping(input.worldPosition.xy, _ClipRect);
                #endif

                #ifdef UNITY_UI_ALPHACLIP
                clip(color.a - 0.001);
                #endif

                return color;
            }
            ENDHLSL
        }
    }
}

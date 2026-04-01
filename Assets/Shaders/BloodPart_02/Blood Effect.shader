Shader "URP/Particles/Blood Effect"
{
    Properties
    {
        [Header (Color Controls)]
        [HDR] _BaseColor ("Base Color Mult", Color) = (1,1,1,1)
        _LightStr ("(Unused in URP version)", float) = 0.85
        _AlphaMin ("Alpha Clip Min", Range (-0.01, 1.01)) = 0.1
        _AlphaSoft ("Alpha Clip Softness", Range (0,1)) = 0.022
        _EdgeDarken ("Edge Darkening", float) = 1.0
        _ProcMask ("Procedural Mask Strength", float) = 1.0

        [Header (Mask Controls)]
        _MainTex ("Mask Texture", 2D) = "white" {}
        _MaskStr ("Mask Strength", float) = 0.7
        _Columns ("Flipbook Columns", Int) = 1
        _Rows ("Flipbook Rows", Int) = 1
        _ChannelMask ("Channel Mask", Vector) = (1,0,0,0)
        [Toggle] _FlipU("Flip U Randomly", float) = 0
        [Toggle] _FlipV("Flip V Randomly", float) = 0

        [Header (Noise Controls)]
        _NoiseTex ("Noise Texture", 2D) = "white" {}
        _NoiseAlphaStr ("Noise Strength", float) = 0.8
        _NoiseColorStr ("Noise Color Influence", float) = 0.0
        _ChannelMask2 ("Channel Mask 2",Vector) = (1,0,0,0)
        _Randomize ("Randomize Noise", float) = 1.0

        [Header (UV Warp)]
        _WarpTex ("Warp Texture", 2D) = "gray" {}
        _WarpStr ("Warp Strength", float) = 1.0

        [Header (Vertex Physics)]
        _FallOffset ("Gravity Offset", range(-1,0)) = -1.0
        _FallRandomness ("Gravity Randomness", float) = 0.25
    }

    SubShader
    {
        Tags
        {
            "Queue"="Transparent"
            "RenderType"="Transparent"
            "RenderPipeline"="UniversalPipeline"
        }

        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Back

        Pass
        {
            Name "ForwardUnlit"

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            // (fog stripped out for now)
            // #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            // Textures & samplers
            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            TEXTURE2D(_NoiseTex);
            SAMPLER(sampler_NoiseTex);

            TEXTURE2D(_WarpTex);
            SAMPLER(sampler_WarpTex);

            // Per-material data
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float  _LightStr;
                float  _AlphaMin;
                float  _AlphaSoft;
                float  _EdgeDarken;
                float  _ProcMask;

                float  _MaskStr;
                float  _Columns;
                float  _Rows;
                float4 _ChannelMask;
                float  _FlipU;
                float  _FlipV;

                float4 _MainTex_ST;
                float4 _NoiseTex_ST;
                float4 _WarpTex_ST;

                float  _NoiseAlphaStr;
                float  _NoiseColorStr;
                float4 _ChannelMask2;
                float  _Randomize;

                float  _WarpStr;
                float  _FallOffset;
                float  _FallRandomness;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float4 texcoord0  : TEXCOORD0; // Z random, W lifetime
                float3 texcoord1  : TEXCOORD1; // X pan, Y warpStr, Z gravity
                float4 color      : COLOR;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float4 uv          : TEXCOORD0;    // xy: main UV, zw: noise/warp UV
                float4 color       : COLOR;
                float3 normalWS    : TEXCOORD1;
                float3 customData  : TEXCOORD2;    // x: pan, y: warpStr, z: random
            };

            Varyings vert (Attributes IN)
            {
                Varyings OUT;

                // Lifetime-based gravity
                float lifetime = IN.texcoord0.w;
                lifetime = lifetime * lifetime +
                           (_FallOffset + ((IN.texcoord0.z - 0.5) * _FallRandomness)) * lifetime;

                float4 fallPosWS = float4(0, IN.texcoord1.z, 0, 0) * lifetime;

                // Object → World → Clip
                float4 posWS = mul(GetObjectToWorldMatrix(), float4(IN.positionOS.xyz, 1.0)) + fallPosWS;
                OUT.positionHCS = TransformWorldToHClip(posWS.xyz);

                // Random UV flipping
                float2 UVflip = round(frac(float2(IN.texcoord0.z * 13, IN.texcoord0.z * 8)));
                UVflip = UVflip * 2 - 1;
                UVflip = lerp(1, UVflip, float2(_FlipU, _FlipV));

                // Base UV
                float2 baseUV = IN.texcoord0.xy * UVflip;
                OUT.uv.xy = baseUV * _MainTex_ST.xy + _MainTex_ST.zw;

                // Flipbook/randomized UVs
                OUT.uv.zw = OUT.uv.xy * float2(_Columns, _Rows) +
                            IN.texcoord0.z * float2(3, 8) * _Randomize;

                OUT.color = IN.color;
                OUT.color.a *= OUT.color.a;
                OUT.color.a += _AlphaMin;

                OUT.normalWS = TransformObjectToWorldNormal(IN.normalOS);

                OUT.customData = float3(IN.texcoord1.xy, IN.texcoord0.z); // pan, warpStr, random

                return OUT;
            }

            half4 frag (Varyings IN) : SV_Target
            {
                // UV warp sampling
                float4 uvWarp = SAMPLE_TEXTURE2D(
                    _WarpTex, sampler_WarpTex,
                    IN.uv.zw * _WarpTex_ST.xy +
                    _WarpTex_ST.zw * (IN.customData.x + 1) +
                    float2(5,8) * IN.customData.z
                );

                float2 warp = (uvWarp.xy * 2 - 1) * _WarpStr * IN.customData.y;

                // Mask
                half4 mask = SAMPLE_TEXTURE2D(
                    _MainTex, sampler_MainTex,
                    IN.uv.xy + warp
                );
                mask = saturate(lerp(1, mask, _MaskStr));

                // Edge mask
                half2 tempUV = frac(IN.uv.xy * half2(_Columns, _Rows)) - 0.5;
                tempUV *= tempUV * 4;
                half edgeMask = saturate(tempUV.x + tempUV.y);
                edgeMask *= edgeMask;
                edgeMask = 1 - edgeMask;
                edgeMask = lerp(1.0, edgeMask, _ProcMask);
                mask *= edgeMask;

                half4 col = max(0.001, IN.color);
                col.a = saturate(dot(mask, _ChannelMask));

                // Noise
                half4 noise4 = SAMPLE_TEXTURE2D(
                    _NoiseTex, sampler_NoiseTex,
                    IN.uv.zw * _NoiseTex_ST.xy +
                    _NoiseTex_ST.zw * IN.customData.x + warp
                );
                half noise = dot(noise4, _ChannelMask2);
                noise = saturate(lerp(1, noise, _NoiseAlphaStr));

                // Alpha clip & smooth edge
                col.a *= noise;
                half preClipAlpha = col.a;
                half clippedAlpha = saturate((preClipAlpha * IN.color.a - _AlphaMin) / _AlphaSoft);
                col.a = clippedAlpha;

                // Edge shaping
                half edge = 1 - saturate(preClipAlpha * clippedAlpha);
                edge *= edge;
                edge = 1 - edge;
                edge = edge + lerp(0, noise - 0.5, _NoiseColorStr);
                edge = saturate(lerp(0.71, edge * edge, _EdgeDarken));

                // Edge alpha
                col.a *= saturate(lerp(1.25, _BaseColor.a, edge));

                // Unlit color
                col.rgb *= _BaseColor.rgb;

                return col;
            }

            ENDHLSL
        }
    }

    FallBack Off
}

Shader "Custom/MagicBallShader"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _BaseColor ("Base Color", Color) = (0, 0.5, 1, 1)
        _EmissionColor ("Emission Color", Color) = (0, 1, 1, 1)
        _EmissionIntensity ("Emission Intensity", Range(0, 10)) = 2.5
        
        [Header(Fresnel Rim)]
        _RimPower ("Rim Power", Range(0.5, 8)) = 3
        _RimIntensity ("Rim Intensity", Range(0, 5)) = 2
        
        [Header(Noise Animation)]
        _NoiseScale ("Noise Scale", Range(1, 20)) = 5
        _NoiseSpeed ("Noise Speed", Range(0, 2)) = 0.5
        _NoiseOffset ("Noise Offset", Float) = 0
        _NoiseIntensity ("Noise Intensity", Range(0, 1)) = 0.3
        
        [Header(Secondary Noise)]
        _Noise2Scale ("Noise 2 Scale", Range(1, 20)) = 10
        _Noise2Speed ("Noise 2 Speed", Range(0, 2)) = 0.8
        
        [Header(Distortion)]
        _DistortionAmount ("Distortion Amount", Range(0, 0.5)) = 0.1
    }
    
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" }
        LOD 100
        
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float3 viewDirWS : TEXCOORD2;
                float3 positionWS : TEXCOORD3;
                float fogFactor : TEXCOORD4;
            };

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float4 _BaseColor;
                float4 _EmissionColor;
                float _EmissionIntensity;
                float _RimPower;
                float _RimIntensity;
                float _NoiseScale;
                float _NoiseSpeed;
                float _NoiseOffset;
                float _NoiseIntensity;
                float _Noise2Scale;
                float _Noise2Speed;
                float _DistortionAmount;
            CBUFFER_END

            // Simple noise function
            float random(float2 st)
            {
                return frac(sin(dot(st.xy, float2(12.9898, 78.233))) * 43758.5453123);
            }

            float noise(float2 st)
            {
                float2 i = floor(st);
                float2 f = frac(st);
                
                float a = random(i);
                float b = random(i + float2(1.0, 0.0));
                float c = random(i + float2(0.0, 1.0));
                float d = random(i + float2(1.0, 1.0));
                
                float2 u = f * f * (3.0 - 2.0 * f);
                
                return lerp(a, b, u.x) + (c - a) * u.y * (1.0 - u.x) + (d - b) * u.x * u.y;
            }

            float fbm(float2 st, int octaves)
            {
                float value = 0.0;
                float amplitude = 0.5;
                float frequency = 1.0;
                
                for (int i = 0; i < octaves; i++)
                {
                    value += amplitude * noise(st * frequency);
                    frequency *= 2.0;
                    amplitude *= 0.5;
                }
                
                return value;
            }

            Varyings vert(Attributes input)
            {
                Varyings output;
                
                VertexPositionInputs vertexInput = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs normalInput = GetVertexNormalInputs(input.normalOS);
                
                output.positionCS = vertexInput.positionCS;
                output.positionWS = vertexInput.positionWS;
                output.normalWS = normalInput.normalWS;
                output.viewDirWS = GetWorldSpaceViewDir(vertexInput.positionWS);
                output.uv = TRANSFORM_TEX(input.uv, _MainTex);
                output.fogFactor = ComputeFogFactor(vertexInput.positionCS.z);
                
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                // Normalize vectors
                float3 normalWS = normalize(input.normalWS);
                float3 viewDirWS = normalize(input.viewDirWS);
                
                // Fresnel for rim lighting
                float fresnel = pow(1.0 - saturate(dot(normalWS, viewDirWS)), _RimPower);
                
                // Animated noise layers
                float2 noiseUV1 = input.positionWS.xy * _NoiseScale + _NoiseOffset;
                float2 noiseUV2 = input.positionWS.xz * _Noise2Scale + _NoiseOffset * _Noise2Speed;
                
                float noise1 = fbm(noiseUV1, 3);
                float noise2 = fbm(noiseUV2, 2);
                
                // Combine noise layers
                float combinedNoise = noise1 * 0.6 + noise2 * 0.4;
                
                // Apply distortion
                float2 distortedUV = input.uv + combinedNoise * _DistortionAmount;
                
                // Sample texture with distortion
                half4 texColor = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, distortedUV);
                
                // Base color with noise modulation
                half4 baseColor = _BaseColor * (1.0 + combinedNoise * _NoiseIntensity);
                
                // Rim glow
                half3 rimGlow = _EmissionColor.rgb * fresnel * _RimIntensity;
                
                // Emission with pulsing noise
                half3 emission = _EmissionColor.rgb * _EmissionIntensity * (0.8 + combinedNoise * 0.4);
                
                // Combine all elements
                half3 finalColor = baseColor.rgb * texColor.rgb + emission + rimGlow;
                
                // Alpha based on fresnel and base color
                half alpha = saturate(_BaseColor.a + fresnel * 0.5);
                
                half4 color = half4(finalColor, alpha);
                
                // Apply fog
                color.rgb = MixFog(color.rgb, input.fogFactor);
                
                return color;
            }
            ENDHLSL
        }
    }
    
    FallBack "Universal Render Pipeline/Unlit"
}

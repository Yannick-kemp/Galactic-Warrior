Shader "Custom/2D/LaserVFX"
{
 Properties
    {
        [Header(Main Settings)]
        _MainTex ("Texture", 2D) = "white" {}
        _Color ("Laser Color", Color) = (1, 0, 0, 1)
        _CoreColor ("Core Color", Color) = (1, 1, 1, 1)
        _Intensity ("Intensity", Range(0, 10)) = 2
        
        [Header(Beam Settings)]
        _CoreWidth ("Core Width", Range(0, 1)) = 0.3
        _CoreIntensity ("Core Intensity", Range(0, 5)) = 3
        _GlowWidth ("Glow Width", Range(0, 5)) = 1
        _GlowFalloff ("Glow Falloff", Range(0.1, 5)) = 2
        
        [Header(Blur Effect)]
        _BlurSize ("Blur Size", Range(0, 5)) = 2
        _BlurIntensity ("Blur Intensity", Range(0, 2)) = 0.5
        _BlurFalloff ("Blur Falloff", Range(0.1, 10)) = 4
        
        [Header(Animation)]
        _ScrollSpeed ("Scroll Speed", Float) = 1
        
        [Header(Noise Settings)]
        _NoiseScale ("Noise Scale", Float) = 5
        _NoiseStrength ("Noise Strength", Range(0, 1)) = 0.1
        _NoiseSpeed ("Noise Animation Speed", Float) = 0.5
        
        [Header(Advanced Noise)]
        _Noise2Scale ("Secondary Noise Scale", Float) = 15
        _Noise2Strength ("Secondary Noise Strength", Range(0, 1)) = 0.05
        _EdgeNoiseScale ("Edge Distortion Scale", Float) = 10
        _EdgeNoiseStrength ("Edge Distortion Strength", Range(0, 0.5)) = 0.1
        _FlickerSpeed ("Flicker Speed", Float) = 3
        _FlickerStrength ("Flicker Strength", Range(0, 0.3)) = 0.05
        
        [Header(Rendering)]
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend ("Src Blend", Float) = 5 // SrcAlpha
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Dst Blend", Float) = 1 // One
        [Enum(Off, 0, On, 1)] _ZWrite ("Z Write", Float) = 0
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 0
    }
    
    SubShader
    {
        Tags 
        { 
            "RenderType"="Transparent" 
            "Queue"="Transparent"
            "RenderPipeline" = "UniversalPipeline"
        }
        
        Blend [_SrcBlend] [_DstBlend]
        ZWrite [_ZWrite]
        Cull [_Cull]
        
        Pass
        {
            Name "LaserBeam"
            
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            
            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
            };
            
            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
                float fogFactor : TEXCOORD1;
            };
            
            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            
            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float4 _Color;
                float4 _CoreColor;
                float _Intensity;
                float _CoreWidth;
                float _CoreIntensity;
                float _GlowWidth;
                float _GlowFalloff;
                float _BlurSize;
                float _BlurIntensity;
                float _BlurFalloff;
                float _ScrollSpeed;
                float _NoiseScale;
                float _NoiseStrength;
                float _NoiseSpeed;
                float _Noise2Scale;
                float _Noise2Strength;
                float _EdgeNoiseScale;
                float _EdgeNoiseStrength;
                float _FlickerSpeed;
                float _FlickerStrength;
            CBUFFER_END
            
            // Simple hash noise function
            float noise(float2 uv)
            {
                return frac(sin(dot(uv, float2(12.9898, 78.233))) * 43758.5453);
            }
            
            // Smooth interpolated noise
            float smoothNoise(float2 uv)
            {
                float2 i = floor(uv);
                float2 f = frac(uv);
                f = f * f * (3.0 - 2.0 * f);
                
                float a = noise(i);
                float b = noise(i + float2(1.0, 0.0));
                float c = noise(i + float2(0.0, 1.0));
                float d = noise(i + float2(1.0, 1.0));
                
                return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
            }
            
            // Fractal/layered noise (multiple octaves)
            float fractalNoise(float2 uv, int octaves)
            {
                float value = 0.0;
                float amplitude = 1.0;
                float frequency = 1.0;
                float maxValue = 0.0;
                
                for(int i = 0; i < octaves; i++)
                {
                    value += smoothNoise(uv * frequency) * amplitude;
                    maxValue += amplitude;
                    amplitude *= 0.5;
                    frequency *= 2.0;
                }
                
                return value / maxValue;
            }
            
            // Voronoi-like cellular noise
            float voronoiNoise(float2 uv)
            {
                float2 i = floor(uv);
                float2 f = frac(uv);
                
                float minDist = 1.0;
                
                for(int y = -1; y <= 1; y++)
                {
                    for(int x = -1; x <= 1; x++)
                    {
                        float2 neighbor = float2(x, y);
                        float2 cellPoint = float2(noise(i + neighbor), noise(i + neighbor + 0.5));
                        float2 diff = neighbor + cellPoint - f;
                        float dist = length(diff);
                        minDist = min(minDist, dist);
                    }
                }
                
                return minDist;
            }
            
            // Turbulence noise (absolute fractal)
            float turbulence(float2 uv, int octaves)
            {
                float value = 0.0;
                float amplitude = 1.0;
                float frequency = 1.0;
                
                for(int i = 0; i < octaves; i++)
                {
                    value += abs(smoothNoise(uv * frequency) * 2.0 - 1.0) * amplitude;
                    amplitude *= 0.5;
                    frequency *= 2.0;
                }
                
                return value;
            }
            
            Varyings vert(Attributes input)
            {
                Varyings output;
                
                VertexPositionInputs vertexInput = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionHCS = vertexInput.positionCS;
                output.uv = TRANSFORM_TEX(input.uv, _MainTex);
                output.color = input.color;
                output.fogFactor = ComputeFogFactor(vertexInput.positionCS.z);
                
                return output;
            }
            
            half4 frag(Varyings input) : SV_Target
            {
                // Scroll UVs
                float2 scrolledUV = input.uv;
                scrolledUV.x += _Time.y * _ScrollSpeed;
                
                // Sample main texture
                half4 texColor = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, scrolledUV);
                
                // === Advanced Noise System ===
                
                // Primary fractal noise (3 octaves for detail)
                float2 noiseUV1 = scrolledUV * _NoiseScale + _Time.y * _NoiseSpeed;
                float noise1 = fractalNoise(noiseUV1, 3);
                
                // Secondary high-frequency noise
                float2 noiseUV2 = scrolledUV * _Noise2Scale + _Time.y * _NoiseSpeed * 1.3;
                float noise2 = smoothNoise(noiseUV2);
                
                // Edge distortion noise (creates wavy edges)
                float2 edgeNoiseUV = float2(scrolledUV.x * _EdgeNoiseScale, scrolledUV.y * 3.0) + _Time.y * _NoiseSpeed * 0.7;
                float edgeNoise = turbulence(edgeNoiseUV, 2) * 2.0 - 1.0; // -1 to 1 range
                
                // Flickering intensity variation
                float flicker = sin(_Time.y * _FlickerSpeed + noise1 * 6.28) * 0.5 + 0.5;
                float flickerEffect = 1.0 - (flicker * _FlickerStrength);
                
                // Combine noise layers
                float combinedNoise = noise1 * _NoiseStrength + noise2 * _Noise2Strength;
                
                // Distance from center (V coordinate goes from 0 to 1, center is 0.5)
                float distFromCenter = abs(input.uv.y - 0.5) * 2.0;
                
                // Apply edge distortion (makes the beam wavy)
                distFromCenter += edgeNoise * _EdgeNoiseStrength;
                
                // Apply general noise variation
                distFromCenter += combinedNoise;
                
                // Clamp to prevent extreme values
                distFromCenter = saturate(distFromCenter);
                
                // Core beam calculation
                float core = 1.0 - saturate(distFromCenter / _CoreWidth);
                core = pow(core, 2.0);
                
                // Outer glow calculation
                float glow = 1.0 - saturate(distFromCenter / _GlowWidth);
                glow = pow(glow, _GlowFalloff);
                
                // Blur layer calculation (soft outer halo)
                float blur = 1.0 - saturate(distFromCenter / _BlurSize);
                blur = pow(blur, _BlurFalloff);
                
                // Combine all layers
                float beamMask = glow + (blur * _BlurIntensity);
                float3 beamColor = lerp(_Color.rgb, _CoreColor.rgb, core * _CoreIntensity);
                
                // Final color with flicker effect
                half3 finalColor = beamColor * beamMask * _Intensity * texColor.rgb * flickerEffect;
                
                // Add subtle noise-based intensity variation to core
                finalColor += _CoreColor.rgb * core * noise2 * 0.15;
                
                half alpha = beamMask * _Color.a;
                
                // Apply vertex color
                finalColor *= input.color.rgb;
                alpha *= input.color.a;
                
                // Apply fog
                finalColor = MixFog(finalColor, input.fogFactor);
                
                return half4(finalColor, alpha);
            }
            ENDHLSL
        }
    }
    
    // Fallback for Built-in Render Pipeline
    SubShader
    {
        Tags 
        { 
            "RenderType"="Transparent" 
            "Queue"="Transparent"
        }
        
        Blend [_SrcBlend] [_DstBlend]
        ZWrite [_ZWrite]
        Cull [_Cull]
        
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            
            #include "UnityCG.cginc"
            
            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
            };
            
            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
                UNITY_FOG_COORDS(1)
            };
            
            sampler2D _MainTex;
            float4 _MainTex_ST;
            float4 _Color;
            float4 _CoreColor;
            float _Intensity;
            float _CoreWidth;
            float _CoreIntensity;
            float _GlowWidth;
            float _GlowFalloff;
            float _BlurSize;
            float _BlurIntensity;
            float _BlurFalloff;
            float _ScrollSpeed;
            float _NoiseScale;
            float _NoiseStrength;
            float _NoiseSpeed;
            float _Noise2Scale;
            float _Noise2Strength;
            float _EdgeNoiseScale;
            float _EdgeNoiseStrength;
            float _FlickerSpeed;
            float _FlickerStrength;
            
            float noise(float2 uv)
            {
                return frac(sin(dot(uv, float2(12.9898, 78.233))) * 43758.5453);
            }
            
            float smoothNoise(float2 uv)
            {
                float2 i = floor(uv);
                float2 f = frac(uv);
                f = f * f * (3.0 - 2.0 * f);
                
                float a = noise(i);
                float b = noise(i + float2(1.0, 0.0));
                float c = noise(i + float2(0.0, 1.0));
                float d = noise(i + float2(1.0, 1.0));
                
                return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
            }
            
            float fractalNoise(float2 uv, int octaves)
            {
                float value = 0.0;
                float amplitude = 1.0;
                float frequency = 1.0;
                float maxValue = 0.0;
                
                for(int i = 0; i < octaves; i++)
                {
                    value += smoothNoise(uv * frequency) * amplitude;
                    maxValue += amplitude;
                    amplitude *= 0.5;
                    frequency *= 2.0;
                }
                
                return value / maxValue;
            }
            
            float voronoiNoise(float2 uv)
            {
                float2 i = floor(uv);
                float2 f = frac(uv);
                
                float minDist = 1.0;
                
                for(int y = -1; y <= 1; y++)
                {
                    for(int x = -1; x <= 1; x++)
                    {
                        float2 neighbor = float2(x, y);
                        float2 cellPoint = float2(noise(i + neighbor), noise(i + neighbor + 0.5));
                        float2 diff = neighbor + cellPoint - f;
                        float dist = length(diff);
                        minDist = min(minDist, dist);
                    }
                }
                
                return minDist;
            }
            
            float turbulence(float2 uv, int octaves)
            {
                float value = 0.0;
                float amplitude = 1.0;
                float frequency = 1.0;
                
                for(int i = 0; i < octaves; i++)
                {
                    value += abs(smoothNoise(uv * frequency) * 2.0 - 1.0) * amplitude;
                    amplitude *= 0.5;
                    frequency *= 2.0;
                }
                
                return value;
            }
            
            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.color = v.color;
                UNITY_TRANSFER_FOG(o, o.pos);
                return o;
            }
            
            fixed4 frag (v2f i) : SV_Target
            {
                // Scroll UVs
                float2 scrolledUV = i.uv;
                scrolledUV.x += _Time.y * _ScrollSpeed;
                
                // Sample texture
                fixed4 texColor = tex2D(_MainTex, scrolledUV);
                
                // === Advanced Noise System ===
                
                // Primary fractal noise (3 octaves for detail)
                float2 noiseUV1 = scrolledUV * _NoiseScale + _Time.y * _NoiseSpeed;
                float noise1 = fractalNoise(noiseUV1, 3);
                
                // Secondary high-frequency noise
                float2 noiseUV2 = scrolledUV * _Noise2Scale + _Time.y * _NoiseSpeed * 1.3;
                float noise2 = smoothNoise(noiseUV2);
                
                // Edge distortion noise (creates wavy edges)
                float2 edgeNoiseUV = float2(scrolledUV.x * _EdgeNoiseScale, scrolledUV.y * 3.0) + _Time.y * _NoiseSpeed * 0.7;
                float edgeNoise = turbulence(edgeNoiseUV, 2) * 2.0 - 1.0; // -1 to 1 range
                
                // Flickering intensity variation
                float flicker = sin(_Time.y * _FlickerSpeed + noise1 * 6.28) * 0.5 + 0.5;
                float flickerEffect = 1.0 - (flicker * _FlickerStrength);
                
                // Combine noise layers
                float combinedNoise = noise1 * _NoiseStrength + noise2 * _Noise2Strength;
                
                // Distance from center
                float distFromCenter = abs(i.uv.y - 0.5) * 2.0;
                
                // Apply edge distortion
                distFromCenter += edgeNoise * _EdgeNoiseStrength;
                
                // Apply general noise variation
                distFromCenter += combinedNoise;
                
                // Clamp to prevent extreme values
                distFromCenter = saturate(distFromCenter);
                
                // Core beam
                float core = 1.0 - saturate(distFromCenter / _CoreWidth);
                core = pow(core, 2.0);
                
                // Outer glow
                float glow = 1.0 - saturate(distFromCenter / _GlowWidth);
                glow = pow(glow, _GlowFalloff);
                
                // Blur layer
                float blur = 1.0 - saturate(distFromCenter / _BlurSize);
                blur = pow(blur, _BlurFalloff);
                
                // Combine all layers
                float beamMask = glow + (blur * _BlurIntensity);
                float3 beamColor = lerp(_Color.rgb, _CoreColor.rgb, core * _CoreIntensity);
                
                // Final color with flicker effect
                fixed3 finalColor = beamColor * beamMask * _Intensity * texColor.rgb * flickerEffect;
                
                // Add subtle noise-based intensity variation to core
                finalColor += _CoreColor.rgb * core * noise2 * 0.15;
                
                fixed alpha = beamMask * _Color.a;
                
                // Apply vertex color
                finalColor *= i.color.rgb;
                alpha *= i.color.a;
                
                // Apply fog
                UNITY_APPLY_FOG(i.fogCoord, finalColor);
                
                return fixed4(finalColor, alpha);
            }
            ENDCG
        }
    }
    
    Fallback "Sprites/Default"


}


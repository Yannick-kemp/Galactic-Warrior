Shader "Custom/EnergyWaveAttack"
{
    Properties
    {
        [MainTexture] _MainTex ("Sprite Texture", 2D) = "white" {}
        [MainColor] _BaseColor ("Base Color", Color) = (0.11, 0.83, 1, 1)
        _SecondaryColor ("Secondary Color", Color) = (0, 0.4, 0.6, 1)
        _GlowColor ("Glow Color", Color) = (0.49, 1, 1, 1)
        
        [Header(Wave Settings)]
        _WaveSpeed ("Wave Speed", Float) = 0.5
        _WaveFrequency ("Wave Frequency", Float) = 8.0
        _WaveAmplitude ("Wave Amplitude", Float) = 0.3
        _DistortionStrength ("Distortion Strength", Range(0, 1)) = 0.3
        
        [Header(Glow Settings)]
        _GlowIntensity ("Glow Intensity", Range(0, 20)) = 5.0
        _FresnelPower ("Fresnel Power", Range(0.5, 5)) = 2.0
        _EdgeSharpness ("Edge Sharpness", Range(1, 10)) = 3.0
        
        [Header(Animation)]
        _NoiseScale ("Noise Scale", Float) = 10.0
        _ScrollSpeed ("Scroll Speed", Vector) = (0.2, 0.5, 0, 0)
        _PulseSpeed ("Pulse Speed", Float) = 2.0
        
        [Header(Tendrils)]
        _TendrilCount ("Tendril Count", Range(2, 8)) = 4
        _TendrilWidth ("Tendril Width", Range(0.01, 0.2)) = 0.08
        _TendrilLength ("Tendril Length", Range(0.2, 1)) = 0.5
        
        [Header(Transparency)]
        _AlphaClip ("Alpha Clip", Range(0, 1)) = 0.1
        _FadeEdge ("Fade Edge", Range(0, 1)) = 0.3
    }
    
    SubShader
    {
        Tags 
        { 
            "RenderType"="Transparent" 
            "Queue"="Transparent"
            "RenderPipeline"="UniversalPipeline"
            "IgnoreProjector"="True"
        }
        
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            
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
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
                float3 positionWS : TEXCOORD1;
                float3 normalWS : TEXCOORD2;
                float fogFactor : TEXCOORD3;
            };
            
            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            
            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float4 _BaseColor;
                float4 _SecondaryColor;
                float4 _GlowColor;
                float _WaveSpeed;
                float _WaveFrequency;
                float _WaveAmplitude;
                float _DistortionStrength;
                float _GlowIntensity;
                float _FresnelPower;
                float _EdgeSharpness;
                float _NoiseScale;
                float4 _ScrollSpeed;
                float _PulseSpeed;
                float _TendrilCount;
                float _TendrilWidth;
                float _TendrilLength;
                float _AlphaClip;
                float _FadeEdge;
            CBUFFER_END
            
            // Noise functions
            float2 hash2(float2 p)
            {
                p = float2(dot(p, float2(127.1, 311.7)), dot(p, float2(269.5, 183.3)));
                return -1.0 + 2.0 * frac(sin(p) * 43758.5453123);
            }
            
            float noise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                float2 u = f * f * (3.0 - 2.0 * f);
                
                return lerp(lerp(dot(hash2(i + float2(0.0, 0.0)), f - float2(0.0, 0.0)),
                                 dot(hash2(i + float2(1.0, 0.0)), f - float2(1.0, 0.0)), u.x),
                            lerp(dot(hash2(i + float2(0.0, 1.0)), f - float2(0.0, 1.0)),
                                 dot(hash2(i + float2(1.0, 1.0)), f - float2(1.0, 1.0)), u.x), u.y);
            }
            
            float voronoiNoise(float2 uv, float time)
            {
                float2 p = floor(uv);
                float2 f = frac(uv);
                
                float res = 8.0;
                for(int j = -1; j <= 1; j++)
                {
                    for(int i = -1; i <= 1; i++)
                    {
                        float2 b = float2(i, j);
                        float2 r = b - f + hash2(p + b) * 0.5;
                        float d = dot(r, r);
                        res = min(res, d);
                    }
                }
                return sqrt(res);
            }
            
            // Rotate UV
            float2 rotate(float2 uv, float angle)
            {
                float s = sin(angle);
                float c = cos(angle);
                float2x2 rotMatrix = float2x2(c, -s, s, c);
                uv -= 0.5;
                uv = mul(rotMatrix, uv);
                uv += 0.5;
                return uv;
            }
            
            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                
                VertexPositionInputs positionInputs = GetVertexPositionInputs(IN.positionOS.xyz);
                OUT.positionCS = positionInputs.positionCS;
                OUT.positionWS = positionInputs.positionWS;
                OUT.uv = TRANSFORM_TEX(IN.uv, _MainTex);
                OUT.color = IN.color;
                OUT.normalWS = float3(0, 0, 1); // 2D sprite facing forward
                OUT.fogFactor = ComputeFogFactor(OUT.positionCS.z);
                
                return OUT;
            }
            
            float4 frag(Varyings IN) : SV_Target
            {
                float time = _Time.y;
                float2 uv = IN.uv;
                float2 centerUV = uv - 0.5;
                
                // === ANIMATED DISTORTION ===
                float2 scrollUV = uv + _ScrollSpeed.xy * time;
                float distortion = noise(scrollUV * _NoiseScale) * _DistortionStrength;
                float2 distortedUV = uv + distortion;
                
                // === VORONOI PATTERN ===
                float voronoi = voronoiNoise(distortedUV * 5.0, time * 0.3);
                voronoi = pow(voronoi, 2.0);
                
                // === WAVE PATTERN ===
                float wave = sin(distortedUV.x * _WaveFrequency + time * _WaveSpeed) * 
                            cos(distortedUV.y * _WaveFrequency * 0.7 + time * _WaveSpeed * 0.8);
                wave = wave * 0.5 + 0.5;
                wave = pow(wave, _EdgeSharpness);
                
                // === GRADIENT NOISE ===
                float gradientNoise = noise(distortedUV * 8.0 + time * 0.5);
                gradientNoise = gradientNoise * 0.5 + 0.5;
                
                // === ENERGY TENDRILS ===
                float tendrils = 0.0;
                float angleStep = 6.28318 / _TendrilCount; // 2*PI
                
                for(int i = 0; i < _TendrilCount; i++)
                {
                    float angle = angleStep * i + time * _PulseSpeed;
                    float2 rotatedUV = rotate(uv, angle);
                    float2 tendrilUV = rotatedUV - 0.5;
                    
                    // Elongated ellipse for tendril
                    float tendril = length(float2(tendrilUV.x / _TendrilWidth, 
                                                  tendrilUV.y / _TendrilLength));
                    tendril = 1.0 - smoothstep(0.8, 1.0, tendril);
                    
                    // Animate tendril along its length
                    float lengthFade = smoothstep(0.0, _TendrilLength, abs(tendrilUV.y));
                    tendril *= lengthFade;
                    
                    tendrils = max(tendrils, tendril);
                }
                
                // === FRESNEL GLOW ===
                float3 viewDir = normalize(_WorldSpaceCameraPos - IN.positionWS);
                float3 normalWS = normalize(IN.normalWS);
                float fresnel = 1.0 - saturate(dot(viewDir, normalWS));
                fresnel = pow(fresnel, _FresnelPower);
                
                // === COLOR GRADIENT ===
                float colorGradient = gradientNoise * wave;
                float3 color = lerp(_SecondaryColor.rgb, _BaseColor.rgb, colorGradient);
                color = lerp(color, _GlowColor.rgb, fresnel * 0.5);
                
                // === COMBINE EFFECTS ===
                float intensity = wave * voronoi;
                intensity = max(intensity, tendrils * 0.8);
                
                // Add pulsing effect
                float pulse = sin(time * _PulseSpeed) * 0.5 + 0.5;
                intensity *= (0.7 + pulse * 0.3);
                
                // Edge glow
                float edgeGlow = pow(fresnel, 2.0) * _GlowIntensity;
                color += _GlowColor.rgb * edgeGlow;
                
                // === ALPHA CALCULATION ===
                float alpha = intensity;
                
                // Fade from edges
                float edgeFade = smoothstep(_FadeEdge, 1.0 - _FadeEdge, uv.x) * 
                                smoothstep(_FadeEdge, 1.0 - _FadeEdge, uv.y);
                alpha *= edgeFade;
                
                // Distance fade from center
                float distFromCenter = length(centerUV);
                alpha *= smoothstep(0.7, 0.3, distFromCenter);
                
                // Noise-based dissipation
                float dissipation = noise(uv * 15.0 + time * 2.0) * 0.5 + 0.5;
                alpha *= smoothstep(_AlphaClip, 1.0, dissipation);
                
                // Add tendril alpha
                alpha = max(alpha, tendrils * 0.6);
                
                // === FINAL COLOR ===
                float3 finalColor = color * intensity * _GlowIntensity;
                finalColor = max(finalColor, tendrils * _GlowColor.rgb * _GlowIntensity * 0.5);
                
                // Apply vertex color
                finalColor *= IN.color.rgb;
                alpha *= IN.color.a;
                
                float4 output = float4(finalColor, alpha);
                
                // Apply fog
                output.rgb = MixFog(output.rgb, IN.fogFactor);
                
                return output;
            }
            ENDHLSL
        }
    }
    
    FallBack "Sprites/Default"
}

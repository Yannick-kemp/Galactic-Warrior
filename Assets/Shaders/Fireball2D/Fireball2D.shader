Shader "Custom/2D/Fireball"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _NoiseTex ("Noise Texture", 2D) = "white" {}
        
        [Header(Fireball Colors)]
        _CoreColor ("Core Color", Color) = (1, 1, 0.8, 1)
        _MidColor ("Mid Color", Color) = (1, 0.5, 0, 1)
        _OuterColor ("Outer Color", Color) = (1, 0.1, 0, 1)
        
        [Header(Animation)]
        _Speed ("Animation Speed", Range(0, 5)) = 1.5
        _Distortion ("Distortion Amount", Range(0, 1)) = 0.3
        _NoiseScale ("Noise Scale", Range(1, 10)) = 3.0
        
        [Header(Fireball Shape)]
        _Intensity ("Intensity", Range(0, 2)) = 1.0
        _Glow ("Glow", Range(0, 5)) = 2.0
        _Softness ("Edge Softness", Range(0, 1)) = 0.3
        
        [Header(Render Settings)]
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend ("Src Blend", Float) = 5
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Dst Blend", Float) = 1
    }
    
    SubShader
    {
        Tags 
        { 
            "Queue"="Transparent" 
            "RenderType"="Transparent"
            "IgnoreProjector"="True"
            "PreviewType"="Plane"
        }
        
        Blend [_SrcBlend] [_DstBlend]
        ZWrite Off
        Cull Off
        
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            
            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
            };
            
            struct v2f
            {
                float4 vertex : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
            };
            
            sampler2D _MainTex;
            sampler2D _NoiseTex;
            float4 _MainTex_ST;
            
            float4 _CoreColor;
            float4 _MidColor;
            float4 _OuterColor;
            
            float _Speed;
            float _Distortion;
            float _NoiseScale;
            float _Intensity;
            float _Glow;
            float _Softness;
            
            // Simple noise function
            float noise(float2 uv)
            {
                return tex2D(_NoiseTex, uv * _NoiseScale).r;
            }
            
            // Fractal Brownian Motion for more complex noise
            float fbm(float2 uv)
            {
                float value = 0.0;
                float amplitude = 0.5;
                float frequency = 1.0;
                
                for(int i = 0; i < 3; i++)
                {
                    value += amplitude * noise(uv * frequency);
                    frequency *= 2.0;
                    amplitude *= 0.5;
                }
                
                return value;
            }
            
            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.color = v.color;
                return o;
            }
            
            fixed4 frag (v2f i) : SV_Target
            {
                float2 uv = i.uv;
                float time = _Time.y * _Speed;
                
                // Create animated UV distortion
                float2 noiseUV1 = uv + float2(time * 0.3, time * 0.5);
                float2 noiseUV2 = uv + float2(-time * 0.2, time * 0.7);
                
                float noise1 = fbm(noiseUV1);
                float noise2 = fbm(noiseUV2);
                
                // Apply distortion
                float2 distortedUV = uv;
                distortedUV.x += (noise1 - 0.5) * _Distortion;
                distortedUV.y += (noise2 - 0.5) * _Distortion * 1.5;
                
                // Sample main texture with distortion
                float4 mainTex = tex2D(_MainTex, distortedUV);
                
                // Create fireball shape (circular gradient from center)
                float2 center = float2(0.5, 0.5);
                float dist = distance(distortedUV, center);
                
                // Add noise to the distance for organic edges
                float noiseMask = fbm(uv * 2.0 + time * 0.5);
                dist += (noiseMask - 0.5) * 0.2;
                
                // Create the fireball mask with soft edges
                float mask = 1.0 - smoothstep(0.0, 0.5 + _Softness, dist);
                mask = pow(mask, 1.5); // Make it more concentrated
                
                // Animate the intensity with pulsing
                float pulse = sin(time * 2.0) * 0.1 + 0.9;
                mask *= pulse;
                
                // Create color gradient based on distance from center
                float colorGradient = 1.0 - smoothstep(0.0, 0.4, dist);
                
                // Mix colors
                float4 fireColor = lerp(_OuterColor, _MidColor, colorGradient);
                fireColor = lerp(fireColor, _CoreColor, pow(colorGradient, 2.0));
                
                // Add glow effect
                float glow = pow(mask, _Glow) * _Intensity;
                
                // Combine everything
                float4 finalColor = fireColor * mainTex * i.color;
                finalColor.a = mask * mainTex.a * i.color.a * glow;
                
                return finalColor;
            }
            ENDCG
        }
    }
    
    FallBack "Sprites/Default"
}

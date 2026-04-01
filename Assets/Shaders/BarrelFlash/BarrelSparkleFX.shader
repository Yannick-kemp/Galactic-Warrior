Shader "Custom/2D/BarrelSparkleFX"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _SparkleColor ("Sparkle Color", Color) = (1, 1, 1, 1)
        _CoreColor ("Core Color", Color) = (1, 1, 0.8, 1)
        _Intensity ("Intensity", Range(0, 10)) = 5
        _SparkleCount ("Sparkle Count", Range(5, 50)) = 20
        _SparkleSize ("Sparkle Size", Range(0.01, 0.3)) = 0.08
        _FlashSize ("Flash Size", Range(0, 2)) = 1
        _RotationSpeed ("Rotation Speed", Range(0, 10)) = 3
        _AnimationSpeed ("Animation Speed", Range(0, 10)) = 5
        
        [Header(Blend Mode)]
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
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
            };
            
            sampler2D _MainTex;
            float4 _MainTex_ST;
            float4 _SparkleColor;
            float4 _CoreColor;
            float _Intensity;
            float _SparkleCount;
            float _SparkleSize;
            float _FlashSize;
            float _RotationSpeed;
            float _AnimationSpeed;
            
            // Random function
            float rand(float2 co)
            {
                return frac(sin(dot(co.xy, float2(12.9898, 78.233))) * 43758.5453);
            }
            
            // 2D rotation
            float2 rotate(float2 p, float angle)
            {
                float s = sin(angle);
                float c = cos(angle);
                return float2(p.x * c - p.y * s, p.x * s + p.y * c);
            }
            
            // Star shape function
            float star(float2 p, float size, float points)
            {
                float angle = atan2(p.y, p.x);
                float radius = length(p);
                
                // Create star points
                float a = 3.14159 / points;
                float sector = floor(angle / (2.0 * a));
                float localAngle = angle - sector * 2.0 * a;
                
                // Star radius variation
                float starRadius = size * (1.0 + 0.5 * cos(localAngle * points));
                
                float dist = radius / starRadius;
                return 1.0 - smoothstep(0.8, 1.2, dist);
            }
            
            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.color = v.color;
                return o;
            }
            
            fixed4 frag(v2f i) : SV_Target
            {
                float2 uv = i.uv * 2.0 - 1.0; // Center coordinates
                float time = _Time.y * _AnimationSpeed;
                
                // Rotate the entire effect
                uv = rotate(uv, _Time.y * _RotationSpeed);
                
                float dist = length(uv);
                
                // Central flash/glow
                float flash = exp(-dist * 3.0 / _FlashSize);
                flash *= (sin(time * 3.0) * 0.3 + 0.7); // Pulsing
                
                float4 finalColor = _CoreColor * flash * 2.0;
                
                // Generate multiple sparkles in a circular pattern
                float totalSparkle = 0.0;
                
                for(float j = 0; j < _SparkleCount; j++)
                {
                    // Create unique seed for each sparkle
                    float seed = j / _SparkleCount;
                    
                    // Random angle for sparkle position
                    float angle = seed * 6.28318 + time * rand(float2(seed, 0.1));
                    
                    // Random distance from center (with some variation)
                    float sparkleRadius = 0.3 + rand(float2(seed, 0.2)) * 0.6;
                    sparkleRadius *= (1.0 + sin(time * 2.0 + seed * 10.0) * 0.3); // Animate outward
                    
                    // Sparkle position
                    float2 sparklePos = float2(cos(angle), sin(angle)) * sparkleRadius;
                    
                    // Distance to sparkle
                    float2 toSparkle = uv - sparklePos;
                    float sparkleDist = length(toSparkle);
                    
                    // Create star-shaped sparkle
                    float sparkleShape = star(toSparkle, _SparkleSize, 4.0);
                    
                    // Animate sparkle brightness (twinkle)
                    float twinkle = sin(time * 3.0 + seed * 20.0) * 0.5 + 0.5;
                    twinkle = pow(twinkle, 2.0);
                    
                    // Fade sparkles based on distance from center
                    float fadeFactor = 1.0 - smoothstep(0.2, 1.0, sparkleRadius);
                    
                    sparkleShape *= twinkle * fadeFactor;
                    totalSparkle = max(totalSparkle, sparkleShape);
                }
                
                // Add sparkles to final color
                finalColor += _SparkleColor * totalSparkle * 3.0;
                
                // Add some additional glow around sparkles
                float glowRing = exp(-abs(dist - 0.5) * 8.0);
                glowRing *= sin(time * 2.0) * 0.3 + 0.7;
                finalColor += _CoreColor * glowRing * 0.5;
                
                // Apply intensity
                finalColor *= _Intensity;
                
                // Fade out from center
                float centerFade = 1.0 - smoothstep(0.0, 1.2, dist);
                finalColor.a = saturate((flash + totalSparkle + glowRing) * centerFade);
                
                // Apply vertex color
                finalColor *= i.color;
                
                return finalColor;
            }
            ENDCG
        }
    }
    
    FallBack "Sprites/Default"
}

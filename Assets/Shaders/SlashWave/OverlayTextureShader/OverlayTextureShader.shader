Shader "Custom/OverlayTexture"
{
    Properties
    {
        _MainTex    ("Island Sprite",    2D) = "white" {}   // your island
        _OverlayTex ("Overlay Texture",  2D) = "white" {}   // rock/ice pattern
        _BlendStrength ("Blend Strength", Range(0,1)) = 0.5 // mix amount
        _OverlayTiling ("Overlay Tiling", Float) = 1.0      // texture repeat
    }

    SubShader
    {
        Tags
        {
            "Queue"           = "Transparent"
            "RenderType"      = "Transparent"
            "IgnoreProjector" = "True"
        }

        Blend SrcAlpha OneMinusSrcAlpha   // standard transparency
        Cull Off
        ZWrite Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            // --- Declare shader properties ---
            sampler2D _MainTex;
            sampler2D _OverlayTex;
            float     _BlendStrength;
            float     _OverlayTiling;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv     : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv  : TEXCOORD0;
            };

            // --- Vertex shader ---
            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv  = v.uv;
                return o;
            }

            // --- Fragment shader ---
            fixed4 frag(v2f i) : SV_Target
            {
                // Sample the island sprite (with its alpha)
                fixed4 mainColor = tex2D(_MainTex, i.uv);

                // Sample the overlay using TILED UVs
                fixed4 overlayColor = tex2D(_OverlayTex, i.uv * _OverlayTiling);

                // Blend: mix island color with overlay color
                fixed3 blended = lerp(mainColor.rgb, overlayColor.rgb, _BlendStrength);

                // Keep the island's original alpha (preserves transparency)
                return fixed4(blended, mainColor.a);
            }
            ENDCG
        }
    }
}
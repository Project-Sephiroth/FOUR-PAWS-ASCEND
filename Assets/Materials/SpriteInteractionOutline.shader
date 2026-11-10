Shader "Custom/SpriteInteractionOutline"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _OutlineColor ("Outline Color", Color) = (1,0.85,0.12,1)
        _OutlineThickness ("Outline Width (texture pixels)", Range(1,4)) = 1
        [Toggle] _OutlineEnabled ("Outline Enabled", Float) = 0
    }
    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "RenderType" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "CanUseSpriteAtlas" = "True"
        }
        Pass
        {
            Tags { "LightMode" = "Universal2D" }
            Cull Off
            ZWrite Off
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            float4 _MainTex_TexelSize;

            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float4 _OutlineColor;
                float _OutlineThickness;
                float _OutlineEnabled;
            CBUFFER_END

            struct Attributes
            {
                float3 positionOS : POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            Varyings Vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS);
                o.uv = v.uv;
                o.color = v.color * _Color;
                return o;
            }

            half AlphaAt(float2 uv)
            {
                return SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv).a;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                half4 baseColor = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv);
                baseColor *= i.color;

                // Outline is drawn INSIDE the sprite's visible alpha silhouette.
                // This avoids clipping on tight SpriteRenderer meshes.
                if (_OutlineEnabled > 0.5 && baseColor.a > 0.001)
                {
                    float2 pixel = _MainTex_TexelSize.xy * _OutlineThickness;
                    half neighborMin = 1.0;
                    neighborMin = min(neighborMin, AlphaAt(i.uv + float2(pixel.x, 0)));
                    neighborMin = min(neighborMin, AlphaAt(i.uv - float2(pixel.x, 0)));
                    neighborMin = min(neighborMin, AlphaAt(i.uv + float2(0, pixel.y)));
                    neighborMin = min(neighborMin, AlphaAt(i.uv - float2(0, pixel.y)));
                    neighborMin = min(neighborMin, AlphaAt(i.uv + pixel));
                    neighborMin = min(neighborMin, AlphaAt(i.uv - pixel));
                    neighborMin = min(neighborMin, AlphaAt(i.uv + float2(pixel.x, -pixel.y)));
                    neighborMin = min(neighborMin, AlphaAt(i.uv + float2(-pixel.x, pixel.y)));

                    half edge = saturate(1.0 - neighborMin) * _OutlineColor.a;
                    baseColor.rgb = lerp(baseColor.rgb, _OutlineColor.rgb, edge);
                }
                return baseColor;
            }
            ENDHLSL
        }
    }
    FallBack Off
}

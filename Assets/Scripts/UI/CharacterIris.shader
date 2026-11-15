Shader "FourPaws/CharacterIris"
{
    Properties
    {
        [PerRendererData] _MainTex ("Texture", 2D) = "white" {}
        _MaskTex ("Character", 2D) = "white" {}
        _SpriteRect ("Sprite UV", Vector) = (0,0,1,1)
        _HoleSize ("Hole size", Vector) = (0,0,0,0)
        _BlackOpacity ("Opacity", Range(0,1)) = 1
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" }
        Cull Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "UnityCG.cginc"
            sampler2D _MaskTex;
            float4 _SpriteRect;
            float2 _HoleSize;
            float _BlackOpacity;

            struct Varyings
            {
                float4 position : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            Varyings Vert(appdata_base input)
            {
                Varyings output;
                output.position = UnityObjectToClipPos(input.vertex);
                output.uv = input.texcoord.xy;
                return output;
            }

            fixed4 Frag(Varyings input) : SV_Target
            {
                float2 hole = (input.uv - 0.5) / max(_HoleSize, 0.00001) + 0.5;
                float inside = step(0.0, hole.x) * step(hole.x, 1.0) *
                               step(0.0, hole.y) * step(hole.y, 1.0) * step(0.00001, _HoleSize.x);
                float alpha = tex2D(_MaskTex, _SpriteRect.xy + saturate(hole) * _SpriteRect.zw).a;
                return fixed4(0, 0, 0, (1.0 - alpha * inside) * _BlackOpacity);
            }
            ENDHLSL
        }
    }
}

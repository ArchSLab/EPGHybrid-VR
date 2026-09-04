Shader "Custom/DoubleSidedWhiteDiffuse"
{
    Properties
    {
        _Color("Main Color", Color) = (0.78, 0.82, 0.85, 1)
    }

        SubShader
    {
        Tags { "RenderType" = "Opaque" }
        LOD 200

        Cull Off

        CGPROGRAM
        #pragma surface surf Lambert addshadow fullforwardshadows

        fixed4 _Color;

        struct Input
        {
            float3 worldPos;
        };

        void surf(Input IN, inout SurfaceOutput o)
        {
            o.Albedo = _Color.rgb;
            o.Alpha = 1;
        }
        ENDCG
    }

        FallBack "Diffuse"
}
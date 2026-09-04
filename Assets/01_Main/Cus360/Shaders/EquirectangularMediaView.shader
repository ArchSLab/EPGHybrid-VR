Shader "Complete360Tour/EquirectangularMediaView" {
    Properties {
		_MainTex("MainTex", 2D) = "black" {}
        _Yaw ("Yaw", Float ) = 0

		[MaterialToggle] _YFlip("YFlip", Float) = 0
        [MaterialToggle] _Stereoscopic ("Stereoscopic", Float ) = 0
    }
    SubShader {
        Tags { "RenderType"="Opaque" }
        Pass {
            Name "FORWARD"
			Cull Back
			//ZWrite Off
			
            CGPROGRAM

            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct VertexInput {
                float4 vertex : POSITION;
            };

            struct VertexOutput {
                float4 pos : SV_POSITION;
                float4 posWorld : TEXCOORD0;
            };

			uniform sampler2D _MainTex; uniform float4 _MainTex_ST;

			uniform float _Yaw;
			uniform fixed _Stereoscopic;
			uniform fixed _YFlip;

            VertexOutput vert (VertexInput v) {
                VertexOutput o = (VertexOutput)0;
                o.posWorld = mul(unity_ObjectToWorld, v.vertex); 
				o.pos = UnityObjectToClipPos(v.vertex); 
                return o;
            }

			float2 CubemapUV(float3 dir, float yaw, float stereoscopic)
			{
				yaw += 0.5;
				float gC = (acos(dir.g) / 3.141592654);
				float2 N = float2((((atan2(dir.r, dir.b) / 6.28318530718) + 0.5) + yaw), frac(gC));
				N.y *= -1;
				float2 offset = stereoscopic ? float2(0.0, lerp(0.0, 0.5, unity_StereoEyeIndex)) : float2(0, 0);
				float2 M = frac((offset + lerp(N, (N*float2(1, 0.5)), stereoscopic)) );

				if (_YFlip == 1) M.y = 1 - M.y;

				return M;
			}

            float4 frag(VertexOutput i) : COLOR {
				return tex2D(_MainTex, CubemapUV( normalize(i.posWorld.rgb) , _Yaw, _Stereoscopic));
            }
            ENDCG
        }
    }
    FallBack "Diffuse"
}

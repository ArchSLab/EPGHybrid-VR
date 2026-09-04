// Upgrade NOTE: replaced 'mul(UNITY_MATRIX_MVP,*)' with 'UnityObjectToClipPos(*)'

Shader "Cus360Tour/EquirectangularMediaViewNew" {
    Properties {
		_MainTex("MainTex", 2D) = "black" {}
        _Yaw ("Yaw", Float ) = 0

		[MaterialToggle] _YFlip("YFlip", Float) = 0
        [MaterialToggle] _Stereoscopic ("Stereoscopic", Float ) = 0

		_tX("TranslateX", float) = 0
		_tY("TranslateY", float) = 0
		_tZ("TranslateZ", float) = 0
		_sX("ScaleX", float) = 1
		_sY("ScaleY", float) = 1
		_sZ("ScaleZ", float) = 1
		_rX("RotateX", float) = 0
		_rY("RotateY", float) = 0
		_rZ("RotateZ", float) = 0
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
			float4 _MainTex_ST;

			float _tX, _tY, _tZ;
			float _sX, _sY, _sZ;
			float _rX, _rY, _rZ;
			uniform sampler2D _MainTex; 
			//uniform float4 _MainTex_ST;

			uniform float _Yaw;
			uniform fixed _Stereoscopic;
			uniform fixed _YFlip;

			VertexOutput vert(VertexInput v)
			{
				VertexOutput o;

				float4x4 translateMatrix = float4x4(1, 0, 0, _tX,
					0, 1, 0, _tY,
					0, 0, 1, _tZ,
					0, 0, 0, 1);

				float4x4 scaleMatrix = float4x4(_sX, 0, 0, 0,
					0, _sY, 0, 0,
					0, 0, _sZ, 0,
					0, 0, 0, 1);

				float angleX = radians(_rX);
				float c = cos(angleX);
				float s = sin(angleX);
				float4x4 rotateXMatrix = float4x4(1, 0, 0, 0,
					0, c, -s, 0,
					0, s, c, 0,
					0, 0, 0, 1);

				float angleY = radians(_rY);
				c = cos(angleY);
				s = sin(angleY);
				float4x4 rotateYMatrix = float4x4(c, 0, s, 0,
					0, 1, 0, 0,
					-s, 0, c, 0,
					0, 0, 0, 1);

				float angleZ = radians(_rZ);
				c = cos(angleZ);
				s = sin(angleZ);
				float4x4 rotateZMatrix = float4x4(c, -s, 0, 0,
					s, c, 0, 0,
					0, 0, 1, 0,
					0, 0, 0, 1);


				float4 localVertexPos = v.vertex;

				// NOTE: the order matters, try scaling first before translating, different results
				float4 localTranslated = mul(translateMatrix, localVertexPos);
				//float4 localScaledTranslated = mul(scaleMatrix,localTranslated);
				//float4 localScaledTranslatedRotX = mul(rotateXMatrix,localScaledTranslated);
				//float4 localScaledTranslatedRotXY = mul(rotateYMatrix,localScaledTranslatedRotX);
				//float4 localScaledTranslatedRotXYZ = mul(rotateZMatrix,localScaledTranslatedRotXY);
				float4 localScaledTranslated = mul(localTranslated, scaleMatrix);
				float4 localScaledTranslatedRotX = mul(localScaledTranslated, rotateXMatrix);
				float4 localScaledTranslatedRotXY = mul(localScaledTranslatedRotX, rotateYMatrix);
				float4 localScaledTranslatedRotXYZ = mul(localScaledTranslatedRotXY, rotateZMatrix);
				o.posWorld = localScaledTranslatedRotXYZ;
				//o.posWorld = mul(unity_ObjectToWorld,localScaledTranslatedRotXYZ);
				o.pos = UnityObjectToClipPos(v.vertex);
				//o.uv = TRANSFORM_TEX(v.uv, _MainTex);
				return o;
			}
    //        VertexOutput vert (VertexInput v) {
    //            VertexOutput o = (VertexOutput)0;
    //            o.posWorld = mul(unity_ObjectToWorld, v.vertex); 
				//o.pos = UnityObjectToClipPos(v.vertex); 
    //            return o;
    //        }

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

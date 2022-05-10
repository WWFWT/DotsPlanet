Shader "MyShader/AtmoUseDepthLUT"
{
	Properties
	{
		_MainTex("Texture", 2D) = "white" {}
		_AtmoDepthLUT("AtmoDepthLUT",2D) = "white"{}
	}
	SubShader
	{
		Cull Off ZWrite Off ZTest Always
		Tags
		{
			"RenderType" = "Background"
			"RenderPipeline" = "UniversalPipeline"
		}

		HLSLINCLUDE
		#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
		#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
		static const float maxFloat = 3.402823466e+38;

		CBUFFER_START(UnityPerMaterial)

		float3 _PlanetCenter;
		float _PlanetRadius;
		int _SampleCount;
		float _PlanetRadiusOffset;

		float _SunStrength;
		float3 _SunLight;
		float3 _DirToSun;

		//������ƽ���ܶ����ڵĸ߶� H
		float _ScaleHeighR;
		float _ScaleHeighM;

		//��ƽ��ɢ��ϵ��
		float3 _RayleighSct;
		float3 _MieSct;

		//������뾶
		float _AtmoRadius;

		//��ʽɢ����λ����gֵ
		const float _MieG = 0.76f;

		float _RScatteringIntensity;
		float _MScatteringIntensity;

		TEXTURE2D(_MainTex);
		SAMPLER(sampler_MainTex);

		TEXTURE2D(_AtmoDepthLUT);
		SAMPLER(sampler_AtmoDepthLUT);

		CBUFFER_END

		

		ENDHLSL


		Pass
		{
			HLSLPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			#pragma enable_d3d11_debug_symbols

			struct appdata
			{
				float4 vertex : POSITION;
				float2 uv : TEXCOORD0;
			};

			struct v2f
			{
				float2 uv : TEXCOORD0;
				float4 vertex : SV_POSITION;
			};

			// Returns vector (dstToSphere, dstThroughSphere)
			// If ray origin is inside sphere, dstToSphere = 0
			// If ray misses sphere, dstToSphere = maxValue; dstThroughSphere = 0
			float2 RaySphere(float3 sphereCentre, float sphereRadius, float3 rayOrigin, float3 rayDir) {
				rayDir = normalize(rayDir);
				float3 offset = rayOrigin - sphereCentre;
				float a = 1; // Set to dot(rayDir, rayDir) if rayDir might not be normalized
				float b = 2 * dot(offset, rayDir);
				float c = dot(offset, offset) - sphereRadius * sphereRadius;
				float d = b * b - 4 * a * c; // Discriminant from quadratic formula

				// Number of intersections: 0 when d < 0; 1 when d = 0; 2 when d > 0
				if (d > 0) {
					float s = sqrt(d);
					float dstToSphereNear = max(0, (-b - s) / (2 * a));
					float dstToSphereFar = (-b + s) / (2 * a);

					// Ignore intersections that occur behind the ray
					if (dstToSphereFar >= 0) {
						return float2(dstToSphereNear, dstToSphereFar - dstToSphereNear);
					}
				}
				// Ray did not intersect sphere
				return float2(maxFloat, 0);
			}

			//������ܶȱ��� hԽ��Խ�ӽ�0
			float2 AtmoDensityRatio(float h)
			{
				return float2(exp(-(h / _ScaleHeighR)),exp(-(h / _ScaleHeighM)));
			}

			//��������ɢ����λ����
			float PhaseFunctionR(float cosAngle)
			{
				return (3.0 / (16.0 * PI)) * (1 + (cosAngle * cosAngle));
			}

			//��ʽɢ�����λ����
			float PhaseFunctionM(float cosAngle)
			{
				float g = _MieG;
				float g2 = g * g;
				return (1.0 / (4.0 * PI)) * ((3.0 * (1.0 - g2)) / (2.0 * (2.0 + g2))) * ((1 + cosAngle * cosAngle) / (pow((1 + g2 - 2 * g * cosAngle), 3.0 / 2.0)));
			}

			//����һ�����������չ����ɫ sceneDepthΪ���ص�������ľ���
			float3 CalculateLightColor(float3 eyeHitPos,float3 lookDir,float dstThroughAtmo,float inver)
			{
				float3 attenuation_R = 0;
				float3 attenuation_M = 0;
				float3 step = (dstThroughAtmo * normalize(lookDir)) / (_SampleCount - 1);
				float stepSize = length(step);

				//P��
				float3 pos = eyeHitPos;

				//��λ����
				float angle = dot(normalize(lookDir), normalize(_DirToSun));
				float scatterR = PhaseFunctionR(angle);
				float scatterM = PhaseFunctionM(angle);
				float maxHeight = _AtmoRadius - _PlanetRadius;

				[loop]
				for (int i = 0; i < _SampleCount; i++)
				{
					//̫���⿴��ƽ�й� ������㷽���ǹ�Դλ�þ��ǵ�P ����̫���ⷽ��
					//̫Զ�⴩��������ľ���
					float sunRayLength = RaySphere(_PlanetCenter,_AtmoRadius,pos,_DirToSun).y;
					
					if (sunRayLength > 640000) {
						pos += step;
						continue;
					}

					//C��
					float3 sunHitPoint = pos + normalize(_DirToSun) * sunRayLength;
					float3 TAP_R,TCP_R,TAP_M,TCP_M;
					float2 uv1, uv2;
					float pHeight = 0;
					float3 centerToEye = eyeHitPos - _PlanetCenter;
					float3 centerToP = pos - _PlanetCenter;

					float eyeHitHeight = length(centerToEye) - _PlanetRadius;
					pHeight = length(centerToP) - _PlanetRadius;

					//--------------------��tap
					if (inver >= 1 || inver <= -1) 
					{
						//����P��A��Ͷ���ʣ������ת�Ǿ���A��P
						float dotVal = dot(normalize(centerToEye), normalize(-lookDir * inver));
						float angleEyeHitPos = acos(dotVal) / PI;//A�����������ߵļн�

						dotVal = dot(normalize(centerToP), normalize(-lookDir * inver));
						float angleP_Eye = acos(dotVal) / PI;//P�����������߷�����

						float2 uv1, uv2;
						uv1.x = angleEyeHitPos;
						uv1.y = eyeHitHeight / maxHeight;

						uv2.x = angleP_Eye;
						uv2.y = pHeight / maxHeight;

						float2 tempDepth1 = _AtmoDepthLUT.Sample(sampler_AtmoDepthLUT, uv1).rg;
						float2 tempDepth2 = _AtmoDepthLUT.Sample(sampler_AtmoDepthLUT, uv2).rg;
						float2 depth = 0;

						if (inver == 1)
							depth = tempDepth2.xy - tempDepth1.xy;
						else
							depth = tempDepth1.xy - tempDepth2.xy;

						TAP_R = exp(-_RayleighSct * depth.x * _RScatteringIntensity);
						TAP_M = exp(-_MieSct * depth.y * _MScatteringIntensity);
					}
					else 
					{
						//����P��A��Ͷ���ʣ������ת�Ǿ���A��P
						float dotVal = dot(normalize(centerToEye), normalize(lookDir));
						float angleEyeHitPos = acos(dotVal) / PI;//A�����������ߵļн�

						dotVal = dot(normalize(centerToP), normalize(lookDir));
						float angleP_Eye = acos(dotVal) / PI;//P�����������߷�����

						
						uv1.x = angleEyeHitPos;
						uv1.y = eyeHitHeight / maxHeight;

						uv2.x = angleP_Eye;
						uv2.y = pHeight / maxHeight;

						float2 tempDepth1 = _AtmoDepthLUT.Sample(sampler_AtmoDepthLUT, uv1).rg;
						float2 tempDepth2 = _AtmoDepthLUT.Sample(sampler_AtmoDepthLUT, uv2).rg;
						float2 depth = tempDepth1.xy - tempDepth2.xy;

						float3 tempTAP_R1 = exp(-_RayleighSct * depth.x * _RScatteringIntensity);
						float3 tempTAP_M1 = exp(-_MieSct * depth.y * _MScatteringIntensity);

						dotVal = dot(normalize(centerToEye), normalize(-lookDir));
						angleEyeHitPos = acos(dotVal) / PI;//A�����������ߵļн�

						dotVal = dot(normalize(centerToP), normalize(-lookDir));
						angleP_Eye = acos(dotVal) / PI;//P�����������߷�����

						uv1.x = angleEyeHitPos;
						uv1.y = eyeHitHeight / maxHeight;

						uv2.x = angleP_Eye;
						uv2.y = pHeight / maxHeight;

						tempDepth1 = _AtmoDepthLUT.Sample(sampler_AtmoDepthLUT, uv1).rg;
						tempDepth2 = _AtmoDepthLUT.Sample(sampler_AtmoDepthLUT, uv2).rg;
						depth = tempDepth2.xy - tempDepth1.xy;

						float3 tempTAP_R2 = exp(-_RayleighSct * depth.x * _RScatteringIntensity);
						float3 tempTAP_M2 = exp(-_MieSct * depth.y * _MScatteringIntensity);

						TAP_R = lerp(tempTAP_R1, tempTAP_R2, inver);
						TAP_M = lerp(tempTAP_M1, tempTAP_M2, inver);
					}

					//----------------------��tcp
					float2 dotVal = dot(normalize(centerToP), normalize(_DirToSun));
					float angleP_Sun = acos(dotVal) / PI;//P��������̫��

					uv1.x = angleP_Sun;
					uv1.y = pHeight / maxHeight;

					float2 depth = _AtmoDepthLUT.Sample(sampler_AtmoDepthLUT, uv1).rg;
					TCP_R = exp(-_RayleighSct * depth.x * _RScatteringIntensity);
					TCP_M = exp(-_MieSct * depth.y * _MScatteringIntensity);
					//----------------------
					float2 pDepth = AtmoDensityRatio(pHeight);

					attenuation_R += TAP_R.rgb * TCP_R.rgb * pDepth.x * stepSize;
					attenuation_M += TAP_M.rgb * TCP_M.rgb * pDepth.y * stepSize;
					pos += step;
				}

				float3 result_R = _RayleighSct * scatterR * attenuation_R * _SunLight * _SunStrength;
				float3 result_M = _MieSct * scatterM * attenuation_M * _SunLight * _SunStrength;

				return result_R + result_M;
			}

			v2f vert(appdata v)
			{
				v2f o;
				o.vertex = TransformObjectToHClip(v.vertex.xyz);
				o.uv = v.uv;

				return o;
			}

			void GetRay(float2 uv, out float3 rayStart, out float3 rayDir, out float rayLen)
			{
				float sceneRawDepth = SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, sampler_CameraDepthTexture, uv);
				float3 worldPos = ComputeWorldSpacePosition(uv, sceneRawDepth, UNITY_MATRIX_I_VP);

				float3 camToPoint = worldPos - _WorldSpaceCameraPos;
				rayStart = _WorldSpaceCameraPos;
				rayDir = normalize(camToPoint);
				rayLen = length(camToPoint);
			}

			float4 frag(v2f i) : SV_Target
			{
				float4 originalCol = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex,i.uv);
				float sceneRawDepth = SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, sampler_CameraDepthTexture, i.uv);
				float3 worldPos = ComputeWorldSpacePosition(i.uv, sceneRawDepth, UNITY_MATRIX_I_VP);

				float3 camToPoint = worldPos - _WorldSpaceCameraPos;
				float3 rayStart = _WorldSpaceCameraPos;
				float3 rayDir = normalize(camToPoint);
				float rayLen = length(camToPoint);

				float2 hitToSeaInfo = RaySphere(_PlanetCenter, _PlanetRadius + _PlanetRadiusOffset, rayStart, rayDir);
				float sceneDepth = min(rayLen, hitToSeaInfo.x);

				float2 hitInfo = RaySphere(_PlanetCenter, _AtmoRadius, rayStart, rayDir);
				float dstToAtmo = hitInfo.x;
				float dstThroughAtmo = min(hitInfo.y, sceneDepth - dstToAtmo);

				if (dstThroughAtmo > 0)
				{
					//A��
					float3 eyeHitPos = rayStart + normalize(rayDir) * dstToAtmo;

					float inver = 0;
					float3 centerToEye = eyeHitPos - _PlanetCenter;
					float dotVal = dot(normalize(centerToEye), normalize(-rayDir));

					//1��������-1��ת����Χ�ڴ�����ֵ�Ĳ���
					if (dotVal >= 0.01) inver = 1;
					else if (dotVal <= -0.01) inver = -1;
					else {
						dotVal *= 100;
						inver = (dotVal + 1.0) / 2.0;
					}
					float3 atmoCol = CalculateLightColor(eyeHitPos, rayDir, dstThroughAtmo, inver);

					return float4(atmoCol,1) + originalCol;
				}
				else 
				{
					return originalCol;
				}

			}
			ENDHLSL
		}
	}
}
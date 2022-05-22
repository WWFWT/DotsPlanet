Shader "MyShader/AtmoUseDepthLUT"
{
	Properties
	{
		_MainTex("Texture", 2D) = "white" {}
		_AtmoDepthLUT("AtmoDepthLUT",2D) = "white"{}
		_DitherMask("_DitherMask",2D) = "white"{}
		_DitherHeight("DitherHeight",float) = 0
		_DitherWidth("DitherWidth",float) = 0
		_DitherDepth("DitherDepth",float) = 0
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
		//米式散射相位函数g值
		static const float _MieG = 0.76f;

		CBUFFER_START(UnityPerMaterial)

		float3 _PlanetCenter;
		float _PlanetRadius;
		int _SampleCount;
		float _PlanetRadiusOffset;

		float _SunStrength;
		float3 _SunLight;
		float3 _DirToSun;

		//大气的平均密度所在的高度 H
		float _ScaleHeighR;
		float _ScaleHeighM;

		//海平面散射系数
		float3 _RayleighSct;
		float3 _MieSct;

		//大气层半径
		float _AtmoRadius;

		float _RScatteringIntensity;
		float _MScatteringIntensity;

		float _DitherHeight;
		float _DitherWidth;
		float _DitherDepth;

		TEXTURE2D(_MainTex);
		SAMPLER(sampler_MainTex);

		TEXTURE2D(_AtmoDepthLUT);
		SAMPLER(sampler_AtmoDepthLUT);

		TEXTURE2D(_DitherMask);
		SAMPLER(sampler_DitherMask);

		CBUFFER_END

		

		ENDHLSL


		Pass
		{
			HLSLPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			//#pragma enable_d3d11_debug_symbols

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

			//求大气密度比例 h越大越接近0
			float2 AtmoDensityRatio(float h)
			{
				return float2(exp(-(h / _ScaleHeighR)),exp(-(h / _ScaleHeighM)));
			}

			//计算瑞利散射相位函数
			float PhaseFunctionR(float cosAngle)
			{
				return (3.0 / (16.0 * PI)) * (1 + (cosAngle * cosAngle));
			}

			//米式散射的相位函数
			float PhaseFunctionM(float costh,bool isSun)
			{
				float g = isSun ? 0.9381 : _MieG;
				float k = 1.55 * g - 0.55 * g * g * g;
				float kcosth = k * costh;
				return (1 - k * k) / ((4 * PI) * (1 - kcosth) * (1 - kcosth));
			}

			//计算一条视线上最终光的颜色 sceneDepth为像素到摄像机的距离
			float3 CalculateLightColor(float3 eyeHitPos,float3 lookDir,float dstThroughAtmo,float inver,float showSun, out float3 extinction)
			{
				float3 attenuation_R = 0;
				float3 attenuation_M = 0;
				float3 step = (dstThroughAtmo * normalize(lookDir)) / (_SampleCount - 1);
				float stepSize = length(step);
				float3 extinctionAP = 0, extinctionCP = 0;

				//P点
				float3 pos = eyeHitPos;

				//相位函数
				float angle = dot(normalize(lookDir), normalize(_DirToSun));
				float scatterR = PhaseFunctionR(angle);
				float scatterM = PhaseFunctionM(angle,false);
				float maxHeight = _AtmoRadius - _PlanetRadius;

				[loop]
				for (int i = 0; i < _SampleCount; i++)
				{
					//太阳光看作平行光 这里计算方法是光源位置就是点P 向着太阳光方向
					//太远光穿进大气层的距离
					float sunRayLength = RaySphere(_PlanetCenter,_AtmoRadius,pos,_DirToSun).y;
					
					if (sunRayLength > 640000) {
						pos += step;
						continue;
					}

					//C点
					float3 sunHitPoint = pos + normalize(_DirToSun) * sunRayLength;
					float3 TAP_R,TCP_R,TAP_M,TCP_M;
					float2 uv1, uv2;
					float pHeight = 0;
					float3 centerToEye = eyeHitPos - _PlanetCenter;
					float3 centerToP = pos - _PlanetCenter;

					float eyeHitHeight = length(centerToEye) - _PlanetRadius;
					pHeight = length(centerToP) - _PlanetRadius;

					//--------------------求tap
					if (inver >= 1 || inver <= -1) 
					{
						//是求P到A的投射率，如果反转那就是A到P
						float dotVal = dot(normalize(centerToEye), normalize(-lookDir * inver));
						float angleEyeHitPos = acos(dotVal) / PI;//A点向量与视线的夹角

						dotVal = dot(normalize(centerToP), normalize(-lookDir * inver));
						float angleP_Eye = acos(dotVal) / PI;//P点向量与视线反方向

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

						if (i == 0) {
							extinctionAP = TAP_R * TAP_M;
						}
					}
					else 
					{
						//是求P到A的投射率，如果反转那就是A到P
						float dotVal = dot(normalize(centerToEye), normalize(lookDir));
						float angleEyeHitPos = acos(dotVal) / PI;//A点向量与视线的夹角

						dotVal = dot(normalize(centerToP), normalize(lookDir));
						float angleP_Eye = acos(dotVal) / PI;//P点向量与视线反方向

						
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
						angleEyeHitPos = acos(dotVal) / PI;//A点向量与视线的夹角

						dotVal = dot(normalize(centerToP), normalize(-lookDir));
						angleP_Eye = acos(dotVal) / PI;//P点向量与视线反方向

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

						if (i == 0) {
							extinctionAP = TAP_R * TAP_M;
						}
					}

					//----------------------求tcp
					float2 dotVal = dot(normalize(centerToP), normalize(_DirToSun));
					float angleP_Sun = acos(dotVal) / PI;//P点向量与太阳

					uv1.x = angleP_Sun;
					uv1.y = pHeight / maxHeight;

					float2 depth = _AtmoDepthLUT.Sample(sampler_AtmoDepthLUT, uv1).rg;
					TCP_R = exp(-_RayleighSct * depth.x * _RScatteringIntensity);
					TCP_M = exp(-_MieSct * depth.y * _MScatteringIntensity);
					//----------------------
					float2 pDepth = AtmoDensityRatio(pHeight);

					if (i == 0) {
						extinctionCP = TCP_R * TCP_M;
					}

					attenuation_R += TAP_R.rgb * TCP_R.rgb * pDepth.x * stepSize;
					attenuation_M += TAP_M.rgb * TCP_M.rgb * pDepth.y * stepSize;
					pos += step;
				}

				float3 result_R = _RayleighSct * scatterR * attenuation_R;
				float3 result_M = _MieSct * scatterM * attenuation_M;
				extinction = extinctionAP * extinctionCP;

				if (showSun) {
					float sunScatter = PhaseFunctionM(angle, true);
					float3 result_Sun = _MieSct * sunScatter * attenuation_M;
					return (result_M + result_R + result_Sun) * _SunLight * _SunStrength;
				}

				return result_R * _SunLight * _SunStrength;
			}

			struct appdata
			{
				float4 vertex : POSITION;
				float2 uv : TEXCOORD0;
			};

			struct v2f
			{
				float2 uv : TEXCOORD0;
				float4 vertex : SV_POSITION;
				float ShadowCoords : TEXCOORD1;
			};

			v2f vert(appdata v)
			{
				v2f o;
				o.vertex = TransformObjectToHClip(v.vertex.xyz);
				o.uv = v.uv;
				o.ShadowCoords = ComputeScreenPos(o.vertex);
				return o;
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
				float dstThroughAtmo = sceneDepth - dstToAtmo;
				bool showSun = false;
				if (hitInfo.y < dstThroughAtmo)
				{
					dstThroughAtmo = hitInfo.y;
					showSun = true;
				}

				float mainLightShadow = 1;

				if (dstThroughAtmo > 0)
				{
					//A点
					float3 eyeHitPos = rayStart + normalize(rayDir) * dstToAtmo;

					float inver = 0;
					float3 centerToEye = eyeHitPos - _PlanetCenter;
					float dotVal = dot(normalize(centerToEye), normalize(-rayDir));

					//1是正常，-1反转，范围内代表插值的参数
					if (dotVal >= 0.01) inver = 1;
					else if (dotVal <= -0.01) inver = -1;
					else {
						dotVal *= 100;
						inver = (dotVal + 1.0) / 2.0;
					}
					float3 extinction = 0;
					float3 atmoCol = CalculateLightColor(eyeHitPos, rayDir, dstThroughAtmo, inver, showSun, extinction);
					if (!showSun) {
						originalCol *= float4(extinction, 1);
					}

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

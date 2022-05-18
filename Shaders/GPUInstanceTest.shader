Shader "Unlit/GPUInstanceTest"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
    }
    SubShader
    {
        Tags { 
            "RenderType"="Opaque"
            "RenderPipeline" = "UniversalPipeline"
        }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        struct appdata
        {
            #if UNITY_ANY_INSTANCING_ENABLED
            uint instanceID : INSTANCEID_SEMANTIC;
            #endif
            float4 vertex : POSITION;
            float2 uv : TEXCOORD0;
        };

        struct v2f
        {
            #if UNITY_ANY_INSTANCING_ENABLED
            uint instanceID : CUSTOM_INSTANCE_ID;
            #endif
            float2 uv : TEXCOORD0;
            float4 vertex : SV_POSITION;
        };

        float4 _MainTex_ST;
        TEXTURE2D(_MainTex);
        SAMPLER(sampler_MainTex);

        ENDHLSL
        
        Pass
        {
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile _ DOTS_INSTANCING_ON

            v2f vert (appdata v)
            {
                v2f o;
                #if UNITY_ANY_INSTANCING_ENABLED
                o.instanceID = v.instanceID;
                #endif
                
                o.vertex = TransformWorldToHClip(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                // sample the texture
                float4 col = SAMPLE_TEXTURE2D(_MainTex,sampler_MainTex,i.uv);

#if UNITY_ANY_INSTANCING_ENABLED
                return float4(1, 1, 1, 1);
#endif

                return col;
            }
            ENDHLSL
        }
    }
}

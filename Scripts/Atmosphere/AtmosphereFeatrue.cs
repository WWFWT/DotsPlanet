using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public class AtmosphereFeatrue : ScriptableRendererFeature
{
    class CustomRenderPass : ScriptableRenderPass
    {
        Material mt;
        AtmosphereVolume atmosphereVolume;
        RenderTargetIdentifier currentTarget;
        RenderTargetHandle m_temporaryColorTexture;
        static readonly string renderTag = "TestAtmospherePostProcess";
        static readonly int MainTexId = Shader.PropertyToID("_MainTex");
        GameObject light;

        public CustomRenderPass()
        {
            atmosphereVolume = VolumeManager.instance.stack.GetComponent<AtmosphereVolume>();
            mt = Resources.Load<Material>("AtmoUseDepthLUT");
            if(mt == null)
            {
                Debug.LogError("找不到材质");
                return;
            }

            this.renderPassEvent = RenderPassEvent.AfterRenderingTransparents;
            m_temporaryColorTexture.Init("temporaryColorTexture");
            light = GameObject.Find("Directional Light");
            if(light == null)
            {
                Debug.LogError("找不到光源");
                return;
            }
            SetMt();
        }


        void SetMt()
        {
            mt.SetInt("_SampleCount", atmosphereVolume.sampleCount.value);
            mt.SetFloat("_PlanetRadius", atmosphereVolume.offsetPlanetRadius);
            mt.SetVector("_PlanetCenter", Sphere.pos);
            mt.SetFloat("_PlanetRadiusOffset", AtmosphereVolume.offset);
            mt.SetFloat("_ScaleHeighR", atmosphereVolume.offsetScaleHeighR);
            mt.SetFloat("_ScaleHeighM", atmosphereVolume.offsetScaleHeighM);
            mt.SetVector("_RayleighSct", AtmosphereVolume.RayleighSct * 0.000001f);
            mt.SetVector("_MieSct", AtmosphereVolume.MieSct * 0.000001f);
            mt.SetVector("_DirToSun", Vector3.Normalize(-light.transform.forward));
            mt.SetVector("_SunLight", atmosphereVolume.sunLightColor.value);
            mt.SetFloat("_AtmoRadius", atmosphereVolume.atmoRadius.value);
            mt.SetFloat("_SunStrength", atmosphereVolume.sunStrength.value);
            mt.SetFloat("_RScatteringIntensity", atmosphereVolume.rScatteringIntensity.value);
            mt.SetFloat("_MScatteringIntensity", atmosphereVolume.mScatteringIntensity.value);

            /*
            if (atmosphereVolume.useCloud.value)
            {
                mt.SetFloat("_CloudUV_Scale", atmosphereVolume.cloudUV_Scale.value);
                mt.SetFloat("_WeatherTexScale", atmosphereVolume.weatherTexScale.value);
                mt.SetFloat("_CloudSpeed", atmosphereVolume.cloudSpeed.value);
                mt.SetFloat("_CloudMaxHeight", atmosphereVolume.cloudMaxHeight.value);
                mt.SetFloat("_CloudMinHeight", atmosphereVolume.cloudMinHeight.value);
                mt.SetFloat("_CloudDensity", atmosphereVolume.cloudDensity.value);
            }
            */
        }

        void UpdateMt()
        {
            mt.SetVector("_PlanetCenter", Sphere.pos);
            mt.SetVector("_DirToSun", Vector3.Normalize(light.transform.forward));
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            if(light == null)
            {
                light = GameObject.Find("Directional Light");
                if (light == null) return;
            }
            if (mt == null) return;
            if (!atmosphereVolume.IsActive()) return;
            SetMt();
            CommandBuffer cmd = CommandBufferPool.Get(renderTag);
            cmd.SetGlobalTexture(MainTexId, currentTarget);
            cmd.GetTemporaryRT(m_temporaryColorTexture.id, renderingData.cameraData.camera.scaledPixelWidth, renderingData.cameraData.camera.scaledPixelHeight, 0, FilterMode.Point, RenderTextureFormat.Default);
            cmd.Blit(currentTarget, m_temporaryColorTexture.Identifier());
            cmd.Blit(m_temporaryColorTexture.Identifier(), currentTarget, mt, 0);
            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }

        public void Setup(in RenderTargetIdentifier currentTarget)
        {
            this.currentTarget = currentTarget;
        }
    }

    CustomRenderPass m_ScriptablePass;

    /// <inheritdoc/>
    public override void Create()
    {
        m_ScriptablePass = new CustomRenderPass();
    }

    // Here you can inject one or multiple render passes in the renderer.
    // This method is called when setting up the renderer once per-camera.
    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        m_ScriptablePass.Setup(renderer.cameraColorTarget);
        renderer.EnqueuePass(m_ScriptablePass);
    }
}



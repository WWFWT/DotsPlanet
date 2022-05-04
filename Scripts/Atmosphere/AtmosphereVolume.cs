using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Rendering;

using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public class AtmosphereVolume : VolumeComponent, IPostProcessComponent
{
    public BoolParameter isEnable = new BoolParameter(false);
    public IntParameter sampleCount = new IntParameter(16);//采样次数
    public FloatParameter planetRadius = new FloatParameter(1000f);//星球半径
    public FloatParameter atmoRadius = new FloatParameter(1200f);//大气层比例
    public Vector3Parameter planetCenter = new Vector3Parameter(Vector3.zero);//星球中心点
    public Vector3Parameter sunDir = new Vector3Parameter(Vector3.forward);//太阳方向
    public ColorParameter sunLightColor = new ColorParameter(Color.white);//太阳光颜色
    public FloatParameter sunStrength = new FloatParameter(1);//太阳光强度
    public FloatParameter rScatteringIntensity = new FloatParameter(1);//瑞利散射强度
    public FloatParameter mScatteringIntensity = new FloatParameter(1);//米式散射强度
    public FloatParameter test = new FloatParameter(1);
    public static bool isActive = true;
    public static float offset = 2000f;//大气散射的地球半径要比正常的小2000，防止精度问题导致的亮边
    //瑞利散射密度平均高度
    public FloatParameter ScaleHeighR = new FloatParameter(80);
    //米式散射密度平均高度
    public FloatParameter ScaleHeighM = new FloatParameter(50);

    public float offsetPlanetRadius
    {
        get
        {
            return planetRadius.value - offset;
        }
    }
    public float offsetScaleHeighR
    {
        get
        {
            return ScaleHeighR.value + offset;
        }
    }
    public float offsetScaleHeighM
    {
        get
        {
            return ScaleHeighM.value;
        }
    }

    /*
    public BoolParameter useCloud = new BoolParameter(true);
    public FloatParameter cloudMaxHeight = new FloatParameter(20000);
    public FloatParameter cloudMinHeight = new FloatParameter(10000);
    public FloatParameter cloudDensity = new FloatParameter(0.01f);
    public FloatParameter cloudUV_Scale = new FloatParameter(1);
    public FloatParameter cloudSpeed = new FloatParameter(1);
    public FloatParameter weatherTexScale = new FloatParameter(1);
    */


    public static Vector3 RayleighSct = new Vector3(3.8f, 13.5f, 33.1f);//瑞利散射系数
    public static Vector3 MieSct = new Vector3(21.0f, 21.0f, 21.0f);//米式散射系数
    public const float EarthHeight = 80000.0f;
    public const float EarthRadius = 6371000.0f;
    public const float RHeight = 8500;
    public const float MHeight = 1200;

    public bool IsActive()
    {
        return isEnable.value && isActive;
    }

    public bool IsTileCompatible()
    {
        return false;
    }
}

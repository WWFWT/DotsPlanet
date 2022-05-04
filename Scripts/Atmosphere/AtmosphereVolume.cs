using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Rendering;

using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public class AtmosphereVolume : VolumeComponent, IPostProcessComponent
{
    public BoolParameter isEnable = new BoolParameter(false);
    public IntParameter sampleCount = new IntParameter(16);//��������
    public FloatParameter planetRadius = new FloatParameter(1000f);//����뾶
    public FloatParameter atmoRadius = new FloatParameter(1200f);//���������
    public Vector3Parameter planetCenter = new Vector3Parameter(Vector3.zero);//�������ĵ�
    public Vector3Parameter sunDir = new Vector3Parameter(Vector3.forward);//̫������
    public ColorParameter sunLightColor = new ColorParameter(Color.white);//̫������ɫ
    public FloatParameter sunStrength = new FloatParameter(1);//̫����ǿ��
    public FloatParameter rScatteringIntensity = new FloatParameter(1);//����ɢ��ǿ��
    public FloatParameter mScatteringIntensity = new FloatParameter(1);//��ʽɢ��ǿ��
    public FloatParameter test = new FloatParameter(1);
    public static bool isActive = true;
    public static float offset = 2000f;//����ɢ��ĵ���뾶Ҫ��������С2000����ֹ�������⵼�µ�����
    //����ɢ���ܶ�ƽ���߶�
    public FloatParameter ScaleHeighR = new FloatParameter(80);
    //��ʽɢ���ܶ�ƽ���߶�
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


    public static Vector3 RayleighSct = new Vector3(3.8f, 13.5f, 33.1f);//����ɢ��ϵ��
    public static Vector3 MieSct = new Vector3(21.0f, 21.0f, 21.0f);//��ʽɢ��ϵ��
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

using System.Collections;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Rendering;
using UnityEngine;

//TODO有大问题
struct GetRoot : IJobParallelFor
{
    [ReadOnly]
    public NativeArray<Entity> leafs;
    [ReadOnly]
    public int maxCount;
    public NativeArray<Vector3> root;

    public void Execute(int index)
    {
        Mesh mesh = GameMgr.entityMgr.GetSharedComponentData<RenderMesh>(leafs[index]).mesh;
        RenderBounds bounds = GameMgr.entityMgr.GetComponentData<RenderBounds>(leafs[index]);
        int seed = (int)(bounds.Value.Center.x * 1 + bounds.Value.Center.y * 2 + bounds.Value.Center.z * 3) / 3;
        System.Random rand = new System.Random(seed);

        int iterationCount = maxCount / leafs.Length;
        for(int i = 0; i < iterationCount; i++)
        {
            int randomNum = rand.Next(mesh.vertexCount);
            Vector3 vector3 = mesh.vertices[randomNum];
            Vector3 normal = mesh.normals[randomNum];
            float temp = Vector3.Dot(vector3, normal) * -6.25f + 5.38f;
            int rootIndex = index * iterationCount + i;
            root[rootIndex] = temp < 0.8f ? vector3 : Vector3.zero;
        }
    }
}

[BurstCompile]
[DisableAutoCreation]
public partial class TreeSystem : SystemBase
{
    const int maxTreeCount = 3000;
    public static GameObject[] treePrefabs;
    static NativeArray<Entity> prefabs;

    EntityManager entityMgr;

    protected override void OnCreate()
    {
        base.OnCreate();
        entityMgr = World.DefaultGameObjectInjectionWorld.EntityManager;
    }

    protected override void OnStartRunning()
    {
        base.OnStartRunning();
        var tempParam = new BlobAssetStore();
        NativeList<Entity> tempList = new NativeList<Entity>(Allocator.Temp);
        foreach(var prefab in treePrefabs)
        {
            var setting = GameObjectConversionSettings.FromWorld(World.DefaultGameObjectInjectionWorld, tempParam);
            Entity treePrefab = GameObjectConversionUtility.ConvertGameObjectHierarchy(prefab, setting);
            tempList.Add(treePrefab);
        }
        tempParam.Dispose();
        prefabs = new NativeArray<Entity>(tempList.ToArray(), Allocator.Persistent);
    }

    protected override void OnUpdate()
    {
        Entities
        .WithAny<Tile>()
        .ForEach((in DynamicBuffer<Vertices> vertices) =>
        {

        }).Schedule();
    }

    protected override void OnDestroy()
    {
        base.OnDestroy();
    }
}

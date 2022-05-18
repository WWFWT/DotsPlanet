using System.Collections;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Rendering;
using UnityEngine;
using Unity.Transforms;
using UnityEngine.Rendering;

[BurstCompile]
[DisableAutoCreation]
[AlwaysUpdateSystem]
public partial class TreeSystem : SystemBase
{
    const int maxTreeCount = 3000;
    EntityCommandBufferSystem CommandBufferSystem;
    public static List<GameObject> treePrefabs = new List<GameObject>();
    static NativeArray<Entity> prefabs;
    const float updateTime = 0.5f;
    float timer = 0;
    BatchRendererGroup batchRendererGroup;

    EntityManager entityMgr;

    protected override void OnCreate()
    {
        base.OnCreate();
        entityMgr = World.DefaultGameObjectInjectionWorld.EntityManager;
        CommandBufferSystem = World.DefaultGameObjectInjectionWorld.GetExistingSystem<EndSimulationEntityCommandBufferSystem>();
    }

    protected override void OnStartRunning()
    {
        base.OnStartRunning();
        if (!prefabs.IsCreated)
        {
            batchRendererGroup = new BatchRendererGroup(OnCulling);

            var tempParam = new BlobAssetStore();
            NativeList<Entity> tempList = new NativeList<Entity>(Allocator.Temp);
            int index = 0;
            foreach (var prefab in treePrefabs)
            {
                var setting = GameObjectConversionSettings.FromWorld(World.DefaultGameObjectInjectionWorld, tempParam);
                Entity treePrefab = GameObjectConversionUtility.ConvertGameObjectHierarchy(prefab, setting);
                entityMgr.SetName(treePrefab, "Tree_" + index);
                tempList.Add(treePrefab);
            }
            tempParam.Dispose();
            prefabs = new NativeArray<Entity>(tempList.ToArray(), Allocator.Persistent);
            tempList.Dispose();
        }
    }

    protected override void OnUpdate()
    {

        timer += Time.DeltaTime;
        if (timer < updateTime) return;
        timer = 0;

        EntityCommandBuffer.ParallelWriter commandBuffer = CommandBufferSystem.CreateCommandBuffer().AsParallelWriter();
        Entity tempTree = prefabs[0];
        Entity parentEntity = Sphere.sphere;
        Vector3 center = Sphere.pos;

        Entities
        .WithAny<Tile>()
        .ForEach((Entity entity, ref DynamicBuffer<Vertices> vertices,in LocalToWorld localToWorld, in int entityInQueryIndex) =>
        {
            for (int i = 0; i < vertices.Length; i++)
            {
                if (vertices[i] == Vector3.zero) continue;
                Entity newEntity = commandBuffer.Instantiate(entityInQueryIndex, tempTree);
                commandBuffer.AddComponent<LocalToParent>(entityInQueryIndex, newEntity);

                Parent parent = new Parent();
                parent.Value = parentEntity;
                commandBuffer.AddComponent<Parent>(entityInQueryIndex, newEntity, parent);
                commandBuffer.SetComponent<LocalToWorld>(entityInQueryIndex, newEntity, localToWorld);

                Translation translation = new Translation();
                translation.Value = vertices[i].vertex;
                vertices[i] = Vector3.zero;
                commandBuffer.SetComponent<Translation>(entityInQueryIndex, newEntity, translation);

                Rotation rotation = new Rotation();
                Vector3 targetDir = (center - vertices[i].vertex).normalized;
                rotation.Value = Quaternion.FromToRotation(localToWorld.Up, -targetDir);
                commandBuffer.SetComponent<Rotation>(entityInQueryIndex, newEntity, rotation);
            }
            commandBuffer.RemoveComponent<Vertices>(entityInQueryIndex, entity);
        })
        .ScheduleParallel();

        CommandBufferSystem.AddJobHandleForProducer(this.Dependency);
    }

    protected override void OnDestroy()
    {
        if(prefabs.IsCreated) prefabs.Dispose();
        base.OnDestroy();
    }

    JobHandle OnCulling(BatchRendererGroup batchRendererGroup,BatchCullingContext batchCullingContext)
    {
        return default;
    }
}

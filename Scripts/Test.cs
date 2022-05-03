using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Rendering;
using Unity.Transforms;
using UnityEngine;

public class Test : MonoBehaviour
{
    public Mesh mesh;
    public Material material;

    // Start is called before the first frame update
    void Start()
    {
        EntityManager entityMgr = World.DefaultGameObjectInjectionWorld.EntityManager;
        EntityQuery entityQuery = entityMgr.CreateEntityQuery(new EntityQueryDesc { Any = new ComponentType[] { ComponentType.ReadWrite<RenderMesh>() } });
        NativeArray<Entity> entities = entityQuery.ToEntityArray(Allocator.Persistent);

        Entity tilePrefab = Entity.Null;
        for(int i = 0; i < entities.Length; i++)
        {
            if(entityMgr.GetName(entities[i]) == "TilePrefab")
            {
                tilePrefab = entities[i];
                break;
            }
        }
        if(tilePrefab == Entity.Null)
        {
            Debug.LogError("ÕÒ²»µ½TilePrefab");
            entities.Dispose();
            return;
        }
        Entity copy = entityMgr.Instantiate(tilePrefab);
        Translation translation = entityMgr.GetComponentData<Translation>(copy);
        RenderMesh renderMesh = entityMgr.GetSharedComponentData<RenderMesh>(copy);
        translation.Value.x += 10;
        entityMgr.SetComponentData(copy, translation);
        entityMgr.AddComponent<Tile>(copy);
        renderMesh.mesh = mesh;
        entityMgr.SetSharedComponentData<RenderMesh>(copy,renderMesh);

        entities.Dispose();
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}

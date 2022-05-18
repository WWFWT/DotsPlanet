using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Unity.Entities;
using UnityEngine;

public class GameMgr : MonoBehaviour
{
    public Transform player;
    public Material tileMt;
    public Material seaMt;
    public ComputeShader computeShader;
    public GameObject tilePrefab;
    public GameObject treePrefab;
    public GameObject sun;
    public int test;
    public Mesh testMesh;

    public static EntityManager entityMgr;

    Sphere sphere;

    [DllImport("MeshOptimize")]
    public static extern int TestAdd(int a, int b);
    [DllImport("MeshOptimize")]
    public static extern void OptimizeMesh(Vector3[] vertexBuf,int[] triangleBuf, int vertexCount, int triangleCount);

    // Start is called before the first frame update
    void Start()
    {
        sphere = new Sphere();
        Sphere.radius = 6371*1000;
        Sphere.tileMt = tileMt;
        Sphere.seaMt = seaMt;
        Sphere.computeVertexShader = computeShader;
        Sphere.player = player;
        player.position = new Vector3(0, 0, -Sphere.radius * 1.5f);

        TileSystem.tileGameObjectPrefab = tilePrefab;
        TreeSystem.treePrefabs.Add(treePrefab);

        entityMgr = World.DefaultGameObjectInjectionWorld.EntityManager;
        ComponentSystemGroup componentSystemGroup = World.DefaultGameObjectInjectionWorld.GetExistingSystem<ComponentSystemGroup>();
        
        TileSystem tileSystem = World.DefaultGameObjectInjectionWorld.GetOrCreateSystem<TileSystem>();
        componentSystemGroup.AddSystemToUpdateList(tileSystem);

        //TreeSystem treeSystem = World.DefaultGameObjectInjectionWorld.GetOrCreateSystem<TreeSystem>();
        //componentSystemGroup.AddSystemToUpdateList(treeSystem);

        //SimulationSystemGroup simulationSystemGroup = World.DefaultGameObjectInjectionWorld.GetExistingSystem<SimulationSystemGroup>();
        //simulationSystemGroup.AddSystemToUpdateList(tileSystem);

        //System.Random rand = new System.Random(test);
        //for(int i = 0; i < 100; i++)
        //{
        //    Debug.Log(rand.Next(0, 100));
        //}
    }

    // Update is called once per frame
    void Update()
    {
        sphere.Update();
        Sphere.tileMt.SetVector("SunLightDir", sun.transform.forward);
    }
}

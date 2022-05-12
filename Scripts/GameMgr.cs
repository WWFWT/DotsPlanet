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
    public GameObject sun;

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

        TileSystem tileSystem = World.DefaultGameObjectInjectionWorld.GetOrCreateSystem<TileSystem>();
        ComponentSystemGroup componentSystemGroup = World.DefaultGameObjectInjectionWorld.GetExistingSystem<ComponentSystemGroup>();
        componentSystemGroup.AddSystemToUpdateList(tileSystem);

        Debug.Log("测试DLL：" + TestAdd(1, 99));

        StartCoroutine("UpdateMesh");
        //SimulationSystemGroup simulationSystemGroup = World.DefaultGameObjectInjectionWorld.GetExistingSystem<SimulationSystemGroup>();
        //simulationSystemGroup.AddSystemToUpdateList(tileSystem);
    }

    // Update is called once per frame
    void Update()
    {
        sphere.Update();
        Sphere.tileMt.SetVector("SunLightDir", sun.transform.forward);
    }

    IEnumerator UpdateMesh()
    {
        //调用外部C++库来进行网格优化
        while (true)
        {
            for (int i = 0; i < TileSystem.updateMesh.Count; i++)
            {
                int[] temp = TileSystem.updateMesh[i].triangles.Clone() as int[];
                Vector3[] tempV = TileSystem.updateMesh[i].vertices.Clone() as Vector3[];
                OptimizeMesh(TileSystem.updateMesh[i].vertices, temp, TileSystem.updateMesh[i].vertices.Length, TileSystem.updateMesh[i].triangles.Length);
                TileSystem.updateMesh[i].vertices = tempV;
                TileSystem.updateMesh[i].triangles = temp;
                //TileSystem.updateMesh[i].Optimize();
                yield return null;
            }
            TileSystem.updateMesh.Clear();
            yield return null;
        }
    }
}

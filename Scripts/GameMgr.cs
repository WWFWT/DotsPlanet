using System.Collections;
using System.Collections.Generic;
using Unity.Entities;
using UnityEngine;

public class GameMgr : MonoBehaviour
{
    public Transform player;
    public Material material;
    public Material seaMt;
    public ComputeShader computeShader;
    public GameObject tilePrefab;

    Sphere sphere;

    // Start is called before the first frame update
    void Start()
    {
        sphere = new Sphere();
        Sphere.radius = 6371*1000;
        Sphere.tileMt = material;
        Sphere.seaMt = seaMt;
        Sphere.computeVertexShader = computeShader;
        Sphere.player = player;
        player.position = new Vector3(0, 0, -Sphere.radius * 1.5f);
        TileSystem.tileGameObjectPrefab = tilePrefab;

        TileSystem tileSystem = World.DefaultGameObjectInjectionWorld.GetOrCreateSystem<TileSystem>();
        ComponentSystemGroup componentSystemGroup = World.DefaultGameObjectInjectionWorld.GetExistingSystem<ComponentSystemGroup>();
        componentSystemGroup.AddSystemToUpdateList(tileSystem);
        
        //SimulationSystemGroup simulationSystemGroup = World.DefaultGameObjectInjectionWorld.GetExistingSystem<SimulationSystemGroup>();
        //simulationSystemGroup.AddSystemToUpdateList(tileSystem);
    }

    // Update is called once per frame
    void Update()
    {
        sphere.Update();
    }
}

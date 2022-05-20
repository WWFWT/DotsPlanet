using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Rendering;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.Rendering;

struct AddJob : IJobParallelFor
{
    public NativeArray<Matrix4x4> matrixes;
    public void Execute(int index)
    {
        System.Random random = new System.Random(index);
        Matrix4x4 offset = Matrix4x4.Translate(new Vector3(random.Next(-50,50), 0, random.Next(-50, 50)));
        matrixes[index] = offset;
    }
}

public class Test : MonoBehaviour
{
    public Mesh mesh;
    public Material material;
    public Vector3 pos;

    NativeArray<Matrix4x4> mt;
    BatchRendererGroup batchRendererGroup;

    // Start is called before the first frame update
    void Start()
    {
        batchRendererGroup = new BatchRendererGroup(OnCulling);

        for (int i = 0; i < 50; i++)
        {
            int index = batchRendererGroup.AddBatch(mesh, 0, material, 0, ShadowCastingMode.On, true, false, new Bounds(Vector3.zero, Vector3.one), 1000, null, null);
            mt = batchRendererGroup.GetBatchMatrices(index);

            AddJob addJob = new AddJob
            {
                matrixes = mt
            };
            addJob.Run(mt.Length);
        }
    }

    private void OnDisable()
    {
        batchRendererGroup.Dispose();
    }

    // Update is called once per frame
    void Update()
    {
        //batchRendererGroup.AddBatch(mesh, 0, material, 1, ShadowCastingMode.On, true, false, mesh.bounds, 10, null, null);
        //Graphics.DrawMesh(mesh, transform.position, Quaternion.identity, material, 0);
        //Graphics.DrawMesh(mesh, pos, Quaternion.identity, material, 1);
        //Graphics.DrawMeshInstanced(mesh, 0, material, mt.ToArray(), 10);
    }

    JobHandle OnCulling(BatchRendererGroup batchRendererGroup, BatchCullingContext batchCullingContext)
    {
        return default;
    }
}

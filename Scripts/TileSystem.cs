using UnityEngine;
using Unity.Entities;
using Unity.Rendering;
using Unity.Transforms;
using Unity.Burst;
using Unity.Collections;
using System.Collections.Generic;
using Unity.Mathematics;

public enum TrianglePos
{
    top, middle, left, right
}

public struct TriangleVertexs
{
    public Vector3 top;
    public Vector3 left;
    public Vector3 right;
}

enum TileOperation
{
    non,split,merge
}

struct Tile: IComponentData
{
    //�Ĳ����ṹ
    public Entity parent;
    public Entity childTop;
    public Entity childMid;
    public Entity childLeft;
    public Entity childRight;

    //��Ҫ���еĲ���
    public TileOperation op;
    //LODˮƽ
    public int level;
    //�����ε�����λ�õĶ���
    public TriangleVertexs triangleVertexs;
    //�Ƿ��Ǻ���
    public bool isSea;
    //�Ƿ�Ҷ�ӽڵ�
    public bool isLeaf;

    public override string ToString()
    {
        string ret = "";
        ret += "Level:" + level + "   ";
        return ret;
    }
}

[BurstCompile]
[DisableAutoCreation]
public partial class TileSystem : SystemBase
{
    const int resolution = 286;
    static int oneMeshVerticesCount = 0;
    const float levelDist = 120; //������һ����������ף�֮ÿһ���Դ�Ϊ��׼����
    static int maxLevel = 0;
    const float accuracy = 10f; //��󾫶�
    const float updateTime = 0.05f;
    float timer = 0f;
    public static GameObject tileGameObjectPrefab;

    Entity tilePrefab = Entity.Null;
    EntityManager entityMgr;

    ComputeBuffer verticesBuffer;
    ComputeBuffer directionBuffer;
    ComputeBuffer triangleMapBuffer;
    int kernelHandle;
    NativeArray<Vector3> vectors; //ϸ��������ʱ��洢�����εĸ�������
    NativeArray<Vector3> vertices; //ϸ��������ʱ��洢�����εĶ���
    Vector3[] calVexRet;

    //����ϸ�ִ���һ�����������ظ�����
    private static int[] triangleIndexs;
    public readonly static List<int> triangleMap = new List<int>();

    protected override void OnCreate()
    {
        base.OnCreate();
        entityMgr = World.DefaultGameObjectInjectionWorld.EntityManager;

        vectors = new NativeArray<Vector3>(3, Allocator.Persistent);
        vertices = new NativeArray<Vector3>(oneMeshVerticesCount, Allocator.Persistent);

        InitTrangleIndex();
        AllocateMem();
        InitComputeShader();
    }

    protected override void OnStartRunning()
    {
        base.OnStartRunning();
        var setting = GameObjectConversionSettings.FromWorld(World.DefaultGameObjectInjectionWorld, null);
        tilePrefab = GameObjectConversionUtility.ConvertGameObjectHierarchy(tileGameObjectPrefab, setting);
        if (tilePrefab == Entity.Null)
        {
            Debug.LogError("�Ҳ���TilePrefab  prefab:"+ tileGameObjectPrefab + "   setting:" + setting);
            return;
        }

        EntityQuery entityQuery = entityMgr.CreateEntityQuery(new EntityQueryDesc { All = new ComponentType[] { ComponentType.ReadWrite<Tile>() } });
        NativeArray<Entity> entities = entityQuery.ToEntityArray(Allocator.Temp);

        for (int i = 0; i < entities.Length; i++)
        {
            Tile tile = entityMgr.GetComponentData<Tile>(entities[i]);
            Entity entity = entityMgr.Instantiate(tilePrefab);
            entityMgr.AddComponent<Tile>(entity);
            entityMgr.SetComponentData(entity, tile);
            entityMgr.SetName(entity, "Tile_0");
            CreateTileMesh(entity, tile, tile.triangleVertexs);
            entityMgr.DestroyEntity(entities[i]);
        }
        entities.Dispose();
    }

    [BurstCompile]
    protected override void OnUpdate()
    {
        Translation sphere = entityMgr.GetComponentData<Translation>(Sphere.sphere);
        Entities.WithAny<Tile>().ForEach((ref Translation translation) =>
        {
            translation = sphere;
        }).ScheduleParallel();

        timer += Time.DeltaTime;
        if (timer < updateTime) return;
        timer = 0;

        int tempMaxLevel = maxLevel;
        //�����Ҫ���µ�ģ��
        Entities.ForEach((Entity entity, ref Tile tile, ref Translation translation, in WorldRenderBounds renderBounds) =>
        {
            translation = sphere;
            if (!tile.isLeaf) return;

            float dist = Vector3.Distance(Vector3.zero, renderBounds.Value.ToBounds().ClosestPoint(Vector3.zero));
            int tempLevel = GetLevel(dist, tempMaxLevel);

            if (tile.level < tempLevel)
            {
                tile.op = TileOperation.split;
            }
            else if (tile.level > tempLevel)
            {
                tile.op = TileOperation.merge;
            }
        })
        .ScheduleParallel();

        EntityQuery entityQuery = entityMgr.CreateEntityQuery(new EntityQueryDesc { Any = new ComponentType[] { ComponentType.ReadWrite<Tile>() } });
        NativeArray<Entity> entities = entityQuery.ToEntityArray(Allocator.Temp);
        Job.WithCode(() =>
        {
            for (int i = 0; i < entities.Length; i++)
            {
                Tile tile = entityMgr.GetComponentData<Tile>(entities[i]);
                if (!tile.isLeaf) continue;
                if (tile.op == TileOperation.split)
                {
                    Split(entities[i]);
                }
                else if (tile.op == TileOperation.merge)
                {
                    bool mergeSuc = Merge(entities[i]);
                    if (mergeSuc) break;//��Щʵ��ɾ���ˣ���ֹ���ʵ�ɾ����ʵ��
                }
            }
        })
        .WithDisposeOnCompletion(entities)
        .WithoutBurst()
        .Run();
    }

    protected override void OnDestroy()
    {
        if (verticesBuffer != null) verticesBuffer.Release();
        if (directionBuffer != null) directionBuffer.Release();
        if (triangleMapBuffer != null) triangleMapBuffer.Release();
        if (vertices != null && vertices.IsCreated) vertices.Dispose();
        if (vectors != null && vectors.IsCreated) vectors.Dispose();
        base.OnDestroy();
    }

    /// <summary>
    /// ����������ϸ�ֳ�һ����ȵ������εĶ���
    /// </summary>
    /// <param name="triangleV">ԭʼ����������</param>
    /// <returns></returns>
    [BurstCompile]
    Vector3[] SubdivideTriangles(TriangleVertexs triangleV, bool isSea)
    {
        //������Ǻ�������ȹ��
        if (!isSea)
        {
            Vector3 topAdd = (triangleV.top - triangleV.left) / (float)(resolution + 1.0f) + (triangleV.top - triangleV.right) / (float)(resolution + 1.0f);
            Vector3 leftAdd = (triangleV.left - triangleV.top) / (float)(resolution + 1.0f) + (triangleV.left - triangleV.right) / (float)(resolution + 1.0f);
            Vector3 rightAdd = (triangleV.right - triangleV.left) / (float)(resolution + 1.0f) + (triangleV.right - triangleV.right) / (float)(resolution + 1.0f);

            triangleV.top += topAdd;
            triangleV.left += leftAdd;
            triangleV.right += rightAdd;
        }

        //��������ϸ�ֵ�����
        vectors[(int)TrianglePos.left] = (triangleV.left - triangleV.top) / (float)(resolution + 1.0f);
        vectors[(int)TrianglePos.middle] = (triangleV.right - triangleV.left) / (float)(resolution + 1.0f);
        vectors[(int)TrianglePos.top] = triangleV.top;

        verticesBuffer.SetData(vertices);
        Sphere.computeVertexShader.SetBuffer(kernelHandle, "Vertices", verticesBuffer);
        directionBuffer.SetData(vectors);
        Sphere.computeVertexShader.SetBuffer(kernelHandle, "Direction", directionBuffer);
        Sphere.computeVertexShader.SetBool("isSea", isSea);
        Sphere.computeVertexShader.Dispatch(kernelHandle, 12, 12, 1);//5��7 ����12��17
        verticesBuffer.GetData(calVexRet);

        return calVexRet;
    }

    void InitTrangleIndex()
    {
        List<int> newTriangles = new List<int>();
        int leftPointCount = 2 + resolution;

        //���������� ����˳����Ȳ��������
        for (int n = 2; n <= leftPointCount; n++)
        {
            int temp = n * (1 + n) / 2 - 1;
            int top = (n - 1) * (1 + (n - 1)) / 2 - 1;

            int temp2 = n * (1 + n) / 2 - 1;
            int top2 = (n - 1) * (1 + (n - 1)) / 2 - 1;

            //��������������
            for (int c = 1; c < n; c++)
            {
                newTriangles.Add(temp);
                newTriangles.Add(top);
                newTriangles.Add(temp - 1);
                temp--;
                top--;
            }
            //���ӵ�����������
            for (int c = 2; c < n; c++)
            {
                newTriangles.Add(top2 - 1);
                newTriangles.Add(temp2 - 1);
                newTriangles.Add(top2);
                temp2--;
                top2--;
            }
        }
        triangleIndexs = newTriangles.ToArray();

        int num = resolution + 2;
        oneMeshVerticesCount = (num * (1 + num)) / 2;

        //һ��������һ���ߵĻ���
        int length = (int)(60.0 * Mathf.PI * Sphere.radius) / 180;
        //�������㼶
        maxLevel = Mathf.RoundToInt(Mathf.Log(length / (accuracy * resolution), 2));
        if (maxLevel < 0) maxLevel = 0;

        //������Ҫ���õĵ�
        for (int leftIndex = 0; leftIndex < leftPointCount; leftIndex++)
        {
            triangleMap.Add(leftIndex);
            triangleMap.Add(0);

            //������Ҫ���õĵ�
            for (int bottomIndex = 1; bottomIndex <= leftIndex; bottomIndex++)
            {
                triangleMap.Add(leftIndex);
                triangleMap.Add(bottomIndex);
            }
        }
    }

    void AllocateMem()
    {
        calVexRet = new Vector3[oneMeshVerticesCount];
    }

    void InitComputeShader()
    {
        verticesBuffer = new ComputeBuffer(oneMeshVerticesCount, 12);
        directionBuffer = new ComputeBuffer(3, 12);
        triangleMapBuffer = new ComputeBuffer(triangleMap.Count, 4);
        kernelHandle = Sphere.computeVertexShader.FindKernel("CSMain");

        triangleMapBuffer.SetData(triangleMap.ToArray());
        Sphere.computeVertexShader.SetBuffer(kernelHandle, "TriangleMap", triangleMapBuffer);
        Sphere.computeVertexShader.SetFloat("Radius", Sphere.radius);
        Sphere.computeVertexShader.SetInt("Resolution", 204); //35 ����204
        Sphere.computeVertexShader.SetInt("seed", 12345);
    }

    [BurstCompile]
    static int GetLevel(float dist,int maxLevel)
    {
        //����<1��ʱ��Mathf.Log(temp, 2)�Ǹ������������ȥ�����������
        if (dist < levelDist) return maxLevel;

        float temp = dist / levelDist;
        int level = (int)Mathf.Log(temp, 2) + 1;
        level = maxLevel - (level > maxLevel ? maxLevel : level);

        return level;
    }

    [BurstCompile]
    void Split(Entity parent)
    {
        Tile parentTile = entityMgr.GetComponentData<Tile>(parent);
        TriangleVertexs originalVertices = new TriangleVertexs();
        Vector3 left = (parentTile.triangleVertexs.top + parentTile.triangleVertexs.left) / 2.0f;
        Vector3 right = (parentTile.triangleVertexs.top + parentTile.triangleVertexs.right) / 2.0f;
        Vector3 bottom = (parentTile.triangleVertexs.left + parentTile.triangleVertexs.right) / 2.0f;

        originalVertices.top = parentTile.triangleVertexs.top;
        originalVertices.left = left;
        originalVertices.right = right;
        Entity childEntity = CreateTileChild(parent, parentTile, originalVertices);
        parentTile.childTop = childEntity;

        originalVertices.top = left;
        originalVertices.left = parentTile.triangleVertexs.left;
        originalVertices.right = bottom;
        childEntity = CreateTileChild(parent, parentTile, originalVertices);
        parentTile.childLeft = childEntity;

        originalVertices.top = right;
        originalVertices.left = bottom;
        originalVertices.right = parentTile.triangleVertexs.right;
        childEntity = CreateTileChild(parent, parentTile, originalVertices);
        parentTile.childRight = childEntity;

        originalVertices.top = bottom;
        originalVertices.left = right;
        originalVertices.right = left;
        childEntity = CreateTileChild(parent, parentTile, originalVertices);
        parentTile.childMid = childEntity;

        parentTile.isLeaf = false;
        parentTile.op = TileOperation.non;
        entityMgr.SetComponentData<Tile>(parent, parentTile);
        //entityMgr.SetEnabled(parent, false);
        HideMesh(parent, false);
    }

    [BurstCompile]
    bool Merge(Entity entity)
    {
        Tile tile = entityMgr.GetComponentData<Tile>(entity);

        if (tile.parent == Entity.Null) return false;

        Tile parentTile = entityMgr.GetComponentData<Tile>(tile.parent);

        Tile tileTop = entityMgr.GetComponentData<Tile>(parentTile.childTop);
        Tile tileMid = entityMgr.GetComponentData<Tile>(parentTile.childMid);
        Tile tileLeft = entityMgr.GetComponentData<Tile>(parentTile.childLeft);
        Tile tileRight = entityMgr.GetComponentData<Tile>(parentTile.childRight);

        if (tileTop.op == TileOperation.merge && 
            tileMid.op == TileOperation.merge &&
            tileLeft.op == TileOperation.merge &&
            tileRight.op == TileOperation.merge)
        {
            WorldRenderBounds renderBounds = entityMgr.GetComponentData<WorldRenderBounds>(tile.parent);
            float dist = Vector3.Distance(Vector3.zero, renderBounds.Value.ToBounds().ClosestPoint(Vector3.zero));
            int tempLevel = GetLevel(dist, maxLevel);
            if (parentTile.level < tempLevel)
            {
                //Debug.LogFormat("id:{0}   dist:{1}   tempLevel:{2}   parentTile.level:{3}   Center:{4}", tile.parent.Index, dist, tempLevel, parentTile.level, renderBounds.Value.Center);
                return false;
            }

            //entityMgr.SetEnabled(tile.parent, true);

            HideMesh(tile.parent, true);
            if (parentTile.childTop != Entity.Null) entityMgr.DestroyEntity(parentTile.childTop);
            if (parentTile.childMid != Entity.Null) entityMgr.DestroyEntity(parentTile.childMid);
            if (parentTile.childLeft != Entity.Null) entityMgr.DestroyEntity(parentTile.childLeft);
            if (parentTile.childRight != Entity.Null) entityMgr.DestroyEntity(parentTile.childRight);

            parentTile.op = TileOperation.non;
            parentTile.isLeaf = true;
            entityMgr.SetComponentData<Tile>(tile.parent, parentTile);
            return true;
        }
        return false;
    }

    [BurstCompile]
    Entity CreateTileChild(Entity parent, Tile parentTile, TriangleVertexs originalVertices)
    {
        Entity newEntity = entityMgr.Instantiate(tilePrefab);
        entityMgr.AddComponent<Tile>(newEntity);
        Tile tile = new Tile
        {
            parent = parent,
            op = TileOperation.non,
            level = parentTile.level + 1,
            triangleVertexs = originalVertices,
            isLeaf = true,
            isSea = parentTile.isSea
        };
        entityMgr.SetName(newEntity, "Tile_" + tile.level);
        entityMgr.SetComponentData<Tile>(newEntity, tile);
        CreateTileMesh(newEntity, tile, originalVertices);
        return newEntity;
    }

    [BurstCompile]
    void CreateTileMesh(Entity entity, Tile tile, TriangleVertexs originalVertices)
    {
        RenderMesh renderMesh = entityMgr.GetSharedComponentData<RenderMesh>(entity);
        RenderBounds renderBounds = entityMgr.GetComponentData<RenderBounds>(entity);

        Mesh mesh = new Mesh();
        mesh.Clear();
        mesh.name = "Tile";
        mesh.vertices = SubdivideTriangles(originalVertices, tile.isSea);
        mesh.triangles = triangleIndexs;
        mesh.RecalculateBounds();
        mesh.RecalculateNormals();
        mesh.RecalculateTangents();
        renderMesh.mesh = mesh;
        renderMesh.material = Sphere.tileMt;
        renderMesh.layerMask = 1;

        renderBounds.Value.Center = mesh.bounds.center;
        renderBounds.Value.Extents = mesh.bounds.extents;

        entityMgr.SetSharedComponentData<RenderMesh>(entity, renderMesh);
        entityMgr.SetComponentData<RenderBounds>(entity, renderBounds);
    }

    void HideMesh(Entity entity,bool show)
    {
        if (!entityMgr.HasComponent<RenderMesh>(entity))
        {
            Debug.LogError("HideMesh:��ʵ����RenderMesh���");
            return;
        }
        RenderMesh renderMesh = entityMgr.GetSharedComponentData<RenderMesh>(entity);
        renderMesh.layerMask = (uint)(show ?1:0);
        entityMgr.SetSharedComponentData<RenderMesh>(entity, renderMesh);
    }
}

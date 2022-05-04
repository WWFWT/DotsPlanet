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
    //将要进行的操作
    public TileOperation op;
    //LOD水平
    public int level;
    //三角形的三个位置的顶点
    public TriangleVertexs triangleVertexs;
    //是否是海洋
    public bool isSea;
    //是否叶子节点
    public bool isLeaf;

    public override string ToString()
    {
        string ret = "";
        ret += "Level:" + level + "   ";
        return ret;
    }
}

public struct VerticesBuffer : IBufferElementData 
{
    public Vector3 vertex;
}


[BurstCompile]
[DisableAutoCreation]
public partial class TileSystem : SystemBase
{
    const int resolution = 286;
    static int oneMeshVerticesCount = 0;
    const float levelDist = 120; //决定第一层持续多少米，之每一层以此为基准增加
    static int maxLevel = 0;
    const float accuracy = 10f; //最大精度
    const float updateTime = 0.05f;
    float timer = 0f;
    public static GameObject tileGameObjectPrefab;

    Entity tilePrefab = Entity.Null;
    EntityManager entityMgr;

    ComputeBuffer verticesBuffer;
    ComputeBuffer directionBuffer;
    ComputeBuffer triangleMapBuffer;
    int kernelHandle;
    NativeArray<Vector3> vectors; //细分三角形时候存储三角形的各个方向
    NativeArray<Vector3> vertices; //细分三角形时候存储三角形的顶点
    NativeList<Entity> willHide;
    Vector3[] calVexRet;

    //避免细分次数一样的三角形重复计算
    static int[] triangleIndexs;
    readonly static List<int> triangleMap = new List<int>();
    static Vector2[] uv;

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
        var tempParam = new BlobAssetStore();
        var setting = GameObjectConversionSettings.FromWorld(World.DefaultGameObjectInjectionWorld, tempParam);
        tilePrefab = GameObjectConversionUtility.ConvertGameObjectHierarchy(tileGameObjectPrefab, setting);
        tempParam.Dispose();
        if (tilePrefab == Entity.Null)
        {
            Debug.LogError("找不到TilePrefab  prefab:"+ tileGameObjectPrefab + "   setting:" + setting);
            return;
        }
        entityMgr.AddComponent<LocalToParent>(tilePrefab);
        entityMgr.AddComponent<Parent>(tilePrefab);

        EntityQuery entityQuery = entityMgr.CreateEntityQuery(new EntityQueryDesc { All = new ComponentType[] { ComponentType.ReadWrite<Tile>() } });
        NativeArray<Entity> entities = entityQuery.ToEntityArray(Allocator.Temp);

        for (int i = 0; i < entities.Length; i++)
        {
            Tile tile = entityMgr.GetComponentData<Tile>(entities[i]);
            Entity entity = entityMgr.Instantiate(tilePrefab);
            entityMgr.AddComponent<Tile>(entity);
            entityMgr.SetComponentData(entity, tile);
            entityMgr.SetName(entity, "Tile_0");

            Parent parent = entityMgr.GetComponentData<Parent>(entity);
            parent.Value = Sphere.sphere;
            entityMgr.SetComponentData<Parent>(entity,parent);

            CreateTileMesh(entity, tile, tile.triangleVertexs);
            entityMgr.DestroyEntity(entities[i]);
        }
        entities.Dispose();
    }

    [BurstCompile]
    protected override void OnUpdate()
    {
        timer += Time.DeltaTime;
        if (timer < updateTime) return;
        timer = 0;

        foreach (Entity entity in willHide)
        {
            SetMeshVisibale(entity, false);
        }
        willHide.Clear();

        NativeArray<Entity> ang = new NativeArray<Entity>();

        int tempMaxLevel = maxLevel;
        //检测需要更新的模型
        Entities.ForEach((Entity entity, ref Tile tile, in WorldRenderBounds renderBounds) =>
        {
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

        float useTime = 0;
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
                    timer = updateTime + 1;//下一帧继续
                    break;
                }
                else if (tile.op == TileOperation.merge)
                {
                    bool mergeSuc = Merge(entities[i]);
                    if (mergeSuc)
                    {
                        timer = updateTime + 1;//下一帧继续
                        break;//有些实体删除了，防止访问到删除的实体
                    }
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
        if (willHide.IsCreated) willHide.Dispose();
        base.OnDestroy();
    }

    /// <summary>
    /// 又三个顶点细分出一组均匀的三角形的顶点
    /// </summary>
    /// <param name="triangleV">原始的三个顶点</param>
    /// <returns></returns>
    [BurstCompile]
    Vector3[] SubdivideTriangles(TriangleVertexs triangleV, bool isSea)
    {
        //如果不是海洋就添加裙边
        if (!isSea)
        {
            Vector3 topAdd = (triangleV.top - triangleV.left) / (float)(resolution + 1.0f) + (triangleV.top - triangleV.right) / (float)(resolution + 1.0f);
            Vector3 leftAdd = (triangleV.left - triangleV.top) / (float)(resolution + 1.0f) + (triangleV.left - triangleV.right) / (float)(resolution + 1.0f);
            Vector3 rightAdd = (triangleV.right - triangleV.left) / (float)(resolution + 1.0f) + (triangleV.right - triangleV.right) / (float)(resolution + 1.0f);

            triangleV.top += topAdd;
            triangleV.left += leftAdd;
            triangleV.right += rightAdd;
        }

        //各个方向细分的向量
        vectors[(int)TrianglePos.left] = (triangleV.left - triangleV.top) / (float)(resolution + 1.0f);
        vectors[(int)TrianglePos.middle] = (triangleV.right - triangleV.left) / (float)(resolution + 1.0f);
        vectors[(int)TrianglePos.top] = triangleV.top;

        verticesBuffer.SetData(vertices);
        Sphere.computeVertexShader.SetBuffer(kernelHandle, "Vertices", verticesBuffer);
        directionBuffer.SetData(vectors);
        Sphere.computeVertexShader.SetBuffer(kernelHandle, "Direction", directionBuffer);
        Sphere.computeVertexShader.SetBool("isSea", isSea);
        Sphere.computeVertexShader.Dispatch(kernelHandle, 12, 12, 1);//5和7 或者12和17
        verticesBuffer.GetData(calVexRet);

        return calVexRet;
    }

    void InitTrangleIndex()
    {
        List<int> newTriangles = new List<int>();
        int leftPointCount = 2 + resolution;

        //连接三角形 连接顺序与等差数列相关
        for (int n = 2; n <= leftPointCount; n++)
        {
            int temp = n * (1 + n) / 2 - 1;
            int top = (n - 1) * (1 + (n - 1)) / 2 - 1;

            int temp2 = n * (1 + n) / 2 - 1;
            int top2 = (n - 1) * (1 + (n - 1)) / 2 - 1;

            //连接正常三角形
            for (int c = 1; c < n; c++)
            {
                newTriangles.Add(temp);
                newTriangles.Add(top);
                newTriangles.Add(temp - 1);
                temp--;
                top--;
            }
            //连接倒立的三角形
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

        //一个三角面一条边的弧长
        int length = (int)(60.0 * Mathf.PI * Sphere.radius) / 180;
        //设置最大层级
        maxLevel = Mathf.RoundToInt(Mathf.Log(length / (accuracy * resolution), 2));
        if (maxLevel < 0) maxLevel = 0;

        //纵向需要布置的点
        for (int leftIndex = 0; leftIndex < leftPointCount; leftIndex++)
        {
            triangleMap.Add(leftIndex);
            triangleMap.Add(0);

            //横向需要布置的点
            for (int bottomIndex = 1; bottomIndex <= leftIndex; bottomIndex++)
            {
                triangleMap.Add(leftIndex);
                triangleMap.Add(bottomIndex);
            }
        }

        List<Vector2> tempUV = new List<Vector2>();


        //底边为1，高则为0.866
        float sidePointCount = resolution + 3;
        float oneHeight = 0.866f / sidePointCount;
        float oneWidth = 1.0f / sidePointCount;

        Vector2 tempVec = new Vector2();

        for (int i = 0; i < sidePointCount; i++)
        {
            tempVec.x = oneHeight * i;
            for (int j = 0; j < i; j++)
            {
                tempVec.y = oneWidth * j;
                tempUV.Add(tempVec);
            }
        }
        uv = tempUV.ToArray();
    }

    void AllocateMem()
    {
        calVexRet = new Vector3[oneMeshVerticesCount];
        willHide = new NativeList<Entity>(16, Allocator.Persistent);
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
        Sphere.computeVertexShader.SetInt("Resolution", 204); //35 或者204
        Sphere.computeVertexShader.SetInt("seed", 12345);
    }

    [BurstCompile]
    static int GetLevel(float dist,int maxLevel)
    {
        //距离<1的时候Mathf.Log(temp, 2)是负数，这里避免去进行这类计算
        if (dist < levelDist) return maxLevel;

        float temp = dist / levelDist;
        int level = (int)Mathf.Log(temp, 2) + 1;
        level = maxLevel - (level > maxLevel ? maxLevel : level);

        return level;
    }

    [BurstCompile]
    void Split(Entity parent)
    {
        entityMgr.AddBuffer<Child>(parent);

        Tile parentTile = entityMgr.GetComponentData<Tile>(parent);
        TriangleVertexs originalVertices = new TriangleVertexs();
        Vector3 left = (parentTile.triangleVertexs.top + parentTile.triangleVertexs.left) / 2.0f;
        Vector3 right = (parentTile.triangleVertexs.top + parentTile.triangleVertexs.right) / 2.0f;
        Vector3 bottom = (parentTile.triangleVertexs.left + parentTile.triangleVertexs.right) / 2.0f;

        originalVertices.top = parentTile.triangleVertexs.top;
        originalVertices.left = left;
        originalVertices.right = right;
        CreateTileChild(parent, parentTile, originalVertices);

        originalVertices.top = left;
        originalVertices.left = parentTile.triangleVertexs.left;
        originalVertices.right = bottom;
        CreateTileChild(parent, parentTile, originalVertices);

        originalVertices.top = right;
        originalVertices.left = bottom;
        originalVertices.right = parentTile.triangleVertexs.right;
        CreateTileChild(parent, parentTile, originalVertices);

        originalVertices.top = bottom;
        originalVertices.left = right;
        originalVertices.right = left;
        CreateTileChild(parent, parentTile, originalVertices);

        parentTile.isLeaf = false;
        parentTile.op = TileOperation.non;
        entityMgr.SetComponentData<Tile>(parent, parentTile);
        willHide.Add(parent);
    }

    [BurstCompile]
    bool Merge(Entity entity)
    {
        Tile tile = entityMgr.GetComponentData<Tile>(entity);

        if (!tile.isLeaf) return false;
        Entity parentEntity = entityMgr.GetComponentData<Parent>(entity).Value;
        Tile parentTile = entityMgr.GetComponentData<Tile>(parentEntity);
        DynamicBuffer<Child> children = entityMgr.GetBuffer<Child>(parentEntity);
        if (children.Length < 4)
        {
            Debug.LogError("Merge:缺少孩子节点");
            return false;
        }

        Entity child1 = children[0].Value;
        Entity child2 = children[1].Value;
        Entity child3 = children[2].Value;
        Entity child4 = children[3].Value;

        Tile tileTop = entityMgr.GetComponentData<Tile>(child1);
        Tile tileMid = entityMgr.GetComponentData<Tile>(child2);
        Tile tileLeft = entityMgr.GetComponentData<Tile>(child3);
        Tile tileRight = entityMgr.GetComponentData<Tile>(child4);

        if (tileTop.op == TileOperation.merge &&
            tileMid.op == TileOperation.merge &&
            tileLeft.op == TileOperation.merge &&
            tileRight.op == TileOperation.merge)
        {
            WorldRenderBounds renderBounds = entityMgr.GetComponentData<WorldRenderBounds>(parentEntity);
            float dist = Vector3.Distance(Vector3.zero, renderBounds.Value.ToBounds().ClosestPoint(Vector3.zero));
            int tempLevel = GetLevel(dist, maxLevel);
            if (parentTile.level < tempLevel)
            {
                //Debug.LogFormat("id:{0}   dist:{1}   tempLevel:{2}   parentTile.level:{3}   Center:{4}", tile.parent.Index, dist, tempLevel, parentTile.level, renderBounds.Value.Center);
                return false;
            }

            SetMeshVisibale(parentEntity, true);
            if (entityMgr.Exists(child1)) entityMgr.DestroyEntity(child1);
            if (entityMgr.Exists(child2)) entityMgr.DestroyEntity(child2);
            if (entityMgr.Exists(child3)) entityMgr.DestroyEntity(child3);
            if (entityMgr.Exists(child4)) entityMgr.DestroyEntity(child4);

            parentTile.op = TileOperation.non;
            parentTile.isLeaf = true;
            entityMgr.RemoveComponent<Child>(parentEntity);
            entityMgr.SetComponentData<Tile>(parentEntity, parentTile);
            return true;
        }
        return false;
    }

    [BurstCompile]
    void CreateTileChild(Entity parent, Tile parentTile, TriangleVertexs originalVertices)
    {
        Entity newEntity = entityMgr.Instantiate(tilePrefab);
        entityMgr.AddComponent<Tile>(newEntity);
        Tile tile = new Tile
        {
            op = TileOperation.non,
            level = parentTile.level + 1,
            triangleVertexs = originalVertices,
            isLeaf = true,
            isSea = parentTile.isSea
        };
        entityMgr.SetName(newEntity, "Tile_" + tile.level);
        entityMgr.SetComponentData<Tile>(newEntity, tile);

        Parent parentCom = entityMgr.GetComponentData<Parent>(newEntity);
        parentCom.Value = parent;
        entityMgr.SetComponentData<Parent>(newEntity, parentCom);

        DynamicBuffer<Child> childs = entityMgr.GetBuffer<Child>(parent);
        Child childCom = new Child();
        childCom.Value = newEntity;
        childs.Add(childCom);

        CreateTileMesh(newEntity, tile, originalVertices);
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
        mesh.uv = uv;
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

    void SetMeshVisibale(Entity entity,bool show)
    {
        if (!entityMgr.HasComponent<RenderMesh>(entity))
        {
            Debug.LogError("SetMeshVisibale:该实体无RenderMesh组件");
            return;
        }
        RenderMesh renderMesh = entityMgr.GetSharedComponentData<RenderMesh>(entity);
        renderMesh.layerMask = (uint)(show ?1:0);
        entityMgr.SetSharedComponentData<RenderMesh>(entity, renderMesh);
    }
}

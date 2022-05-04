using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Rendering;
using Unity.Transforms;
using UnityEngine;

public class Sphere
{
    public static Transform player;
    public static ComputeShader computeVertexShader;
    public static float radius;
    public static Material tileMt;
    public static Material seaMt;
    public static Vector3 pos;

    //ECS相关
    public static Entity sphere;
    private EntityManager entityManager;
    private EntityArchetype tileLeafTypes;

    //正二十面体顶点和三角形
    private static Vector3[] _icosahedronVertices;
    Vector3[] icosahedronVertices
    {
        get
        {
            if (_icosahedronVertices == null)
            {
                _icosahedronVertices = new Vector3[12];

                float m = Mathf.Sqrt(50 - Mathf.Sqrt(5) * 10) / 10;
                float n = Mathf.Sqrt(50 + Mathf.Sqrt(5) * 10) / 10;

                _icosahedronVertices[0] = new Vector3(0, n, m);
                _icosahedronVertices[1] = new Vector3(0, n, -m);
                _icosahedronVertices[2] = new Vector3(-n, m, 0);
                _icosahedronVertices[3] = new Vector3(-m, 0, n);
                _icosahedronVertices[4] = new Vector3(m, 0, n);
                _icosahedronVertices[5] = new Vector3(n, m, 0);
                _icosahedronVertices[6] = new Vector3(-m, 0, -n);
                _icosahedronVertices[7] = new Vector3(-n, -m, 0);
                _icosahedronVertices[8] = new Vector3(0, -n, m);
                _icosahedronVertices[9] = new Vector3(n, -m, 0);
                _icosahedronVertices[10] = new Vector3(m, 0, -n);
                _icosahedronVertices[11] = new Vector3(0, -n, -m);
            }
            return _icosahedronVertices;
        }
    }
    private static int[] _icosahedronTriangles;
    int[] icosahedronTriangles
    {
        get
        {
            if (_icosahedronTriangles == null)
            {
                int[] triangles = new int[12 * 5];

                int index = 0;
                for (int i = 0; i < 5; i++)
                {
                    triangles[index++] = 0;
                    triangles[index++] = i + 1;
                    triangles[index++] = (i == 4 ? 1 : i + 2);

                    triangles[index++] = i + 1;
                    triangles[index++] = i + 6;
                    triangles[index++] = (i == 4 ? 1 : i + 2);

                    triangles[index++] = i + 1;
                    triangles[index++] = (i == 0 ? 10 : i + 5);
                    triangles[index++] = i + 6;

                    triangles[index++] = (i == 0 ? 10 : i + 5);
                    triangles[index++] = 11;
                    triangles[index++] = i + 6;
                }
                _icosahedronTriangles = triangles;
            }
            return _icosahedronTriangles;
        }
    }

    public Sphere()
    {
        entityManager = World.DefaultGameObjectInjectionWorld.EntityManager;
        
        ComponentType[] componentTypes = new ComponentType[]
        {
            ComponentType.ReadWrite<WorldRenderBounds>(),//加这个为了能够让TileSystem运行更新
            ComponentType.ReadWrite<Tile>()
        };
        tileLeafTypes = entityManager.CreateArchetype(componentTypes);

        componentTypes = new ComponentType[]
        {
            ComponentType.ReadWrite<Translation>(),
            ComponentType.ReadWrite<Child>(),
            ComponentType.ReadOnly<LocalToWorld>()
        };

        sphere = entityManager.CreateEntity(componentTypes);
        entityManager.SetName(sphere, "Sphere");

        //细分20个面
        for (int i = 0; i < 20; i++)
        {
            TriangleVertexs triangleVertexs = new TriangleVertexs();
            triangleVertexs.top = icosahedronVertices[icosahedronTriangles[i * 3]];
            triangleVertexs.left = icosahedronVertices[icosahedronTriangles[i * 3 + 1]];
            triangleVertexs.right = icosahedronVertices[icosahedronTriangles[i * 3 + 2]];

            Entity entity = entityManager.CreateEntity(tileLeafTypes);
            entityManager.SetName(entity, "Tile");
            entityManager.SetComponentData<Tile>(entity, new Tile
            {
                op = TileOperation.non,
                triangleVertexs = triangleVertexs,
                isSea = false,
                isLeaf = true,
                level = 0
            }) ;

            entity = entityManager.CreateEntity(tileLeafTypes);
            entityManager.SetComponentData<Tile>(entity, new Tile
            {
                op = TileOperation.non,
                triangleVertexs = triangleVertexs,
                isSea = true,
                isLeaf = true,
                level = 0
            });
        }

    }

    public void Update()
    {
        Translation translation = entityManager.GetComponentData<Translation>(sphere);
        translation.Value.x -= player.position.x;
        translation.Value.y -= player.position.y;
        translation.Value.z -= player.position.z;
        entityManager.SetComponentData<Translation>(sphere, translation);
        pos = translation.Value;
        player.position = Vector3.zero;
    }
}

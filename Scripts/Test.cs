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
    public Vector3 pos;

    // Start is called before the first frame update
    void Start()
    {

    }

    // Update is called once per frame
    void Update()
    {
        //Graphics.DrawMesh(mesh, transform.position, Quaternion.identity, material, 0);
        Graphics.DrawMesh(mesh, pos, Quaternion.identity, material, 1);
    }
}

using System;

[Serializable]
public class ObjathorMeshData
{
    public string assetId;
    public float[] vertices;
    public int[] triangles;
    public float[] normals;
    public float[] uvs;
    public string albedo;
    public string normal;
    public string emission;
    public float yRotOffset;
}

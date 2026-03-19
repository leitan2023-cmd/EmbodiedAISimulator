using System.Collections.Generic;
using LibTessDotNet;
using UnityEngine;

public static class PolygonTriangulator
{
    public static Mesh Triangulate(List<Vec3> polygon)
    {
        Tess tess = new();
        ContourVertex[] contour = new ContourVertex[polygon.Count];

        for (int i = 0; i < polygon.Count; i++)
        {
            contour[i].Position = new LibTessDotNet.Vec3
            {
                X = polygon[i].x,
                Y = 0,
                Z = polygon[i].z
            };
        }

        tess.AddContour(contour, ContourOrientation.CounterClockwise);
        tess.Tessellate(WindingRule.EvenOdd, ElementType.Polygons, 3);

        Mesh mesh = new();
        Vector3[] verts = new Vector3[tess.VertexCount];
        for (int i = 0; i < tess.VertexCount; i++)
        {
            verts[i] = new Vector3(
                tess.Vertices[i].Position.X,
                0,
                tess.Vertices[i].Position.Z
            );
        }

        mesh.vertices = verts;
        mesh.triangles = tess.Elements;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }
}

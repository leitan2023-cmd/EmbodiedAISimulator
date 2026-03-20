using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Cuts rectangular door openings out of wall quads.
/// Given a wall polygon (4 vertices: TL, TR, BR, BL in world space)
/// and a list of door hole rectangles in wall-local space,
/// produces mesh segments that surround the holes.
///
/// Wall-local coordinate system:
///   u = 0..wallWidth  (along bottom edge, from polygon[0] toward polygon[1] projected down)
///   v = 0..wallHeight (from bottom to top)
///
/// Each door hole is defined by holePolygon[0].(x,y) = (uMin, vMin) and
/// holePolygon[1].(x,y) = (uMax, vMax).
/// </summary>
public static class DoorCutter
{
    public struct HoleRect
    {
        public float uMin;
        public float uMax;
        public float vMin;
        public float vMax;
    }

    /// <summary>
    /// Build wall meshes with door holes cut out.
    /// Returns a list of quads (each is a Mesh) that together form the wall minus the holes.
    /// </summary>
    public static List<Mesh> CutWall(WallData wall, List<HoleRect> holes)
    {
        if (wall?.polygon == null || wall.polygon.Count < 4)
            return new List<Mesh>();

        // Wall vertices in world space: TL(0), TR(1), BR(2), BL(3)
        Vector3 tl = wall.polygon[0].ToUnity();
        Vector3 tr = wall.polygon[1].ToUnity();
        Vector3 br = wall.polygon[2].ToUnity();
        Vector3 bl = wall.polygon[3].ToUnity();

        // Wall local axes
        Vector3 bottomLeft = bl;
        Vector3 bottomRight = br;
        Vector3 topLeft = tl;

        Vector3 horizontal = bottomRight - bottomLeft; // along wall bottom edge
        Vector3 vertical = topLeft - bottomLeft;       // from bottom to top

        float wallWidth = horizontal.magnitude;
        float wallHeight = vertical.magnitude;

        if (wallWidth < 0.001f || wallHeight < 0.001f)
            return new List<Mesh>();

        Vector3 hDir = horizontal.normalized;
        Vector3 vDir = vertical.normalized;

        if (holes == null || holes.Count == 0)
        {
            // No doors on this wall — return the original solid quad
            Mesh solid = BuildQuad(bottomLeft, hDir, vDir, 0, wallWidth, 0, wallHeight);
            return new List<Mesh> { solid };
        }

        // Sort holes left to right
        holes.Sort((a, b) => a.uMin.CompareTo(b.uMin));

        List<Mesh> meshes = new();

        // Walk along the wall width, generating segments around each hole.
        // For each hole we generate up to 3 pieces:
        //   1. Left fill strip (from previous edge to hole left)
        //   2. Above-hole strip (from hole top to wall top)
        //   3. Below-hole strip (from wall bottom to hole bottom) — usually vMin=0 so this is empty

        float cursor = 0f;

        foreach (HoleRect hole in holes)
        {
            float hMin = Mathf.Clamp(hole.uMin, 0, wallWidth);
            float hMax = Mathf.Clamp(hole.uMax, 0, wallWidth);
            float vMin = Mathf.Clamp(hole.vMin, 0, wallHeight);
            float vMax = Mathf.Clamp(hole.vMax, 0, wallHeight);

            // Left fill: full-height strip from cursor to hole left edge
            if (hMin - cursor > 0.001f)
            {
                meshes.Add(BuildQuad(bottomLeft, hDir, vDir, cursor, hMin, 0, wallHeight));
            }

            // Below hole: strip from wall bottom to hole bottom (usually zero)
            if (vMin > 0.001f)
            {
                meshes.Add(BuildQuad(bottomLeft, hDir, vDir, hMin, hMax, 0, vMin));
            }

            // Above hole: strip from hole top to wall top (the lintel)
            if (wallHeight - vMax > 0.001f)
            {
                meshes.Add(BuildQuad(bottomLeft, hDir, vDir, hMin, hMax, vMax, wallHeight));
            }

            cursor = hMax;
        }

        // Right fill: from last hole's right edge to wall's right edge
        if (wallWidth - cursor > 0.001f)
        {
            meshes.Add(BuildQuad(bottomLeft, hDir, vDir, cursor, wallWidth, 0, wallHeight));
        }

        return meshes;
    }

    /// <summary>
    /// Build a double-sided quad in world space.
    /// origin + hDir * uMin/uMax defines horizontal span.
    /// origin + vDir * vMin/vMax defines vertical span.
    /// </summary>
    private static Mesh BuildQuad(Vector3 origin, Vector3 hDir, Vector3 vDir,
        float uMin, float uMax, float vMin, float vMax)
    {
        Vector3 v0 = origin + hDir * uMin + vDir * vMin; // bottom-left
        Vector3 v1 = origin + hDir * uMax + vDir * vMin; // bottom-right
        Vector3 v2 = origin + hDir * uMax + vDir * vMax; // top-right
        Vector3 v3 = origin + hDir * uMin + vDir * vMax; // top-left

        Mesh mesh = new()
        {
            vertices = new[] { v0, v1, v2, v3 },
            triangles = new[]
            {
                0, 3, 2, 0, 2, 1, // front face
                2, 3, 0, 1, 2, 0  // back face
            }
        };
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    /// <summary>
    /// Convert a DoorData's holePolygon (2 Vec3 points: min corner and max corner)
    /// into a HoleRect in wall-local (u, v) space.
    /// </summary>
    public static HoleRect DoorToHoleRect(DoorData door)
    {
        if (door.holePolygon == null || door.holePolygon.Count < 2)
        {
            return new HoleRect { uMin = 0, uMax = 0, vMin = 0, vMax = 0 };
        }

        Vec3 min = door.holePolygon[0];
        Vec3 max = door.holePolygon[1];

        return new HoleRect
        {
            uMin = Mathf.Min(min.x, max.x),
            uMax = Mathf.Max(min.x, max.x),
            vMin = Mathf.Min(min.y, max.y),
            vMax = Mathf.Max(min.y, max.y)
        };
    }
}

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Collects simple shapes (boxes, roofs, cylinders, domes, road strips) into a
/// single mesh with one submesh per material. Used to build whole districts as
/// a handful of objects instead of thousands.
/// </summary>
public class MeshKit
{
    readonly List<Vector3> vertices = new List<Vector3>();
    readonly List<Vector2> uvs = new List<Vector2>();
    readonly List<Material> materials = new List<Material>();
    readonly List<List<int>> triangles = new List<List<int>>();

    public int VertexCount => vertices.Count;

    List<int> Triangles(Material material)
    {
        int index = materials.IndexOf(material);
        if (index < 0)
        {
            materials.Add(material);
            triangles.Add(new List<int>());
            index = materials.Count - 1;
        }
        return triangles[index];
    }

    /// <summary>A four-cornered face, wound so it is visible from the side <paramref name="outward"/> points to.</summary>
    public void Quad(Material material, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 outward, Vector2 ua, Vector2 ub, Vector2 uc, Vector2 ud)
    {
        int i = vertices.Count;
        vertices.Add(a); vertices.Add(b); vertices.Add(c); vertices.Add(d);
        uvs.Add(ua); uvs.Add(ub); uvs.Add(uc); uvs.Add(ud);

        List<int> t = Triangles(material);
        if (Vector3.Dot(Vector3.Cross(b - a, c - a), outward) >= 0f)
        {
            t.Add(i); t.Add(i + 1); t.Add(i + 2); t.Add(i); t.Add(i + 2); t.Add(i + 3);
        }
        else
        {
            t.Add(i); t.Add(i + 2); t.Add(i + 1); t.Add(i); t.Add(i + 3); t.Add(i + 2);
        }
    }

    public void Quad(Material material, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 outward)
    {
        Quad(material, a, b, c, d, outward, new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1));
    }

    public void Triangle(Material material, Vector3 a, Vector3 b, Vector3 c, Vector3 outward)
    {
        int i = vertices.Count;
        vertices.Add(a); vertices.Add(b); vertices.Add(c);
        uvs.Add(new Vector2(0, 0)); uvs.Add(new Vector2(1, 0)); uvs.Add(new Vector2(0.5f, 1));
        List<int> t = Triangles(material);
        bool keep = Vector3.Dot(Vector3.Cross(b - a, c - a), outward) >= 0f;
        t.Add(i); t.Add(keep ? i + 1 : i + 2); t.Add(keep ? i + 2 : i + 1);
    }

    /// <summary>
    /// Four walls and a flat roof standing on <paramref name="baseCentre"/>.
    /// With tile sizes given, the wall texture repeats at real-world scale
    /// (so windows stay the same size on every building); otherwise it is
    /// stretched once across each face.
    /// </summary>
    public void Box(Material wall, Material roof, Vector3 baseCentre, float width, float depth, float height, float yaw = 0f, float tileWidth = 0f, float tileHeight = 0f, float uOffset = 0f)
    {
        Quaternion q = Quaternion.Euler(0f, yaw, 0f);
        float hw = width * 0.5f, hd = depth * 0.5f;
        Vector3[] corner =
        {
            baseCentre + q * new Vector3(-hw, 0f, -hd), baseCentre + q * new Vector3(hw, 0f, -hd),
            baseCentre + q * new Vector3(hw, 0f, hd), baseCentre + q * new Vector3(-hw, 0f, hd),
        };
        Vector3 up = Vector3.up * height;

        for (int i = 0; i < 4; i++)
        {
            Vector3 a = corner[i], b = corner[(i + 1) % 4];
            float length = Vector3.Distance(a, b);
            float u1 = tileWidth > 0f ? length / tileWidth : 1f;
            float v1 = tileHeight > 0f ? height / tileHeight : 1f;
            Vector3 outward = (a + b) * 0.5f - baseCentre;
            Quad(wall, a, b, b + up, a + up, outward,
                new Vector2(uOffset, 0f), new Vector2(uOffset + u1, 0f), new Vector2(uOffset + u1, v1), new Vector2(uOffset, v1));
        }

        if (roof != null)
        {
            const float roofTile = 24f;
            Quad(roof, corner[0] + up, corner[1] + up, corner[2] + up, corner[3] + up, Vector3.up,
                new Vector2(0f, 0f), new Vector2(width / roofTile, 0f), new Vector2(width / roofTile, depth / roofTile), new Vector2(0f, depth / roofTile));
        }
    }

    /// <summary>A pitched roof with its ridge running along the depth axis, sitting on top of a box.</summary>
    public void Gable(Material material, Vector3 eavesCentre, float width, float depth, float rise, float yaw = 0f, float overhang = 0.6f)
    {
        Quaternion q = Quaternion.Euler(0f, yaw, 0f);
        float hw = width * 0.5f + overhang, hd = depth * 0.5f + overhang;
        Vector3 fl = eavesCentre + q * new Vector3(-hw, 0f, -hd), fr = eavesCentre + q * new Vector3(hw, 0f, -hd);
        Vector3 bl = eavesCentre + q * new Vector3(-hw, 0f, hd), br = eavesCentre + q * new Vector3(hw, 0f, hd);
        Vector3 rf = eavesCentre + q * new Vector3(0f, rise, -hd), rb = eavesCentre + q * new Vector3(0f, rise, hd);

        float u = depth / 8f, v = width / 8f;
        Quad(material, fl, rf, rb, bl, q * new Vector3(-1f, 1f, 0f), new Vector2(0, 0), new Vector2(0, v), new Vector2(u, v), new Vector2(u, 0));
        Quad(material, fr, rf, rb, br, q * new Vector3(1f, 1f, 0f), new Vector2(0, 0), new Vector2(0, v), new Vector2(u, v), new Vector2(u, 0));
        Triangle(material, fl, fr, rf, q * Vector3.back);
        Triangle(material, bl, br, rb, q * Vector3.forward);
    }

    /// <summary>An upright cylinder (or cone frustum, with a different top radius).</summary>
    public void Cylinder(Material wall, Material cap, Vector3 baseCentre, float radius, float height, int sides = 16, float topRadius = -1f, float tileWidth = 0f, float tileHeight = 0f)
    {
        if (topRadius < 0f) topRadius = radius;
        Vector3 top = baseCentre + Vector3.up * height;
        float circumference = Mathf.PI * 2f * radius;
        float uTotal = tileWidth > 0f ? circumference / tileWidth : 1f;
        float vTotal = tileHeight > 0f ? height / tileHeight : 1f;

        for (int i = 0; i < sides; i++)
        {
            float a0 = i * Mathf.PI * 2f / sides, a1 = (i + 1) * Mathf.PI * 2f / sides;
            Vector3 d0 = new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0)), d1 = new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1));
            float u0 = uTotal * i / sides, u1 = uTotal * (i + 1) / sides;
            Quad(wall, baseCentre + d0 * radius, baseCentre + d1 * radius, top + d1 * topRadius, top + d0 * topRadius, d0 + d1,
                new Vector2(u0, 0f), new Vector2(u1, 0f), new Vector2(u1, vTotal), new Vector2(u0, vTotal));
            if (cap != null) Triangle(cap, top, top + d0 * topRadius, top + d1 * topRadius, Vector3.up);
        }
    }

    /// <summary>A hemisphere sitting on <paramref name="centre"/>; pass full = true for a whole sphere around it.</summary>
    public void Dome(Material material, Vector3 centre, float radius, int sides = 16, int rings = 5, float yScale = 1f, bool full = false)
    {
        float startAngle = full ? -Mathf.PI * 0.5f : 0f;
        int count = full ? rings * 2 : rings;
        for (int r = 0; r < count; r++)
        {
            float e0 = Mathf.Lerp(startAngle, Mathf.PI * 0.5f, r / (float)count);
            float e1 = Mathf.Lerp(startAngle, Mathf.PI * 0.5f, (r + 1) / (float)count);
            for (int s = 0; s < sides; s++)
            {
                float a0 = s * Mathf.PI * 2f / sides, a1 = (s + 1) * Mathf.PI * 2f / sides;
                Vector3 p00 = Point(centre, radius, yScale, a0, e0), p10 = Point(centre, radius, yScale, a1, e0);
                Vector3 p11 = Point(centre, radius, yScale, a1, e1), p01 = Point(centre, radius, yScale, a0, e1);
                Vector3 outward = (p00 + p10 + p11 + p01) * 0.25f - centre;
                Quad(material, p00, p10, p11, p01, outward);
            }
        }
    }

    static Vector3 Point(Vector3 centre, float radius, float yScale, float azimuth, float elevation)
    {
        float c = Mathf.Cos(elevation);
        return centre + new Vector3(Mathf.Cos(azimuth) * c * radius, Mathf.Sin(elevation) * radius * yScale, Mathf.Sin(azimuth) * c * radius);
    }

    /// <summary>
    /// A ribbon following a line of points: roads, taxiways, painted lines.
    /// The texture runs once across the width and repeats every vTile metres along it.
    /// </summary>
    public void Strip(Material material, IList<Vector3> centreLine, float width, float vTile, float lift = 0f)
    {
        float travelled = 0f;
        Vector3 prevLeft = Vector3.zero, prevRight = Vector3.zero;
        for (int i = 0; i < centreLine.Count; i++)
        {
            Vector3 ahead = centreLine[Mathf.Min(i + 1, centreLine.Count - 1)] - centreLine[Mathf.Max(i - 1, 0)];
            ahead.y = 0f;
            Vector3 side = Vector3.Cross(Vector3.up, ahead.normalized) * (width * 0.5f);
            Vector3 p = centreLine[i] + Vector3.up * lift;
            Vector3 left = p - side, right = p + side;

            if (i > 0)
            {
                float step = Vector3.Distance(centreLine[i], centreLine[i - 1]);
                float v0 = travelled / vTile, v1 = (travelled + step) / vTile;
                Quad(material, prevLeft, prevRight, right, left, Vector3.up,
                    new Vector2(0f, v0), new Vector2(1f, v0), new Vector2(1f, v1), new Vector2(0f, v1));
                travelled += step;
            }
            prevLeft = left; prevRight = right;
        }
    }

    /// <summary>A flat rectangle lying on the ground, e.g. an apron or a painted marking.</summary>
    public void GroundRect(Material material, Vector3 centre, float width, float depth, float yaw = 0f, float tile = 0f)
    {
        Quaternion q = Quaternion.Euler(0f, yaw, 0f);
        float hw = width * 0.5f, hd = depth * 0.5f;
        float u = tile > 0f ? width / tile : 1f, v = tile > 0f ? depth / tile : 1f;
        Quad(material,
            centre + q * new Vector3(-hw, 0f, -hd), centre + q * new Vector3(hw, 0f, -hd),
            centre + q * new Vector3(hw, 0f, hd), centre + q * new Vector3(-hw, 0f, hd), Vector3.up,
            new Vector2(0f, 0f), new Vector2(u, 0f), new Vector2(u, v), new Vector2(0f, v));
    }

    /// <summary>
    /// Extrudes a flat outline (points given as X, Z) between two heights and caps the top.
    /// The outline should be roughly convex. The top texture repeats every topTile metres.
    /// </summary>
    public void Prism(Material side, Material top, IList<Vector2> outline, float bottom, float topHeight, float topTile = 20f)
    {
        Vector2 centre = Vector2.zero;
        foreach (Vector2 p in outline) centre += p;
        centre /= outline.Count;

        for (int i = 0; i < outline.Count; i++)
        {
            Vector2 a = outline[i], b = outline[(i + 1) % outline.Count];
            Vector2 mid = (a + b) * 0.5f - centre;
            Quad(side, new Vector3(a.x, bottom, a.y), new Vector3(b.x, bottom, b.y), new Vector3(b.x, topHeight, b.y), new Vector3(a.x, topHeight, a.y),
                new Vector3(mid.x, 0f, mid.y));

            if (top == null) continue;
            int index = vertices.Count;
            vertices.Add(new Vector3(centre.x, topHeight, centre.y));
            vertices.Add(new Vector3(a.x, topHeight, a.y));
            vertices.Add(new Vector3(b.x, topHeight, b.y));
            uvs.Add(centre / topTile); uvs.Add(a / topTile); uvs.Add(b / topTile);
            List<int> t = Triangles(top);
            bool keep = Vector3.Cross(vertices[index + 1] - vertices[index], vertices[index + 2] - vertices[index]).y >= 0f;
            t.Add(index); t.Add(keep ? index + 1 : index + 2); t.Add(keep ? index + 2 : index + 1);
        }
    }

    /// <summary>
    /// A ship's hull: cross-sections along Z, each wide at the top and narrower
    /// at the bottom. Each section is (z, half-width at top, half-width at bottom).
    /// </summary>
    public void Hull(Material material, IList<Vector3> sections, float topHeight, float bottom)
    {
        for (int i = 0; i < sections.Count - 1; i++)
        {
            Vector3 a = sections[i], b = sections[i + 1];
            foreach (float side in new[] { -1f, 1f })
            {
                Quad(material,
                    new Vector3(side * a.y, topHeight, a.x), new Vector3(side * b.y, topHeight, b.x),
                    new Vector3(side * b.z, bottom, b.x), new Vector3(side * a.z, bottom, a.x),
                    new Vector3(side, 0.2f, 0f));
            }
        }
        // Flat transom across the stern.
        Vector3 stern = sections[0];
        Quad(material,
            new Vector3(-stern.y, topHeight, stern.x), new Vector3(stern.y, topHeight, stern.x),
            new Vector3(stern.z, bottom, stern.x), new Vector3(-stern.z, bottom, stern.x), Vector3.back);
    }

    public Mesh BuildMesh(string name, out Material[] usedMaterials)
    {
        var mesh = new Mesh { name = name, indexFormat = IndexFormat.UInt32 };
        mesh.SetVertices(vertices);
        mesh.SetUVs(0, uvs);
        mesh.subMeshCount = materials.Count;
        for (int i = 0; i < materials.Count; i++) mesh.SetTriangles(triangles[i], i);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        usedMaterials = materials.ToArray();
        return mesh;
    }
}

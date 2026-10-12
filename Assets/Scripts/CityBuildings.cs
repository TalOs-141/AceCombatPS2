using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Raises the real city from its footprints. Each building in the data file
/// (outlines and heights from OpenStreetMap) is extruded from the ground to
/// its height; the roof is textured with the aerial photo of that exact spot,
/// so from above the city still matches the photograph, and the walls get a
/// facade to suit the building's height.
///
/// Built once at startup into one mesh per square kilometre, and kept across
/// mission restarts.
/// </summary>
public class CityBuildings : MonoBehaviour
{
    [Tooltip("Footprints, heights and roof triangles; see Assets/RealWorld/README.md.")]
    public TextAsset data;
    [Tooltip("One roof material per terrain tile, indexed tileZ * tilesPerSide + tileX.")]
    public Material[] roofByTile;
    public Material wallsLow;
    public Material wallsMid;
    public Material wallsTall;

    [Header("Map layout (metres)")]
    public float mapSize = 20000f;
    public int tilesPerSide = 4;
    public float chunkSize = 1000f;

    [Header("Heights (m)")]
    [Tooltip("Buildings at least this tall use the mid-rise facade.")]
    public float midHeight = 9f;
    [Tooltip("Buildings at least this tall use the tower facade.")]
    public float tallHeight = 40f;
    [Tooltip("Buildings at least this tall can be crashed into.")]
    public float solidHeight = 25f;

    const int Roof = 0, Low = 1, Mid = 2, Tall = 3;

    static GameObject built;

    Terrain[] terrainGrid;
    float[] terrainBase;

    class Chunk
    {
        public readonly List<Vector3> vertices = new List<Vector3>();
        public readonly List<Vector2> uvs = new List<Vector2>();
        public readonly List<int>[] triangles = { new List<int>(), new List<int>(), new List<int>(), new List<int>() };
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => built = null;

    void Start()
    {
        // Already standing from an earlier run of the mission.
        if (built != null || data == null) return;

        float started = Time.realtimeSinceStartup;
        FindTerrains();
        built = new GameObject("City Buildings (generated)");
        DontDestroyOnLoad(built);

        int perSide = Mathf.CeilToInt(mapSize / chunkSize);
        var chunks = new Chunk[perSide * perSide];
        float half = mapSize * 0.5f, tileSize = mapSize / tilesPerSide;
        int count, solid = 0;

        var corners = new List<Vector2>(64);
        using (var reader = new BinaryReader(new MemoryStream(data.bytes)))
        {
            count = reader.ReadInt32();
            for (int b = 0; b < count; b++)
            {
                int cornerCount = reader.ReadUInt16(), triangleCount = reader.ReadUInt16();
                float height = reader.ReadSingle();

                corners.Clear();
                Vector2 centre = Vector2.zero, min = new Vector2(float.MaxValue, float.MaxValue), max = -min;
                for (int i = 0; i < cornerCount; i++)
                {
                    var p = new Vector2(reader.ReadSingle(), reader.ReadSingle());
                    corners.Add(p);
                    centre += p;
                    min = Vector2.Min(min, p);
                    max = Vector2.Max(max, p);
                }
                centre /= cornerCount;

                int cx = Mathf.Clamp((int)((centre.x + half) / chunkSize), 0, perSide - 1);
                int cz = Mathf.Clamp((int)((centre.y + half) / chunkSize), 0, perSide - 1);
                Chunk chunk = chunks[cz * perSide + cx] ?? (chunks[cz * perSide + cx] = new Chunk());

                // Stand on the lowest corner so nothing floats on sloping ground.
                float ground = float.MaxValue;
                for (int i = 0; i < cornerCount; i++) ground = Mathf.Min(ground, GroundHeight(corners[i]));
                ground -= 0.5f;
                float top = ground + height + 0.5f;

                // Roof: the photo tile under the building, at the building's own position.
                float tileX = Mathf.Floor((centre.x + half) / tileSize) * tileSize - half;
                float tileZ = Mathf.Floor((centre.y + half) / tileSize) * tileSize - half;
                int first = chunk.vertices.Count;
                for (int i = 0; i < cornerCount; i++)
                {
                    chunk.vertices.Add(new Vector3(corners[i].x, top, corners[i].y));
                    chunk.uvs.Add(new Vector2((corners[i].x - tileX) / tileSize, (corners[i].y - tileZ) / tileSize));
                }
                List<int> roof = chunk.triangles[Roof];
                for (int t = 0; t < triangleCount; t++)
                {
                    int a = reader.ReadByte(), b2 = reader.ReadByte(), c = reader.ReadByte();
                    // The file's triangles are counter-clockwise seen from above; Unity wants clockwise.
                    roof.Add(first + a); roof.Add(first + c); roof.Add(first + b2);
                }

                // Walls: one flat panel per edge, the facade repeating at real-world scale.
                List<int> walls = chunk.triangles[height >= tallHeight ? Tall : height >= midHeight ? Mid : Low];
                float along = 0f;
                for (int i = 0; i < cornerCount; i++)
                {
                    Vector2 p0 = corners[i], p1 = corners[(i + 1) % cornerCount];
                    float length = Vector2.Distance(p0, p1);
                    int v = chunk.vertices.Count;
                    chunk.vertices.Add(new Vector3(p0.x, ground, p0.y));
                    chunk.vertices.Add(new Vector3(p0.x, top, p0.y));
                    chunk.vertices.Add(new Vector3(p1.x, top, p1.y));
                    chunk.vertices.Add(new Vector3(p1.x, ground, p1.y));
                    float u0 = along / 32f, u1 = (along + length) / 32f, vTop = (height + 1f) / 28f;
                    chunk.uvs.Add(new Vector2(u0, 0f)); chunk.uvs.Add(new Vector2(u0, vTop));
                    chunk.uvs.Add(new Vector2(u1, vTop)); chunk.uvs.Add(new Vector2(u1, 0f));
                    walls.Add(v); walls.Add(v + 1); walls.Add(v + 2);
                    walls.Add(v); walls.Add(v + 2); walls.Add(v + 3);
                    along += length;
                }

                if (height >= solidHeight)
                {
                    var blocker = new GameObject("Tall Building");
                    blocker.transform.SetParent(built.transform, false);
                    blocker.transform.position = new Vector3((min.x + max.x) * 0.5f, ground + (top - ground) * 0.5f, (min.y + max.y) * 0.5f);
                    // Slightly inside the outline, so a near miss past a corner stays a near miss.
                    blocker.AddComponent<BoxCollider>().size = new Vector3((max.x - min.x) * 0.85f, top - ground, (max.y - min.y) * 0.85f);
                    solid++;
                }
            }
        }

        int meshes = 0;
        for (int i = 0; i < chunks.Length; i++)
        {
            Chunk chunk = chunks[i];
            if (chunk == null) continue;

            int cx = i % perSide, cz = i / perSide;
            int tile = Mathf.Clamp((int)((cz + 0.5f) * chunkSize / tileSize), 0, tilesPerSide - 1) * tilesPerSide
                       + Mathf.Clamp((int)((cx + 0.5f) * chunkSize / tileSize), 0, tilesPerSide - 1);

            var mesh = new Mesh { name = $"Buildings {cx} {cz}", indexFormat = IndexFormat.UInt32 };
            mesh.SetVertices(chunk.vertices);
            mesh.SetUVs(0, chunk.uvs);
            mesh.subMeshCount = 4;
            for (int s = 0; s < 4; s++) mesh.SetTriangles(chunk.triangles[s], s);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            var go = new GameObject(mesh.name, typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(built.transform, false);
            go.GetComponent<MeshFilter>().sharedMesh = mesh;
            go.GetComponent<MeshRenderer>().sharedMaterials = new[] { roofByTile[tile], wallsLow, wallsMid, wallsTall };
            meshes++;
        }

        Debug.Log($"City: {count} buildings in {meshes} meshes, {solid} solid, built in {Time.realtimeSinceStartup - started:0.0} s");
    }

    /// <summary>Indexes the terrain tiles by grid position, so each height lookup goes straight to the right one.</summary>
    void FindTerrains()
    {
        terrainGrid = new Terrain[tilesPerSide * tilesPerSide];
        terrainBase = new float[terrainGrid.Length];
        float half = mapSize * 0.5f, tileSize = mapSize / tilesPerSide;
        foreach (Terrain terrain in Terrain.activeTerrains)
        {
            Vector3 origin = terrain.transform.position;
            int ix = Mathf.RoundToInt((origin.x + half) / tileSize), iz = Mathf.RoundToInt((origin.z + half) / tileSize);
            if (ix < 0 || iz < 0 || ix >= tilesPerSide || iz >= tilesPerSide) continue;
            terrainGrid[iz * tilesPerSide + ix] = terrain;
            terrainBase[iz * tilesPerSide + ix] = origin.y;
        }
    }

    float GroundHeight(Vector2 at)
    {
        float half = mapSize * 0.5f, tileSize = mapSize / tilesPerSide;
        int ix = Mathf.Clamp((int)((at.x + half) / tileSize), 0, tilesPerSide - 1);
        int iz = Mathf.Clamp((int)((at.y + half) / tileSize), 0, tilesPerSide - 1);
        Terrain terrain = terrainGrid[iz * tilesPerSide + ix];
        return terrain != null ? terrain.SampleHeight(new Vector3(at.x, 0f, at.y)) + terrainBase[iz * tilesPerSide + ix] : 0f;
    }
}

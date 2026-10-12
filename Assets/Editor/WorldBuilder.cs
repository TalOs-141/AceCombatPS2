using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

/// <summary>
/// Builds the map: shapes the terrain around each site, then lays down the
/// roads, the city, the port and refinery, the airport's civil side, the
/// mountain observatory and the forests. Everything it places lives under one
/// "World" object in the scene and can be regenerated from the AceCombat menu.
/// </summary>
public static class WorldBuilder
{
    static float[,] heights;
    static System.Random rng;
    static Dictionary<string, MeshKit> kits;
    static Dictionary<string, Transform> colliderParents;
    static List<TreeInstance> trees;
    static Transform root;

    // Materials, filled in by BuildMaterials.
    static Material road, concrete, roofGravel, corrugated, steel, rust, white, darkGlass, paint, lamp, pole;
    static Material[] towers, offices, flats, houseWalls, houseRoofs, containers, cars;
    static Material hullRed, hullBlack, craneBlue, treeConifer, treeLeaf, trunk;

    [MenuItem("AceCombat/Rebuild World")]
    public static void BuildMenu() => Debug.Log(Build());

    public static string Build()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return "Stop Play mode first.";
        Terrain terrain = Terrain.activeTerrain;
        if (terrain == null) return "No terrain in the scene.";

        rng = new System.Random(20261011);
        kits = new Dictionary<string, MeshKit>();
        colliderParents = new Dictionary<string, Transform>();
        trees = new List<TreeInstance>();
        var log = new List<string>();

        GameObject old = GameObject.Find("World");
        if (old != null) Object.DestroyImmediate(old);
        root = new GameObject("World").transform;

        // The loose grey blocks around the airport are replaced by the real city.
        GameObject arena = GameObject.Find("Arena");
        Transform looseBuildings = arena != null ? arena.transform.Find("Buildings") : null;
        if (looseBuildings != null) Object.DestroyImmediate(looseBuildings.gameObject);

        BuildMaterials();
        GenerateHeights();

        // Roads are cut into the terrain before it is applied, so they sit on level ground.
        List<Vector3> highway = CarveRoad(new[]
        {
            new Vector2(660f, -250f), new Vector2(1500f, -700f), new Vector2(2500f, -2400f),
            new Vector2(3100f, -3350f), new Vector2(3500f, -3460f),
        }, 0f, WorldLayout.CityHeight);
        List<Vector3> mountainRoad = CarveRoad(MountainRoute(), 0f, WorldLayout.Observatory.y);

        ApplyHeights(terrain);

        Kit("Roads").Strip(road, highway, 22f, 24f, 1.2f);
        Kit("Roads").Strip(road, mountainRoad, 12f, 24f, 1.2f);
        Lights("Roads", highway, 13f, 70f);

        BuildCity(log);
        BuildPort(log);
        BuildAirport(log);
        BuildObservatory(log);
        ScatterForest(log);

        FinishKits();
        ApplyTrees(terrain, log);
        LiftAircraftClear();

        GraphicsBuilder.BuildTerrainPaint(log);

        AssetDatabase.SaveAssets();
        Scene scene = SceneManager.GetActiveScene();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        return string.Join("; ", log);
    }

    // --------------------------------------------------------------- utilities

    static float Rand(float min, float max) => min + (float)rng.NextDouble() * (max - min);
    static T Pick<T>(T[] options) => options[rng.Next(options.Length)];

    static MeshKit Kit(string name)
    {
        if (!kits.TryGetValue(name, out MeshKit kit))
        {
            kit = new MeshKit();
            kits[name] = kit;
        }
        return kit;
    }

    /// <summary>An invisible box the plane can crash into, grouped under the site it belongs to.</summary>
    static void Solid(string site, Vector3 baseCentre, float width, float depth, float height, float yaw = 0f)
    {
        if (!colliderParents.TryGetValue(site, out Transform parent))
        {
            parent = new GameObject(site + " Colliders").transform;
            parent.SetParent(root, false);
            colliderParents[site] = parent;
        }
        var go = new GameObject("Solid");
        go.transform.SetParent(parent, false);
        go.transform.SetPositionAndRotation(baseCentre + Vector3.up * (height * 0.5f), Quaternion.Euler(0f, yaw, 0f));
        go.AddComponent<BoxCollider>().size = new Vector3(width, height, depth);
    }

    static void FinishKits()
    {
        foreach (KeyValuePair<string, MeshKit> entry in kits)
        {
            Mesh mesh = entry.Value.BuildMesh(entry.Key, out Material[] materials);
            mesh = GraphicsBuilder.Save(mesh, "World_" + entry.Key.Replace(' ', '_'));
            var go = new GameObject(entry.Key, typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(root, false);
            go.isStatic = true;
            go.GetComponent<MeshFilter>().sharedMesh = mesh;
            go.GetComponent<MeshRenderer>().sharedMaterials = materials;
        }
    }

    // ----------------------------------------------------------------- terrain

    static float ToCell(float world) => (world / WorldLayout.WorldSize + 0.5f) * (WorldLayout.Resolution - 1);
    static float ToWorld(int cell) => (cell / (float)(WorldLayout.Resolution - 1) - 0.5f) * WorldLayout.WorldSize;

    /// <summary>Ground height in world metres at any point, read from the working heightmap.</summary>
    static float Ground(float wx, float wz)
    {
        float fx = Mathf.Clamp(ToCell(wx), 0f, WorldLayout.Resolution - 1.001f);
        float fz = Mathf.Clamp(ToCell(wz), 0f, WorldLayout.Resolution - 1.001f);
        int x = (int)fx, z = (int)fz;
        float tx = fx - x, tz = fz - z;
        return Mathf.Lerp(Mathf.Lerp(heights[z, x], heights[z, x + 1], tx), Mathf.Lerp(heights[z + 1, x], heights[z + 1, x + 1], tx), tz);
    }

    static void GenerateHeights()
    {
        int res = WorldLayout.Resolution;
        heights = new float[res, res];
        Rect city = WorldLayout.City, port = WorldLayout.Port;

        for (int z = 0; z < res; z++)
        {
            for (int x = 0; x < res; x++)
            {
                float wx = ToWorld(x), wz = ToWorld(z);
                float y = WorldLayout.NaturalHeight(wx, wz);

                // Level the city's site, easing back into the hills around it.
                y = Mathf.Lerp(y, WorldLayout.CityHeight, 1f - WorldLayout.Step(0f, 1000f, WorldLayout.Outside(city, wx, wz)));
                // The port is reclaimed land right at the water...
                y = Mathf.Lerp(y, WorldLayout.PortHeight, 1f - WorldLayout.Step(0f, 350f, WorldLayout.Outside(port, wx, wz)));
                // ...with open harbour water in front of the quay.
                if (wz < WorldLayout.QuayZ)
                {
                    float alongQuay = 1f - WorldLayout.Step(0f, 500f, Mathf.Max(port.xMin - 200f - wx, 0f, wx - port.xMax - 200f));
                    float offshore = 1f - WorldLayout.Step(1500f, 2200f, WorldLayout.QuayZ - wz);
                    y = Mathf.Lerp(y, -WorldLayout.SeaFloor, alongQuay * offshore);
                }
                heights[z, x] = y;
            }
        }

        // Observatory: the highest summit in the mountains north-west of the airport, with its top levelled.
        float best = float.MinValue;
        Vector2 peak = Vector2.zero;
        for (int z = (int)ToCell(2500f); z <= (int)ToCell(8500f); z++)
        {
            for (int x = (int)ToCell(-9500f); x <= (int)ToCell(-4500f); x++)
            {
                if (heights[z, x] <= best) continue;
                best = heights[z, x];
                peak = new Vector2(ToWorld(x), ToWorld(z));
            }
        }
        float padHeight = best - 30f;
        WorldLayout.Observatory = new Vector3(peak.x, padHeight, peak.y);
        Stamp(peak, padHeight, 70f, 220f);
    }

    /// <summary>Flattens a disc of terrain to a height, blending out to the surrounding ground.</summary>
    static void Stamp(Vector2 centre, float height, float flatRadius, float outerRadius)
    {
        int reach = Mathf.CeilToInt(outerRadius / (WorldLayout.WorldSize / (WorldLayout.Resolution - 1))) + 1;
        int cx = Mathf.RoundToInt(ToCell(centre.x)), cz = Mathf.RoundToInt(ToCell(centre.y));
        for (int z = Mathf.Max(cz - reach, 0); z <= Mathf.Min(cz + reach, WorldLayout.Resolution - 1); z++)
        {
            for (int x = Mathf.Max(cx - reach, 0); x <= Mathf.Min(cx + reach, WorldLayout.Resolution - 1); x++)
            {
                float distance = Vector2.Distance(new Vector2(ToWorld(x), ToWorld(z)), centre);
                heights[z, x] = Mathf.Lerp(heights[z, x], height, 1f - WorldLayout.Step(flatRadius, outerRadius, distance));
            }
        }
    }

    /// <summary>A winding route from the airport's west side up to the observatory.</summary>
    static Vector2[] MountainRoute()
    {
        Vector2 start = new Vector2(-660f, 300f);
        Vector2 end = new Vector2(WorldLayout.Observatory.x, WorldLayout.Observatory.z);
        Vector2 along = (end - start).normalized, across = new Vector2(-along.y, along.x);
        float length = Vector2.Distance(start, end);

        var route = new List<Vector2>();
        const int legs = 14;
        for (int i = 0; i <= legs; i++)
        {
            float t = i / (float)legs;
            // Swings side to side like a real mountain road, straightening out at both ends.
            float swing = Mathf.Sin(t * Mathf.PI * 5f) * 260f * Mathf.Sin(t * Mathf.PI);
            route.Add(start + along * (length * t) + across * swing);
        }
        return route.ToArray();
    }

    /// <summary>
    /// Cuts a level roadbed through the terrain along a route and returns the
    /// road's centre line. The profile is smoothed so the road climbs evenly
    /// instead of following every bump.
    /// </summary>
    static List<Vector3> CarveRoad(IList<Vector2> waypoints, float startHeight, float endHeight)
    {
        // Round the corners, then resample at an even spacing.
        var smooth = new List<Vector2>(waypoints);
        for (int pass = 0; pass < 2; pass++)
        {
            var next = new List<Vector2> { smooth[0] };
            for (int i = 0; i < smooth.Count - 1; i++)
            {
                next.Add(Vector2.Lerp(smooth[i], smooth[i + 1], 0.25f));
                next.Add(Vector2.Lerp(smooth[i], smooth[i + 1], 0.75f));
            }
            next.Add(smooth[smooth.Count - 1]);
            smooth = next;
        }

        const float spacing = 30f;
        var points = new List<Vector2>();
        for (int i = 0; i < smooth.Count - 1; i++)
        {
            int steps = Mathf.Max(1, Mathf.RoundToInt(Vector2.Distance(smooth[i], smooth[i + 1]) / spacing));
            for (int s = 0; s < steps; s++) points.Add(Vector2.Lerp(smooth[i], smooth[i + 1], s / (float)steps));
        }
        points.Add(smooth[smooth.Count - 1]);

        int count = points.Count;
        var profile = new float[count];
        for (int i = 0; i < count; i++) profile[i] = Ground(points[i].x, points[i].y);
        profile[0] = startHeight;
        profile[count - 1] = endHeight;
        for (int pass = 0; pass < 8; pass++)
        {
            var averaged = (float[])profile.Clone();
            for (int i = 1; i < count - 1; i++)
            {
                float sum = 0f;
                int samples = 0;
                for (int k = Mathf.Max(i - 6, 0); k <= Mathf.Min(i + 6, count - 1); k++) { sum += profile[k]; samples++; }
                averaged[i] = sum / samples;
            }
            profile = averaged;
        }

        // Each terrain cell takes the height of whichever road point claims it most strongly.
        const float flat = 45f, outer = 190f;
        float cellSize = WorldLayout.WorldSize / (WorldLayout.Resolution - 1);
        int reach = Mathf.CeilToInt(outer / cellSize) + 1;
        var weight = new Dictionary<int, float>();
        var target = new Dictionary<int, float>();
        for (int i = 0; i < count; i++)
        {
            int cx = Mathf.RoundToInt(ToCell(points[i].x)), cz = Mathf.RoundToInt(ToCell(points[i].y));
            for (int z = Mathf.Max(cz - reach, 0); z <= Mathf.Min(cz + reach, WorldLayout.Resolution - 1); z++)
            {
                for (int x = Mathf.Max(cx - reach, 0); x <= Mathf.Min(cx + reach, WorldLayout.Resolution - 1); x++)
                {
                    float distance = Vector2.Distance(new Vector2(ToWorld(x), ToWorld(z)), points[i]);
                    float w = 1f - WorldLayout.Step(flat, outer, distance);
                    int key = z * WorldLayout.Resolution + x;
                    if (w <= 0f || (weight.TryGetValue(key, out float existing) && existing >= w)) continue;
                    weight[key] = w;
                    target[key] = profile[i];
                }
            }
        }
        foreach (KeyValuePair<int, float> entry in weight)
        {
            int z = entry.Key / WorldLayout.Resolution, x = entry.Key % WorldLayout.Resolution;
            heights[z, x] = Mathf.Lerp(heights[z, x], target[entry.Key], entry.Value);
        }

        var line = new List<Vector3>(count);
        for (int i = 0; i < count; i++) line.Add(new Vector3(points[i].x, profile[i], points[i].y));
        return line;
    }

    static void ApplyHeights(Terrain terrain)
    {
        int res = WorldLayout.Resolution;
        var normalised = new float[res, res];
        for (int z = 0; z < res; z++)
            for (int x = 0; x < res; x++)
                normalised[z, x] = Mathf.Clamp01((heights[z, x] + WorldLayout.SeaFloor) / WorldLayout.MaxHeight);

        TerrainData data = terrain.terrainData;
        data.heightmapResolution = res;
        data.size = new Vector3(WorldLayout.WorldSize, WorldLayout.MaxHeight, WorldLayout.WorldSize);
        data.SetHeights(0, 0, normalised);
        terrain.transform.position = new Vector3(-WorldLayout.WorldSize * 0.5f, -WorldLayout.SeaFloor, -WorldLayout.WorldSize * 0.5f);
        EditorUtility.SetDirty(data);
    }

    /// <summary>Terrain changed under the aircraft's start positions, so make sure none of them begins inside a hill.</summary>
    static void LiftAircraftClear()
    {
        foreach (EnemyPlane enemy in Object.FindObjectsByType<EnemyPlane>(FindObjectsSortMode.None))
        {
            Vector3 p = enemy.transform.position;
            float floor = Ground(p.x, p.z) + 600f;
            if (p.y < floor) enemy.transform.position = new Vector3(p.x, floor, p.z);
        }
    }

    // --------------------------------------------------------------- materials

    static Texture2D Facade(string name, Color wall, Color glassDark, Color glassLight, float litChance, float windowWidth, float windowHeight, int seed)
    {
        // An 8 x 8 grid of windows; one tile covers 32 m x 28 m of wall.
        const int cells = 8;
        return GraphicsBuilder.Tex(name, 256, false, (u, v, x, y) =>
        {
            int cx = (int)(u * cells), cy = (int)(v * cells);
            float fu = u * cells % 1f, fv = v * cells % 1f;
            Color c = wall * (0.92f + Mathf.PerlinNoise(u * 50f + seed, v * 50f) * 0.14f);
            bool inWindow = Mathf.Abs(fu - 0.5f) < windowWidth * 0.5f && Mathf.Abs(fv - 0.55f) < windowHeight * 0.5f;
            if (inWindow)
            {
                float pick = GraphicsBuilder.Hash(cx, cy, seed);
                c = pick < litChance ? new Color(0.98f, 0.9f, 0.62f) : Color.Lerp(glassDark, glassLight, GraphicsBuilder.Hash(cx, cy, seed + 5) * fv);
            }
            c.a = 1f;
            return c;
        });
    }

    /// <summary>Loads one of the downloaded ambientCG maps, fixing its import settings the first time.</summary>
    static Texture2D ThirdParty(string asset, bool normalMap)
    {
        string path = $"Assets/ThirdParty/ambientCG/{asset}/{asset}_1K-JPG_{(normalMap ? "NormalGL" : "Color")}.jpg";
        var importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer == null) return null;

        TextureImporterType wanted = normalMap ? TextureImporterType.NormalMap : TextureImporterType.Default;
        if (importer.textureType != wanted || importer.anisoLevel != 8)
        {
            importer.textureType = wanted;
            importer.anisoLevel = 8;
            importer.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    /// <summary>
    /// A material from a downloaded photo texture, or null if the files aren't there.
    /// tileMetres is how much real wall one copy of the picture covers; walls are
    /// laid out for a 32 x 28 m tile, so the material rescales to match.
    /// </summary>
    static Material Photo(string name, string asset, Color tint, float smoothness, Vector2 tileMetres, bool litWindows = false)
    {
        Texture2D color = ThirdParty(asset, false);
        if (color == null) return null;

        Material m = Tinted(name, color, tint, smoothness);
        Texture2D normal = ThirdParty(asset, true);
        if (normal != null)
        {
            m.SetTexture("_BumpMap", normal);
            m.EnableKeyword("_NORMALMAP");
        }
        m.SetTextureScale("_BaseMap", new Vector2(32f / tileMetres.x, 28f / tileMetres.y));
        if (litWindows)
        {
            // The picture is a building at night: use it as its own glow so the rooms stay lit in daylight.
            m.SetTexture("_EmissionMap", color);
            m.SetColor("_EmissionColor", Color.white * 0.9f);
            m.EnableKeyword("_EMISSION");
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.BakedEmissive;
        }
        EditorUtility.SetDirty(m);
        return m;
    }

    static Material Tinted(string name, Texture2D texture, Color tint, float smoothness = 0.2f)
    {
        return GraphicsBuilder.Mat(name, "Universal Render Pipeline/Lit", tint, texture, smoothness);
    }

    static Material Plain(string name, Color color, float smoothness = 0.15f)
    {
        return GraphicsBuilder.Mat(name, "Universal Render Pipeline/Lit", color, null, smoothness);
    }

    static void BuildMaterials()
    {
        Texture2D roadTex = GraphicsBuilder.Tex("RoadTex", 128, false, (u, v, x, y) =>
        {
            float grit = 0.2f + Mathf.PerlinNoise(u * 40f, v * 40f) * 0.06f;
            Color c = new Color(grit, grit, grit * 1.03f);
            if (Mathf.Abs(u - 0.06f) < 0.012f || Mathf.Abs(u - 0.94f) < 0.012f) c = new Color(0.85f, 0.85f, 0.82f);
            if (Mathf.Abs(u - 0.5f) < 0.014f && v < 0.5f) c = new Color(0.9f, 0.8f, 0.3f);
            return c;
        });
        Texture2D concreteTex = GraphicsBuilder.Tex("ConcreteTex", 128, false, (u, v, x, y) =>
        {
            float n = 0.52f + GraphicsBuilder.TileFbm(u, v, 3f, 4f, 3) * 0.14f;
            // Expansion joints every quarter tile.
            if (x % 32 == 0 || y % 32 == 0) n *= 0.82f;
            return new Color(n, n, n * 0.98f);
        });
        Texture2D gravelTex = GraphicsBuilder.Tex("RoofTex", 64, false, (u, v, x, y) =>
        {
            float n = 0.2f + GraphicsBuilder.TileNoise(u, v, 12f, 9f) * 0.12f;
            return new Color(n, n, n * 1.05f);
        });
        Texture2D corrugatedTex = GraphicsBuilder.Tex("CorrugatedTex", 64, false, (u, v, x, y) =>
        {
            float rib = 0.72f + Mathf.Sin(u * Mathf.PI * 16f) * 0.12f;
            return new Color(rib, rib, rib);
        });
        Texture2D tileTex = GraphicsBuilder.Tex("RoofTileTex", 64, false, (u, v, x, y) =>
        {
            float course = 0.8f + (y % 8 < 1 ? -0.25f : 0f) + GraphicsBuilder.TileNoise(u, v, 10f, 3f) * 0.15f;
            return new Color(course, course, course);
        });

        Texture2D glass = Facade("FacadeGlassTex", new Color(0.30f, 0.36f, 0.42f), new Color(0.10f, 0.22f, 0.36f), new Color(0.45f, 0.65f, 0.82f), 0.06f, 0.9f, 0.86f, 3);
        Texture2D office = Facade("FacadeOfficeTex", new Color(0.66f, 0.65f, 0.62f), new Color(0.08f, 0.12f, 0.18f), new Color(0.3f, 0.42f, 0.55f), 0.14f, 0.66f, 0.5f, 7);
        Texture2D flat = Facade("FacadeFlatsTex", new Color(0.78f, 0.72f, 0.62f), new Color(0.1f, 0.12f, 0.16f), new Color(0.3f, 0.36f, 0.44f), 0.22f, 0.4f, 0.48f, 13);

        road = Tinted("WorldRoad", roadTex, Color.white, 0.1f);
        concrete = Tinted("WorldConcrete", concreteTex, Color.white, 0.1f);
        roofGravel = Tinted("WorldRoof", gravelTex, Color.white, 0.05f);
        corrugated = Tinted("WorldCorrugated", corrugatedTex, new Color(0.8f, 0.82f, 0.85f), 0.3f);
        steel = Plain("WorldSteel", new Color(0.45f, 0.47f, 0.5f), 0.4f);
        rust = Plain("WorldRust", new Color(0.42f, 0.28f, 0.2f), 0.1f);
        white = Plain("WorldWhite", new Color(0.9f, 0.9f, 0.88f), 0.3f);
        darkGlass = Plain("WorldDarkGlass", new Color(0.08f, 0.14f, 0.2f), 0.85f);
        pole = Plain("WorldPole", new Color(0.3f, 0.31f, 0.33f), 0.2f);
        paint = GraphicsBuilder.Mat("WorldPaint", "Universal Render Pipeline/Unlit", new Color(0.92f, 0.92f, 0.88f));
        lamp = GraphicsBuilder.Mat("WorldLamp", "Universal Render Pipeline/Unlit", new Color(1f, 0.9f, 0.6f));
        hullRed = Plain("WorldHullRed", new Color(0.45f, 0.12f, 0.1f), 0.2f);
        hullBlack = Plain("WorldHullBlack", new Color(0.1f, 0.11f, 0.13f), 0.2f);
        craneBlue = Plain("WorldCrane", new Color(0.15f, 0.35f, 0.6f), 0.3f);
        trunk = Plain("WorldTrunk", new Color(0.26f, 0.19f, 0.13f), 0.05f);
        treeConifer = Plain("WorldConifer", new Color(0.09f, 0.22f, 0.11f), 0.05f);
        treeLeaf = Plain("WorldLeaf", new Color(0.2f, 0.36f, 0.14f), 0.05f);
        foreach (Material instanced in new[] { trunk, treeConifer, treeLeaf }) instanced.enableInstancing = true;

        towers = new[]
        {
            Tinted("WorldTowerBlue", glass, Color.white, 0.75f),
            Tinted("WorldTowerGreen", glass, new Color(0.75f, 1f, 0.9f), 0.75f),
            Tinted("WorldTowerBronze", glass, new Color(1f, 0.82f, 0.62f), 0.7f),
        };
        offices = new[]
        {
            Tinted("WorldOfficeGrey", office, Color.white),
            Tinted("WorldOfficeTan", office, new Color(1f, 0.92f, 0.78f)),
            Tinted("WorldOfficeBlue", office, new Color(0.8f, 0.88f, 1f)),
        };
        flats = new[]
        {
            Tinted("WorldFlatsCream", flat, Color.white),
            Tinted("WorldFlatsBrick", flat, new Color(0.95f, 0.66f, 0.55f)),
            Tinted("WorldFlatsGrey", flat, new Color(0.8f, 0.82f, 0.85f)),
        };
        houseWalls = new[]
        {
            Plain("WorldHouseCream", new Color(0.86f, 0.82f, 0.72f)),
            Plain("WorldHouseWhite", new Color(0.9f, 0.9f, 0.88f)),
            Plain("WorldHouseSand", new Color(0.76f, 0.66f, 0.52f)),
        };
        houseRoofs = new[]
        {
            Tinted("WorldRoofRed", tileTex, new Color(0.62f, 0.25f, 0.18f), 0.1f),
            Tinted("WorldRoofSlate", tileTex, new Color(0.3f, 0.32f, 0.36f), 0.1f),
            Tinted("WorldRoofBrown", tileTex, new Color(0.42f, 0.3f, 0.22f), 0.1f),
        };
        containers = new[]
        {
            Tinted("WorldBoxRed", corrugatedTex, new Color(0.7f, 0.18f, 0.14f)),
            Tinted("WorldBoxBlue", corrugatedTex, new Color(0.16f, 0.32f, 0.62f)),
            Tinted("WorldBoxGreen", corrugatedTex, new Color(0.18f, 0.48f, 0.3f)),
            Tinted("WorldBoxOrange", corrugatedTex, new Color(0.85f, 0.5f, 0.15f)),
            Tinted("WorldBoxGrey", corrugatedTex, new Color(0.6f, 0.62f, 0.64f)),
        };
        // Photo textures from ambientCG replace the generated ones wherever the files are present.
        Material glassA = Photo("WorldPhotoGlassA", "Facade001", new Color(0.8f, 0.92f, 1f), 0.55f, new Vector2(24f, 35f));
        Material glassB = Photo("WorldPhotoGlassB", "Facade005", new Color(0.78f, 1f, 0.95f), 0.55f, new Vector2(32f, 36f));
        Material glassWarm = Photo("WorldPhotoGlassWarm", "Facade001", new Color(1f, 0.86f, 0.7f), 0.5f, new Vector2(24f, 35f));
        Material night = Photo("WorldPhotoNight", "Facade009", new Color(0.5f, 0.55f, 0.6f), 0.85f, new Vector2(48f, 56f), true);
        if (glassA != null && glassB != null && night != null) towers = new[] { glassA, glassB, glassWarm, night };

        Material officeWhite = Photo("WorldPhotoOffice", "Facade006", Color.white, 0.3f, new Vector2(28f, 28f));
        Material officeSand = Photo("WorldPhotoOfficeSand", "Facade006", new Color(1f, 0.9f, 0.76f), 0.3f, new Vector2(28f, 28f));
        Material officeSlate = Photo("WorldPhotoOfficeSlate", "Facade006", new Color(0.72f, 0.78f, 0.86f), 0.3f, new Vector2(28f, 28f));
        if (officeWhite != null) offices = new[] { officeWhite, officeSand, officeSlate };

        Texture2D roadPhoto = ThirdParty("Road007", false);
        if (roadPhoto != null)
        {
            road = Tinted("WorldRoad", roadPhoto, Color.white, 0.15f);
            Texture2D roadNormal = ThirdParty("Road007", true);
            if (roadNormal != null)
            {
                road.SetTexture("_BumpMap", roadNormal);
                road.EnableKeyword("_NORMALMAP");
                EditorUtility.SetDirty(road);
            }
        }

        cars = new[]
        {
            Plain("WorldCarWhite", new Color(0.85f, 0.85f, 0.85f), 0.6f),
            Plain("WorldCarRed", new Color(0.6f, 0.12f, 0.1f), 0.6f),
            Plain("WorldCarBlue", new Color(0.12f, 0.2f, 0.45f), 0.6f),
            Plain("WorldCarDark", new Color(0.12f, 0.12f, 0.14f), 0.6f),
        };
    }

    // ------------------------------------------------------------ street props

    /// <summary>A street light: pole, arm reaching over the road, and a glowing lamp head.</summary>
    static void StreetLight(string kit, Vector3 foot, Vector3 towardRoad)
    {
        MeshKit k = Kit(kit);
        const float height = 9f;
        k.Box(pole, null, foot, 0.3f, 0.3f, height);
        Vector3 armCentre = foot + Vector3.up * (height - 0.2f) + towardRoad * 1.4f;
        float yaw = Mathf.Atan2(towardRoad.x, towardRoad.z) * Mathf.Rad2Deg;
        k.Box(pole, null, armCentre, 0.2f, 3f, 0.2f, yaw);
        k.Box(lamp, lamp, foot + Vector3.up * (height - 0.45f) + towardRoad * 2.6f, 0.5f, 1.1f, 0.25f, yaw);
    }

    /// <summary>Street lights down both sides of a road, staggered.</summary>
    static void Lights(string kit, IList<Vector3> line, float offset, float spacing)
    {
        float sinceLast = spacing * 0.5f;
        bool left = false;
        for (int i = 1; i < line.Count; i++)
        {
            sinceLast += Vector3.Distance(line[i], line[i - 1]);
            if (sinceLast < spacing) continue;
            sinceLast = 0f;
            Vector3 ahead = line[i] - line[i - 1];
            ahead.y = 0f;
            Vector3 side = Vector3.Cross(Vector3.up, ahead.normalized) * (left ? -1f : 1f);
            Vector3 foot = line[i] + side * offset;
            foot.y = Ground(foot.x, foot.z);
            StreetLight(kit, foot, -side);
            left = !left;
        }
    }

    static void Car(string kit, Vector3 position, float yaw)
    {
        Material body = Pick(cars);
        MeshKit k = Kit(kit);
        k.Box(body, body, position, 1.9f, 4.4f, 0.9f, yaw);
        k.Box(darkGlass, body, position + Vector3.up * 0.9f, 1.7f, 2.3f, 0.6f, yaw);
    }

    static void Tree(float wx, float wz, int prototype, float scale)
    {
        trees.Add(new TreeInstance
        {
            position = new Vector3(wx / WorldLayout.WorldSize + 0.5f, 0f, wz / WorldLayout.WorldSize + 0.5f),
            prototypeIndex = prototype,
            widthScale = scale * Rand(0.85f, 1.15f),
            heightScale = scale * Rand(0.85f, 1.25f),
            rotation = Rand(0f, Mathf.PI * 2f),
            color = Color.white,
            lightmapColor = Color.white,
        });
    }

    // -------------------------------------------------------------------- city

    static void BuildCity(List<string> log)
    {
        Rect city = WorldLayout.City;
        float y = WorldLayout.CityHeight;
        const float blockX = 170f, blockZ = 140f, street = 16f, avenue = 26f;
        int columns = Mathf.RoundToInt(city.width / blockX), rows = Mathf.RoundToInt(city.height / blockZ);
        Vector2 centre = city.center;
        int towerCount = 0, midCount = 0, houseCount = 0;

        // Street grid. Every fifth line is a wider avenue; north-south roads sit a touch higher so crossings don't flicker.
        for (int i = 0; i <= columns; i++)
        {
            float x = city.xMin + i * blockX;
            float width = i % 5 == 0 ? avenue : street;
            var line = new List<Vector3> { new Vector3(x, y, city.yMin), new Vector3(x, y, city.yMax) };
            Kit("City Roads").Strip(road, line, width, 24f, 0.30f);
            CityLightsAndCars(line, width, i);
        }
        for (int j = 0; j <= rows; j++)
        {
            float z = city.yMin + j * blockZ;
            float width = j % 5 == 0 ? avenue : street;
            var line = new List<Vector3> { new Vector3(city.xMin, y, z), new Vector3(city.xMax, y, z) };
            Kit("City Roads").Strip(road, line, width, 24f, 0.22f);
            CityLightsAndCars(line, width, j + 100);
        }

        for (int i = 0; i < columns; i++)
        {
            for (int j = 0; j < rows; j++)
            {
                float x0 = city.xMin + i * blockX + avenue * 0.5f + 5f, x1 = city.xMin + (i + 1) * blockX - avenue * 0.5f - 5f;
                float z0 = city.yMin + j * blockZ + avenue * 0.5f + 5f, z1 = city.yMin + (j + 1) * blockZ - avenue * 0.5f - 5f;
                Vector2 mid = new Vector2((x0 + x1) * 0.5f, (z0 + z1) * 0.5f);
                // 0 at the centre of downtown, 1 at the city limits.
                float fromCentre = Mathf.Max(Mathf.Abs(mid.x - centre.x) / (city.width * 0.5f), Mathf.Abs(mid.y - centre.y) / (city.height * 0.5f));
                string kit = "City " + (mid.x < centre.x ? "West" : "East") + (mid.y < centre.y ? " South" : " North");
                float roll = Rand(0f, 1f);

                if (roll < 0.05f)
                {
                    // A park.
                    for (int t = 0; t < 26; t++) Tree(Rand(x0, x1), Rand(z0, z1), 1, Rand(0.8f, 1.3f));
                }
                else if (fromCentre < 0.3f)
                {
                    towerCount += Downtown(kit, x0, x1, z0, z1, y, fromCentre);
                }
                else if (fromCentre < 0.66f)
                {
                    midCount += Midtown(kit, x0, x1, z0, z1, y);
                }
                else
                {
                    houseCount += Suburb(kit, x0, x1, z0, z1, y);
                }
            }
        }

        log.Add($"city: {towerCount} towers, {midCount} mid-rise blocks, {houseCount} houses");
    }

    static void CityLightsAndCars(List<Vector3> line, float width, int seed)
    {
        Lights("City Lights", line, width * 0.5f + 1.5f, 55f);

        Vector3 along = (line[1] - line[0]).normalized;
        Vector3 side = Vector3.Cross(Vector3.up, along);
        float length = Vector3.Distance(line[0], line[1]);
        float yaw = Mathf.Atan2(along.x, along.z) * Mathf.Rad2Deg;
        for (float d = Rand(20f, 90f); d < length; d += Rand(35f, 140f))
        {
            // Drive on the right: each direction keeps to its own side.
            bool forward = rng.Next(2) == 0;
            Vector3 p = line[0] + along * d + side * (forward ? 1f : -1f) * (width * 0.22f);
            Car("City Traffic", p + Vector3.up * 0.35f, forward ? yaw : yaw + 180f);
        }
    }

    static int Downtown(string kit, float x0, float x1, float z0, float z1, float y, float fromCentre)
    {
        int count = rng.Next(1, 3);
        float slot = (x1 - x0) / count;
        for (int n = 0; n < count; n++)
        {
            float width = Mathf.Min(slot - 8f, Rand(36f, 60f)), depth = Rand(40f, z1 - z0 - 6f);
            // Tallest in the very middle of downtown.
            float height = Mathf.Lerp(250f, 90f, fromCentre / 0.3f) * Rand(0.55f, 1f);
            Vector3 foot = new Vector3(x0 + slot * (n + 0.5f), y, (z0 + z1) * 0.5f + Rand(-6f, 6f));
            Material skin = Pick(towers);
            MeshKit k = Kit(kit);

            // A low podium, the main shaft, and sometimes a narrower crown.
            k.Box(Pick(offices), roofGravel, foot, width + 6f, depth + 6f, 9f, 0f, 32f, 28f, Rand(0f, 1f));
            bool crowned = rng.Next(3) > 0;
            float shaft = crowned ? height * Rand(0.6f, 0.8f) : height;
            k.Box(skin, roofGravel, foot + Vector3.up * 9f, width, depth, shaft - 9f, 0f, 32f, 28f, Rand(0f, 1f));
            if (crowned)
            {
                k.Box(skin, roofGravel, foot + Vector3.up * shaft, width * 0.7f, depth * 0.7f, height - shaft, 0f, 32f, 28f, Rand(0f, 1f));
                // Rooftop mast.
                if (rng.Next(2) == 0) k.Box(pole, null, foot + Vector3.up * height, 1.2f, 1.2f, Rand(15f, 40f));
            }
            Solid("City", foot, width + 6f, depth + 6f, height);
        }
        return count;
    }

    static int Midtown(string kit, float x0, float x1, float z0, float z1, float y)
    {
        int built = 0;
        float halfX = (x1 - x0) * 0.5f, halfZ = (z1 - z0) * 0.5f;
        for (int a = 0; a < 2; a++)
        {
            for (int b = 0; b < 2; b++)
            {
                if (rng.Next(8) == 0) continue;
                float width = halfX - Rand(6f, 14f), depth = halfZ - Rand(6f, 12f);
                float height = Rand(16f, 70f);
                Vector3 foot = new Vector3(x0 + halfX * (a + 0.5f), y, z0 + halfZ * (b + 0.5f));
                Material skin = height > 40f ? Pick(offices) : Pick(flats);
                Kit(kit).Box(skin, roofGravel, foot, width, depth, height, 0f, 32f, 28f, Rand(0f, 1f));
                // Plant room on the roof.
                Kit(kit).Box(corrugated, roofGravel, foot + new Vector3(Rand(-5f, 5f), height, Rand(-5f, 5f)), 8f, 6f, 3.5f);
                Solid("City", foot, width, depth, height);
                built++;
            }
        }
        return built;
    }

    static int Suburb(string kit, float x0, float x1, float z0, float z1, float y)
    {
        int built = 0;
        const int lotsX = 4, lotsZ = 2;
        float lotX = (x1 - x0) / lotsX, lotZ = (z1 - z0) / lotsZ;
        for (int a = 0; a < lotsX; a++)
        {
            for (int b = 0; b < lotsZ; b++)
            {
                Vector3 foot = new Vector3(x0 + lotX * (a + 0.5f) + Rand(-3f, 3f), y, z0 + lotZ * (b + 0.5f) + Rand(-4f, 4f));
                float width = Rand(10f, 15f), depth = Rand(9f, 13f), height = Rand(5.5f, 8.5f);
                float yaw = rng.Next(2) * 90f;
                Kit(kit).Box(Pick(houseWalls), null, foot, width, depth, height, yaw);
                Kit(kit).Gable(Pick(houseRoofs), foot + Vector3.up * height, width, depth, Rand(2.5f, 4f), yaw);
                // Garden trees.
                for (int t = 0; t < 2; t++) Tree(foot.x + Rand(-14f, 14f), foot.z + Rand(12f, 22f) * (rng.Next(2) == 0 ? 1f : -1f), rng.Next(2), Rand(0.6f, 0.9f));
                built++;
            }
        }
        return built;
    }

    // ----------------------------------------------------- port and refinery

    static void BuildPort(List<string> log)
    {
        Rect port = WorldLayout.Port;
        float y = WorldLayout.PortHeight, quay = WorldLayout.QuayZ;
        MeshKit k = Kit("Port");

        // Quay wall along the whole waterfront; it also hides the terrain's soft edge.
        k.Box(concrete, concrete, new Vector3(port.center.x, -6f, quay - 22f), port.width + 300f, 60f, y + 6f, 0f, 20f, 20f);
        // Link road from the city down to the quayside road.
        k.Strip(road, new List<Vector3> { new Vector3(5200f, WorldLayout.CityHeight, WorldLayout.City.yMin), new Vector3(5200f, y, port.yMax - 90f) }, 18f, 24f, 0.8f);
        var quayRoad = new List<Vector3> { new Vector3(port.xMin + 40f, y, port.yMax - 90f), new Vector3(port.xMax - 40f, y, port.yMax - 90f) };
        k.Strip(road, quayRoad, 16f, 24f, 0.6f);
        Lights("Port", quayRoad, 10f, 60f);

        int stacks = Containers(port, y);
        Warehouses(port, y);

        // Container terminal: finger piers, gantry cranes and ships alongside.
        float[] piers = { 5150f, 5720f, 6290f, 6800f };
        for (int i = 0; i < piers.Length; i++)
        {
            k.Box(concrete, concrete, new Vector3(piers[i], -6f, quay - 250f), 46f, 420f, y + 5.5f, 0f, 20f, 20f);
            Crane(new Vector3(piers[i] - 60f, y, quay - 18f));
            Crane(new Vector3(piers[i] + 60f, y, quay - 18f));
            if (i % 2 == 0) CargoShip(new Vector3(piers[i] + 48f, 0f, quay - 240f));
        }

        int tanks = Refinery(port, y);
        log.Add($"port: {stacks} container stacks, {piers.Length} piers, 8 cranes, 3 ships; refinery: {tanks} storage tanks");
    }

    static int Containers(Rect port, float y)
    {
        // Rows of stacked boxes with truck lanes between the blocks.
        const float length = 12.2f, width = 2.5f, unit = 2.6f;
        int stacks = 0;
        for (float x = 4950f; x < port.xMax - 60f; x += length + 1.2f)
        {
            // A cross lane every dozen stacks.
            if (Mathf.RoundToInt((x - 4950f) / (length + 1.2f)) % 12 == 11) continue;
            int row = 0;
            for (float z = WorldLayout.QuayZ + 70f; z < port.yMax - 130f; z += width + 0.4f)
            {
                if (row++ % 8 >= 6) continue;
                if (rng.Next(100) < 30) continue;
                Material skin = Pick(containers);
                Kit("Port Containers").Box(skin, skin, new Vector3(x, y, z), length, width, unit * rng.Next(1, 5), 0f, 3f, 2.6f);
                stacks++;
            }
        }
        return stacks;
    }

    static void Warehouses(Rect port, float y)
    {
        for (float x = 4980f; x < port.xMax - 80f; x += 150f)
        {
            Vector3 foot = new Vector3(x, y, port.yMax - 45f);
            Kit("Port").Box(corrugated, null, foot, 120f, 50f, 13f, 0f, 6f, 6f);
            Kit("Port").Gable(corrugated, foot + Vector3.up * 13f, 50f, 120f, 6f, 90f, 1f);
            Solid("Port", foot, 120f, 50f, 18f);
        }
    }

    /// <summary>Ship-to-shore gantry crane: four legs straddling the quay with a long boom out over the water.</summary>
    static void Crane(Vector3 foot)
    {
        MeshKit k = Kit("Port");
        const float span = 28f, legHeight = 48f;
        foreach (float sx in new[] { -1f, 1f })
            foreach (float sz in new[] { -1f, 1f })
                k.Box(craneBlue, null, foot + new Vector3(sx * span * 0.5f, 0f, sz * 9f), 2.2f, 2.2f, legHeight);
        Vector3 top = foot + Vector3.up * legHeight;
        k.Box(craneBlue, craneBlue, top, span + 4f, 22f, 4f);
        // The boom reaches south over the ship; a shorter tail balances it landward.
        k.Box(craneBlue, craneBlue, top + new Vector3(0f, 4f, -26f), 5f, 96f, 3.5f);
        k.Box(white, white, top + new Vector3(0f, 7.5f, 4f), 8f, 12f, 6f);
        k.Box(craneBlue, null, top + new Vector3(0f, 4f, -2f), 1.5f, 1.5f, 22f);
        Solid("Port", foot + new Vector3(0f, 0f, -26f), span + 4f, 96f, legHeight + 10f);
    }

    static void CargoShip(Vector3 waterline)
    {
        MeshKit k = Kit("Port Ships");
        const float length = 230f, beam = 32f;
        k.Box(hullRed, null, waterline + Vector3.down * 4f, beam, length, 8f);
        k.Box(hullBlack, concrete, waterline + Vector3.up * 4f, beam, length, 9f);
        // Tapered bow, as a narrower block.
        k.Box(hullBlack, concrete, waterline + new Vector3(0f, -4f, -length * 0.5f - 12f), beam * 0.55f, 26f, 17f);
        // Accommodation block and funnel at the stern.
        Vector3 deck = waterline + Vector3.up * 13f;
        k.Box(white, white, deck + new Vector3(0f, 0f, length * 0.5f - 22f), beam - 4f, 22f, 24f, 0f, 6f, 3.5f);
        k.Box(hullRed, hullBlack, deck + new Vector3(0f, 24f, length * 0.5f - 14f), 6f, 8f, 9f);

        for (float z = -length * 0.5f + 18f; z < length * 0.5f - 44f; z += 13.4f)
            for (float x = -beam * 0.5f + 3f; x < beam * 0.5f - 2f; x += 2.9f)
            {
                Material skin = Pick(containers);
                Kit("Port Ships").Box(skin, skin, deck + new Vector3(x, 0f, z), 2.5f, 12.2f, 2.6f * rng.Next(2, 6), 0f, 2.6f, 2.6f);
            }
        Solid("Port", waterline + Vector3.down * 4f, beam, length + 30f, 40f);
    }

    static int Refinery(Rect port, float y)
    {
        MeshKit k = Kit("Refinery");
        int tanks = 0;

        // Tank farm: white floating-roof storage tanks in a grid, each inside a low bund wall.
        for (int i = 0; i < 6; i++)
        {
            for (int j = 0; j < 3; j++)
            {
                Vector3 foot = new Vector3(port.xMin + 90f + i * 96f, y, WorldLayout.QuayZ + 85f + j * 96f);
                float radius = Rand(26f, 34f), height = Rand(15f, 21f);
                k.Cylinder(white, white, foot, radius, height, 20);
                k.Dome(white, foot + Vector3.up * height, radius, 20, 3, 0.18f);
                // Spiral stair suggested by a dark band.
                k.Cylinder(steel, null, foot, radius + 0.3f, 1.2f, 20);
                Solid("Refinery", foot, radius * 1.7f, radius * 1.7f, height + 5f);
                tanks++;
            }
        }

        // Pressurised gas spheres on legs.
        for (int i = 0; i < 5; i++)
        {
            Vector3 foot = new Vector3(port.xMin + 760f + i * 42f, y, port.yMax - 250f);
            k.Dome(white, foot + Vector3.up * 19f, 14f, 14, 4, 1f, true);
            foreach (float a in new[] { 0f, 90f, 180f, 270f })
                k.Box(steel, null, foot + Quaternion.Euler(0f, a + 45f, 0f) * Vector3.forward * 11f, 1f, 1f, 16f);
            Solid("Refinery", foot, 28f, 28f, 34f);
        }

        // Process area: distillation columns, pipe racks, chimneys and a flare stack.
        Vector3 unit = new Vector3(port.xMin + 1010f, y, port.yMax - 330f);
        for (int i = 0; i < 8; i++)
        {
            Vector3 foot = unit + new Vector3(i % 4 * 38f, 0f, i / 4 * 46f);
            float radius = Rand(2.6f, 4.5f), height = Rand(42f, 82f);
            k.Cylinder(steel, steel, foot, radius, height, 12);
            k.Dome(steel, foot + Vector3.up * height, radius, 12, 3);
            // Access platforms ring the column.
            for (float h = 14f; h < height; h += 16f) k.Cylinder(rust, rust, foot + Vector3.up * h, radius + 1.6f, 0.6f, 12);
            Solid("Refinery", foot, radius * 2f + 3f, radius * 2f + 3f, height);
        }
        for (int i = 0; i < 4; i++)
        {
            Vector3 start = unit + new Vector3(-170f, 0f, -30f + i * 34f);
            k.Box(rust, rust, start + Vector3.up * 7f, 420f, 4f, 1.6f);
            for (float x = -200f; x <= 200f; x += 40f) k.Box(steel, null, start + new Vector3(x, 0f, 0f), 1f, 4f, 7f);
        }
        foreach (float offset in new[] { 190f, 240f })
        {
            Vector3 foot = unit + new Vector3(offset, 0f, 20f);
            k.Cylinder(concrete, concrete, foot, 5f, 115f, 14, 3.2f, 20f, 20f);
            Solid("Refinery", foot, 10f, 10f, 115f);
            Plume("Chimney Smoke", foot + Vector3.up * 117f, false);
        }
        Vector3 flare = unit + new Vector3(110f, 0f, 150f);
        k.Cylinder(steel, steel, flare, 1.4f, 125f, 8);
        k.Box(rust, null, flare, 6f, 6f, 30f);
        Solid("Refinery", flare, 6f, 6f, 125f);
        Plume("Flare", flare + Vector3.up * 126f, true);

        // Oil jetty with a tanker alongside.
        float jettyX = port.xMin + 420f;
        Kit("Port").Box(concrete, concrete, new Vector3(jettyX, -6f, WorldLayout.QuayZ - 190f), 18f, 300f, y + 5f, 0f, 20f, 20f);
        Kit("Port").Box(rust, rust, new Vector3(jettyX, y - 0.5f, WorldLayout.QuayZ - 190f), 3f, 300f, 1.2f);
        Tanker(new Vector3(jettyX + 34f, 0f, WorldLayout.QuayZ - 250f));
        return tanks;
    }

    static void Tanker(Vector3 waterline)
    {
        MeshKit k = Kit("Port Ships");
        const float length = 270f, beam = 42f;
        k.Box(hullRed, null, waterline + Vector3.down * 4f, beam, length, 7f);
        k.Box(hullBlack, rust, waterline + Vector3.up * 3f, beam, length, 7f);
        k.Box(hullBlack, rust, waterline + new Vector3(0f, -4f, -length * 0.5f - 14f), beam * 0.55f, 30f, 14f);
        Vector3 deck = waterline + Vector3.up * 10f;
        k.Box(white, white, deck + new Vector3(0f, 0f, length * 0.5f - 26f), beam - 6f, 26f, 26f, 0f, 6f, 3.5f);
        k.Box(hullRed, hullBlack, deck + new Vector3(0f, 26f, length * 0.5f - 18f), 7f, 9f, 10f);
        // Deck piping running fore and aft.
        k.Box(steel, steel, deck + new Vector3(0f, 0f, -20f), 5f, length - 90f, 2.2f);
        for (float z = -length * 0.5f + 30f; z < length * 0.5f - 60f; z += 36f) k.Box(steel, steel, deck + new Vector3(0f, 0f, z), beam - 8f, 2.5f, 1.6f);
        Solid("Port", waterline + Vector3.down * 4f, beam, length + 30f, 42f);
    }

    /// <summary>A never-ending column of smoke, or the burning tip of a flare stack.</summary>
    static void Plume(string name, Vector3 position, bool fire)
    {
        Material material = AssetDatabase.LoadAssetAtPath<Material>(fire ? "Assets/Generated/FxFire.mat" : "Assets/Generated/FxSmoke.mat");
        var go = new GameObject(name, typeof(ParticleSystem));
        go.transform.SetParent(root, false);
        go.transform.SetPositionAndRotation(position, Quaternion.Euler(-90f, 0f, 0f));

        var ps = go.GetComponent<ParticleSystem>();
        ParticleSystem.MainModule main = ps.main;
        main.loop = true;
        main.prewarm = true;
        main.startLifetime = fire ? new ParticleSystem.MinMaxCurve(0.6f, 1.1f) : new ParticleSystem.MinMaxCurve(9f, 14f);
        main.startSpeed = fire ? new ParticleSystem.MinMaxCurve(8f, 14f) : new ParticleSystem.MinMaxCurve(7f, 11f);
        main.startSize = fire ? new ParticleSystem.MinMaxCurve(7f, 12f) : new ParticleSystem.MinMaxCurve(10f, 16f);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startColor = fire ? new Color(1f, 0.75f, 0.3f, 1f) : new Color(0.75f, 0.75f, 0.75f, 0.5f);
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 80;

        ParticleSystem.EmissionModule emission = ps.emission;
        emission.rateOverTime = fire ? 30f : 4f;
        ParticleSystem.ShapeModule shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = fire ? 8f : 5f;
        shape.radius = fire ? 1f : 3f;

        // Smoke drifts downwind and spreads as it rises.
        if (!fire)
        {
            ParticleSystem.VelocityOverLifetimeModule velocity = ps.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.World;
            velocity.x = new ParticleSystem.MinMaxCurve(6f);
            velocity.y = new ParticleSystem.MinMaxCurve(0f);
            velocity.z = new ParticleSystem.MinMaxCurve(2f);
        }
        ParticleSystem.SizeOverLifetimeModule size = ps.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, fire
            ? new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0.2f))
            : new AnimationCurve(new Keyframe(0f, 0.4f), new Keyframe(1f, 4f)));
        ParticleSystem.ColorOverLifetimeModule color = ps.colorOverLifetime;
        color.enabled = true;
        var fade = new Gradient();
        fade.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(0f, 1f) });
        color.color = new ParticleSystem.MinMaxGradient(fade);

        var renderer = go.GetComponent<ParticleSystemRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
    }

    // ----------------------------------------------------------------- airport

    static void BuildAirport(List<string> log)
    {
        MeshKit k = Kit("Airport");
        const float ground = 0.12f, runwayTop = 1.04f;

        // Taxiway parallel to the runway, with links across to it, and a concrete apron on the east side.
        k.GroundRect(concrete, new Vector3(-125f, ground, 0f), 26f, 2300f, 0f, 20f);
        foreach (float z in new[] { -1120f, -400f, 400f, 1120f }) k.GroundRect(concrete, new Vector3(-75f, ground, z), 100f, 26f, 0f, 20f);
        k.GroundRect(concrete, new Vector3(440f, ground, -180f), 420f, 1300f, 0f, 20f);
        foreach (float z in new[] { -700f, -150f, 420f }) k.GroundRect(concrete, new Vector3(130f, ground, z), 210f, 28f, 0f, 20f);

        // Runway paint: threshold bars, centre line, edge lines and touchdown blocks.
        foreach (float end in new[] { -1f, 1f })
        {
            for (int i = 0; i < 8; i++) k.GroundRect(paint, new Vector3(-24.5f + i * 7f, runwayTop, end * 1195f), 3.5f, 45f);
            foreach (float d in new[] { 950f, 800f })
                foreach (float side in new[] { -1f, 1f })
                    k.GroundRect(paint, new Vector3(side * 11f, runwayTop, end * d), 7f, 30f);
        }
        for (float z = -1100f; z <= 1100f; z += 60f) k.GroundRect(paint, new Vector3(0f, runwayTop, z), 0.9f, 30f);
        foreach (float side in new[] { -1f, 1f }) k.GroundRect(paint, new Vector3(side * 28f, runwayTop, 0f), 0.9f, 2440f);

        // Edge lights down both sides and approach lights off each end.
        for (float z = -1230f; z <= 1230f; z += 60f)
            foreach (float side in new[] { -1f, 1f })
                k.Box(lamp, lamp, new Vector3(side * 32f, 0f, z), 0.8f, 0.8f, 1.4f);
        foreach (float end in new[] { -1f, 1f })
            for (int i = 1; i <= 9; i++)
                for (int n = -1; n <= 1; n++)
                    k.Box(lamp, lamp, new Vector3(n * 6f, 0f, end * (1250f + i * 40f)), 0.8f, 0.8f, 2f);

        // Passenger terminal with two boarding piers, facing the apron.
        Vector3 terminal = new Vector3(690f, 0f, -120f);
        k.Box(Pick(towers), roofGravel, terminal, 70f, 380f, 22f, 0f, 32f, 28f);
        k.Box(white, white, terminal + Vector3.up * 22f, 40f, 300f, 5f);
        foreach (float z in new[] { -110f, 110f }) k.Box(Pick(offices), roofGravel, terminal + new Vector3(-80f, 0f, z), 100f, 22f, 10f, 0f, 32f, 28f);
        Solid("Airport", terminal, 70f, 380f, 27f);

        // Control tower.
        Vector3 tower = new Vector3(610f, 0f, 250f);
        k.Cylinder(concrete, concrete, tower, 5.5f, 44f, 14, 4.5f, 20f, 20f);
        k.Cylinder(darkGlass, white, tower + Vector3.up * 44f, 8f, 6.5f, 14, 10f);
        k.Box(pole, null, tower + Vector3.up * 50.5f, 0.6f, 0.6f, 10f);
        Solid("Airport", tower, 18f, 18f, 60f);

        // Fuel depot and a row of maintenance sheds at the south end of the apron.
        for (int i = 0; i < 4; i++)
        {
            Vector3 foot = new Vector3(520f + i * 34f, 0f, -960f);
            k.Cylinder(white, white, foot, 13f, 11f, 14);
            Solid("Airport", foot, 24f, 24f, 12f);
        }
        for (int i = 0; i < 3; i++)
        {
            Vector3 foot = new Vector3(300f + i * 70f, 0f, 560f);
            k.Box(corrugated, null, foot, 56f, 44f, 12f, 0f, 6f, 6f);
            k.Gable(corrugated, foot + Vector3.up * 12f, 44f, 56f, 5f, 90f, 1f);
            Solid("Airport", foot, 56f, 44f, 17f);
        }

        ParkedAircraft();

        // Car park and access road joining the highway.
        k.GroundRect(road, new Vector3(800f, ground, -120f), 90f, 300f, 0f, 24f);
        for (int i = 0; i < 60; i++) Car("Airport", new Vector3(770f + rng.Next(6) * 11f, ground, -250f + rng.Next(40) * 6.5f), rng.Next(2) * 180f);

        log.Add("airport: taxiways, apron, terminal, control tower, runway markings and lights");
    }

    /// <summary>Jets lined up on the apron. Scenery only: they can't be locked or destroyed.</summary>
    static void ParkedAircraft()
    {
        Mesh jet = AssetDatabase.LoadAssetAtPath<Mesh>("Assets/Generated/JetMesh.asset");
        Material body = AssetDatabase.LoadAssetAtPath<Material>("Assets/Generated/JetEnemy.mat");
        Material canopy = AssetDatabase.LoadAssetAtPath<Material>("Assets/Generated/JetCanopy.mat");
        Material trim = AssetDatabase.LoadAssetAtPath<Material>("Assets/Generated/JetTrim.mat");
        if (jet == null || body == null) return;

        var parent = new GameObject("Parked Aircraft").transform;
        parent.SetParent(root, false);
        for (int i = 0; i < 6; i++)
        {
            var go = new GameObject("Parked Jet", typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(new Vector3(300f, 1.9f, -640f + i * 34f), Quaternion.Euler(0f, 90f, 0f));
            go.GetComponent<MeshFilter>().sharedMesh = jet;
            go.GetComponent<MeshRenderer>().sharedMaterials = new[] { body, canopy, trim };
            // Simple stand-in for the undercarriage.
            foreach (Vector3 leg in new[] { new Vector3(0f, 0f, 4.2f), new Vector3(-1.7f, 0f, -1.6f), new Vector3(1.7f, 0f, -1.6f) })
                Kit("Airport").Box(pole, null, go.transform.TransformPoint(leg) + Vector3.down * 1.9f, 0.3f, 0.3f, 1.4f);
        }
    }

    // ------------------------------------------------------------- observatory

    static void BuildObservatory(List<string> log)
    {
        Vector3 pad = WorldLayout.Observatory;
        MeshKit k = Kit("Observatory");

        // Main telescope: a drum with a white dome and a dark shutter slit.
        k.Cylinder(white, white, pad, 17f, 13f, 20);
        k.Dome(white, pad + Vector3.up * 13f, 17f, 20, 6);
        k.Box(darkGlass, darkGlass, pad + new Vector3(0f, 13f, 9f), 4.5f, 17f, 16f);
        Solid("Observatory", pad, 34f, 34f, 31f);

        // Two smaller domes and the support buildings.
        foreach (Vector3 offset in new[] { new Vector3(44f, 0f, 14f), new Vector3(-40f, 0f, -24f) })
        {
            k.Cylinder(white, white, pad + offset, 8f, 7f, 14);
            k.Dome(white, pad + offset + Vector3.up * 7f, 8f, 14, 4);
            Solid("Observatory", pad + offset, 16f, 16f, 16f);
        }
        k.Box(Pick(offices), roofGravel, pad + new Vector3(-8f, 0f, -44f), 46f, 16f, 8f, 0f, 32f, 28f);
        k.Box(concrete, roofGravel, pad + new Vector3(30f, 0f, -36f), 14f, 12f, 5f, 0f, 20f, 20f);

        // Radio mast with a dish, and a car park where the road arrives.
        Vector3 mast = pad + new Vector3(-52f, 0f, 26f);
        k.Box(steel, null, mast, 1.2f, 1.2f, 40f);
        k.Dome(white, mast + Vector3.up * 24f + Vector3.right * 2f, 4f, 10, 3, 0.5f);
        k.GroundRect(road, pad + new Vector3(8f, 0.3f, -66f), 60f, 26f, 0f, 24f);
        for (int i = 0; i < 6; i++) Car("Observatory", pad + new Vector3(-14f + i * 8f, 0.3f, -66f), 0f);

        log.Add($"observatory on the {pad.y:0} m summit at ({pad.x:0}, {pad.z:0})");
    }

    // ------------------------------------------------------------------ forest

    static void ScatterForest(List<string> log)
    {
        int before = trees.Count;
        float half = WorldLayout.WorldSize * 0.5f;
        float cell = WorldLayout.WorldSize / (WorldLayout.Resolution - 1);

        // Trees thin out with distance from the playable middle, so numbers stay reasonable.
        for (int i = 0; i < 260000 && trees.Count - before < 42000; i++)
        {
            float wx = Rand(-half + 500f, half - 500f), wz = Rand(-half + 500f, half - 500f);
            float fromCentre = Mathf.Sqrt(wx * wx + wz * wz);
            if (Rand(0f, 1f) < WorldLayout.Step(7000f, 16000f, fromCentre) * 0.85f) continue;

            float height = Ground(wx, wz);
            if (height < 4f || height > 900f) continue;
            if (WorldLayout.UrbanWeight(wx, wz) > 0f) continue;
            // Keep the airport, its approaches and the highway corridor clear.
            if (Mathf.Abs(wx) < 950f && Mathf.Abs(wz) < 2000f) continue;

            float slope = Mathf.Max(Mathf.Abs(Ground(wx + cell, wz) - height), Mathf.Abs(Ground(wx, wz + cell) - height)) / cell;
            if (slope > 0.55f) continue;

            // Same noise the terrain paint uses for woodland, so trees stand on the dark green patches.
            float u = wx / WorldLayout.WorldSize + 0.5f, v = wz / WorldLayout.WorldSize + 0.5f;
            float woods = WorldLayout.Step(0.45f, 0.58f, Mathf.PerlinNoise(u * 26f + 30f, v * 26f + 50f));
            if (Rand(0f, 1f) > woods * 0.95f + 0.02f) continue;

            // Conifers take over higher up.
            int prototype = Rand(0f, 1f) < WorldLayout.Step(150f, 500f, height) ? 0 : 1;
            Tree(wx, wz, prototype, Rand(0.9f, 1.5f));
        }
        log.Add($"{trees.Count - before} forest trees, {before} city trees");
    }

    static GameObject TreePrefab(string name, bool conifer)
    {
        var kit = new MeshKit();
        kit.Cylinder(trunk, null, Vector3.zero, 0.5f, conifer ? 4f : 6f, 6, 0.35f);
        if (conifer)
        {
            // Two stacked cones.
            kit.Cylinder(treeConifer, null, Vector3.up * 3f, 4.6f, 8f, 8, 1.6f);
            kit.Cylinder(treeConifer, null, Vector3.up * 9f, 3.2f, 8f, 8, 0.05f);
        }
        else
        {
            kit.Dome(treeLeaf, Vector3.up * 10f, 5.5f, 8, 3, 0.85f, true);
        }

        Mesh mesh = GraphicsBuilder.Save(kit.BuildMesh(name, out Material[] materials), name + "Mesh");
        var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
        go.GetComponent<MeshFilter>().sharedMesh = mesh;
        var renderer = go.GetComponent<MeshRenderer>();
        renderer.sharedMaterials = materials;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(go, $"Assets/Generated/{name}.prefab");
        Object.DestroyImmediate(go);
        return prefab;
    }

    static void ApplyTrees(Terrain terrain, List<string> log)
    {
        TerrainData data = terrain.terrainData;
        data.treePrototypes = new[]
        {
            new TreePrototype { prefab = TreePrefab("TreeConifer", true) },
            new TreePrototype { prefab = TreePrefab("TreeBroadleaf", false) },
        };
        data.SetTreeInstances(trees.ToArray(), true);
        // Visible from a long way off; there are no billboards, so beyond this they simply stop drawing.
        terrain.treeDistance = 7000f;
        terrain.treeBillboardDistance = 7000f;
        terrain.treeMaximumFullLODCount = 60000;
        terrain.Flush();
        EditorUtility.SetDirty(data);
    }
}

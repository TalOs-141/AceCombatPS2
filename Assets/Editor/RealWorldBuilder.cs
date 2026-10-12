using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

/// <summary>
/// Builds the San Diego map from real data: US Geological Survey elevation
/// shapes the ground and USGS aerial photography is draped over it, the way a
/// flight simulator does. The map is 20 km square, centred on San Diego Bay,
/// at true scale. Source files live in Assets/RealWorld/SanDiego.
///
/// World coordinates are metres east (X) and north (Z) of the map centre,
/// which is UTM zone 11N 483600 E, 3620250 N.
/// </summary>
public static class RealWorldBuilder
{
    const string Dir = "Assets/RealWorld/SanDiego";
    const float MapSize = 20000f;
    const int Tiles = 4;
    const float TileSize = MapSize / Tiles;
    /// <summary>Elevation samples along one side of the whole map (one every 9.77 m).</summary>
    const int Grid = 2049;
    const int TileResolution = (Grid - 1) / Tiles + 1;
    const float BaseY = -10f;
    const float HeightRange = 200f;
    const float WaterLevel = -0.4f;
    /// <summary>Water areas are pushed down to this so the animated sea surface covers them.</summary>
    const float SeaBed = -1.8f;

    // San Diego International's runway 09/27, measured off the aerial photo.
    static readonly Vector3 RunwayCentre = new Vector3(-1161f, 0f, 1542f);
    const float RunwayHeading = 106f;
    const float RunwayLength = 2865f;

    /// <summary>The aircraft carrier sits in open ocean west of Point Loma.</summary>
    static readonly Vector3 CarrierPosition = new Vector3(-8000f, 0f, -6000f);

    static float[] elevation;

    [MenuItem("AceCombat/Build San Diego")]
    public static void BuildMenu() => Debug.Log(Build());

    public static string Build()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return "Stop Play mode first.";
        string elevationPath = Dir + "/elevation_2049.bytes";
        if (!File.Exists(elevationPath)) return "Missing " + elevationPath;
        var log = new List<string>();

        LoadElevation(elevationPath);
        bool[] water = ClassifyWater();
        int waterCount = 0;
        for (int i = 0; i < water.Length; i++)
        {
            if (!water[i]) continue;
            elevation[i] = SeaBed;
            waterCount++;
        }
        log.Add($"elevation {Grid} x {Grid}, {100f * waterCount / water.Length:0}% water");

        // The invented map and everything built on it goes.
        foreach (string old in new[] { "Arena", "World", "SanDiego" })
        {
            GameObject go = GameObject.Find(old);
            if (go != null) Object.DestroyImmediate(go);
        }
        Transform root = new GameObject("SanDiego").transform;

        BuildTerrain(root, log);
        BuildBackdrop(root, log);
        BuildOcean(water, log);
        BuildRunway(root, log);
        BuildCity(root, log);
        PlaceCarrierAndPlayer(log);
        PlaceTargets(log);
        SetAtmosphere();

        // Clouds (and the other generated art) come from the graphics builder.
        GraphicsBuilder.BuildWorld(log);

        AssetDatabase.SaveAssets();
        Scene scene = SceneManager.GetActiveScene();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        return string.Join("; ", log);
    }

    // --------------------------------------------------------------- elevation

    static void LoadElevation(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        elevation = new float[Grid * Grid];
        System.Buffer.BlockCopy(bytes, 0, elevation, 0, elevation.Length * 4);
    }

    static float ToGrid(float world) => (world / MapSize + 0.5f) * (Grid - 1);

    /// <summary>Ground height in metres at a world position.</summary>
    public static float Ground(float wx, float wz)
    {
        float fx = Mathf.Clamp(ToGrid(wx), 0f, Grid - 1.001f), fz = Mathf.Clamp(ToGrid(wz), 0f, Grid - 1.001f);
        int x = (int)fx, z = (int)fz;
        float tx = fx - x, tz = fz - z;
        return Mathf.Lerp(
            Mathf.Lerp(elevation[z * Grid + x], elevation[z * Grid + x + 1], tx),
            Mathf.Lerp(elevation[(z + 1) * Grid + x], elevation[(z + 1) * Grid + x + 1], tx), tz);
    }

    static string TilePath(int ix, int iz) => $"{Dir}/Imagery/aerial_{ix}_{iz}.jpg";

    /// <summary>
    /// Decides which elevation samples are open water: at sea level in the
    /// elevation data and dark blue-green in the photo. Surf, mud flats and
    /// beaches stay as photographed.
    /// </summary>
    static bool[] ClassifyWater()
    {
        var water = new bool[Grid * Grid];
        int perTile = (Grid - 1) / Tiles;

        for (int iz = 0; iz < Tiles; iz++)
        {
            for (int ix = 0; ix < Tiles; ix++)
            {
                var photo = new Texture2D(2, 2, TextureFormat.RGB24, false);
                photo.LoadImage(File.ReadAllBytes(TilePath(ix, iz)));
                Color32[] pixels = photo.GetPixels32();
                int width = photo.width, height = photo.height;
                Object.DestroyImmediate(photo);

                for (int z = 0; z <= perTile; z++)
                {
                    for (int x = 0; x <= perTile; x++)
                    {
                        int index = (iz * perTile + z) * Grid + ix * perTile + x;
                        if (elevation[index] >= 1f) continue;
                        int px = Mathf.Min(x * width / perTile, width - 1), py = Mathf.Min(z * height / perTile, height - 1);
                        Color32 c = pixels[py * width + px];
                        float brightness = (c.r + c.g + c.b) / 765f;
                        if (brightness < 0.34f && c.b >= c.r && c.g >= c.r * 0.9f) water[index] = true;
                    }
                }
            }
        }

        // Tidy single-sample speckle: a sample follows the majority of its neighbours.
        var cleaned = (bool[])water.Clone();
        for (int z = 1; z < Grid - 1; z++)
        {
            for (int x = 1; x < Grid - 1; x++)
            {
                int around = 0;
                for (int dz = -1; dz <= 1; dz++)
                    for (int dx = -1; dx <= 1; dx++)
                        if (water[(z + dz) * Grid + x + dx]) around++;
                cleaned[z * Grid + x] = around >= 5;
            }
        }
        return cleaned;
    }

    // ----------------------------------------------------------------- terrain

    static T SaveAsset<T>(T asset, string path) where T : Object
    {
        T existing = AssetDatabase.LoadAssetAtPath<T>(path);
        if (existing == null)
        {
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }
        EditorUtility.CopySerialized(asset, existing);
        EditorUtility.SetDirty(existing);
        return existing;
    }

    static Texture2D ImportPhoto(string path)
    {
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        // No alpha and a compression format without one: the terrain shader reads alpha as glossiness.
        if (importer.maxTextureSize != 4096 || importer.wrapMode != TextureWrapMode.Clamp
            || importer.alphaSource != TextureImporterAlphaSource.None
            || importer.textureCompression != TextureImporterCompression.Compressed)
        {
            importer.maxTextureSize = 4096;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.anisoLevel = 16;
            importer.alphaSource = TextureImporterAlphaSource.None;
            importer.textureCompression = TextureImporterCompression.Compressed;
            importer.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    static void BuildTerrain(Transform root, List<string> log)
    {
        var material = AssetDatabase.LoadAssetAtPath<Material>("Assets/Terrain/TerrainMaterial.mat");
        var terrains = new Terrain[Tiles, Tiles];
        int perTile = TileResolution - 1;

        for (int iz = 0; iz < Tiles; iz++)
        {
            for (int ix = 0; ix < Tiles; ix++)
            {
                var layer = new TerrainLayer
                {
                    diffuseTexture = ImportPhoto(TilePath(ix, iz)),
                    // One copy of the photo across the whole tile.
                    tileSize = new Vector2(TileSize, TileSize),
                    smoothness = 0f,
                    metallic = 0f,
                };
                layer = SaveAsset(layer, $"{Dir}/Layer_{ix}_{iz}.terrainlayer");

                var heights = new float[TileResolution, TileResolution];
                for (int z = 0; z < TileResolution; z++)
                    for (int x = 0; x < TileResolution; x++)
                        heights[z, x] = Mathf.Clamp01((elevation[(iz * perTile + z) * Grid + ix * perTile + x] - BaseY) / HeightRange);

                string dataPath = $"{Dir}/Terrain_{ix}_{iz}.asset";
                var data = AssetDatabase.LoadAssetAtPath<TerrainData>(dataPath);
                if (data == null)
                {
                    data = new TerrainData();
                    AssetDatabase.CreateAsset(data, dataPath);
                }
                data.heightmapResolution = TileResolution;
                data.size = new Vector3(TileSize, HeightRange, TileSize);
                data.SetHeights(0, 0, heights);
                data.terrainLayers = new[] { layer };
                data.alphamapResolution = 16;
                var alpha = new float[16, 16, 1];
                for (int z = 0; z < 16; z++)
                    for (int x = 0; x < 16; x++)
                        alpha[z, x, 0] = 1f;
                data.SetAlphamaps(0, 0, alpha);
                EditorUtility.SetDirty(data);

                GameObject go = Terrain.CreateTerrainGameObject(data);
                go.name = $"Terrain {ix} {iz}";
                go.transform.SetParent(root, false);
                go.transform.position = new Vector3(-MapSize * 0.5f + ix * TileSize, BaseY, -MapSize * 0.5f + iz * TileSize);
                Terrain terrain = go.GetComponent<Terrain>();
                terrain.materialTemplate = material;
                terrain.heightmapPixelError = 4f;
                // Past this distance Unity swaps the photo for a low-resolution copy, so keep it far out.
                terrain.basemapDistance = 30000f;
                terrain.shadowCastingMode = ShadowCastingMode.Off;
                terrain.drawInstanced = true;
                terrains[ix, iz] = terrain;
            }
        }

        // Telling each tile about its neighbours keeps the seams between them closed.
        for (int iz = 0; iz < Tiles; iz++)
            for (int ix = 0; ix < Tiles; ix++)
                terrains[ix, iz].SetNeighbors(
                    ix > 0 ? terrains[ix - 1, iz] : null, iz < Tiles - 1 ? terrains[ix, iz + 1] : null,
                    ix < Tiles - 1 ? terrains[ix + 1, iz] : null, iz > 0 ? terrains[ix, iz - 1] : null);

        log.Add($"{Tiles * Tiles} terrain tiles of {TileSize / 1000f:0} km, aerial photo at {TileSize / 4000f:0.00} m per pixel");
    }

    // ---------------------------------------------------------------- backdrop

    const float BackdropSize = 64000f;
    const int BackdropGrid = 513;
    const int BackdropTiles = 2;
    const float BackdropRange = 1400f;

    /// <summary>
    /// The land around the detailed map: 64 km of the same imagery and elevation
    /// at a sixteenth of the resolution, with a hole cut where the detailed
    /// terrain sits. It is only ever seen from a distance, but it means the
    /// city is surrounded by its real coastline and mountains instead of sea.
    /// </summary>
    static void BuildBackdrop(Transform root, List<string> log)
    {
        string elevationPath = Dir + "/backdrop_elevation_513.bytes";
        if (!File.Exists(elevationPath))
        {
            log.Add("no backdrop elevation: surroundings left as open sea");
            return;
        }
        var coarse = new float[BackdropGrid * BackdropGrid];
        System.Buffer.BlockCopy(File.ReadAllBytes(elevationPath), 0, coarse, 0, coarse.Length * 4);

        var material = AssetDatabase.LoadAssetAtPath<Material>("Assets/Terrain/TerrainMaterial.mat");
        int perTile = (BackdropGrid - 1) / BackdropTiles, resolution = perTile + 1;
        float tileSize = BackdropSize / BackdropTiles, spacing = BackdropSize / (BackdropGrid - 1);
        float half = BackdropSize * 0.5f, inner = MapSize * 0.5f;
        var random = new System.Random(7);

        for (int iz = 0; iz < BackdropTiles; iz++)
        {
            for (int ix = 0; ix < BackdropTiles; ix++)
            {
                var photo = new Texture2D(2, 2, TextureFormat.RGB24, false);
                photo.LoadImage(File.ReadAllBytes($"{Dir}/BackdropSource/backdrop_{ix}_{iz}.jpg"));
                Color32[] pixels = photo.GetPixels32();
                int width = photo.width, height = photo.height;

                var heights = new float[resolution, resolution];
                for (int z = 0; z < resolution; z++)
                {
                    for (int x = 0; x < resolution; x++)
                    {
                        float wx = -half + (ix * perTile + x) * spacing, wz = -half + (iz * perTile + z) * spacing;
                        float e = coarse[(iz * perTile + z) * BackdropGrid + ix * perTile + x];

                        Color32 c = pixels[Mathf.Min(z * height / perTile, height - 1) * width + Mathf.Min(x * width / perTile, width - 1)];
                        bool noPhoto = c.r < 22 && c.g < 22 && c.b < 22;
                        bool seaColoured = !noPhoto && (c.r + c.g + c.b) / 765f < 0.34f && c.b >= c.r && c.g >= c.r * 0.9f;

                        // Outside US coverage there is no elevation: open sea to the west, and a plain
                        // stand-in plateau for Mexico's interior, which is far off in the haze.
                        if (e < -500f) e = wx > 5000f ? 90f : SeaBed;
                        else if (e < 1f && (seaColoured || noPhoto)) e = SeaBed;

                        // Where the detailed map takes over, match it exactly so the join is closed.
                        if (Mathf.Abs(wx) <= inner && Mathf.Abs(wz) <= inner) e = Ground(wx, wz);

                        heights[z, x] = Mathf.Clamp01((e - BaseY) / BackdropRange);
                    }
                }

                // A cell is cut away if it lies entirely under the detailed terrain.
                var surface = new bool[perTile, perTile];
                for (int z = 0; z < perTile; z++)
                {
                    for (int x = 0; x < perTile; x++)
                    {
                        float x0 = -half + (ix * perTile + x) * spacing, z0 = -half + (iz * perTile + z) * spacing;
                        bool covered = x0 >= -inner && x0 + spacing <= inner && z0 >= -inner && z0 + spacing <= inner;
                        surface[z, x] = !covered;
                    }
                }

                // Fill the unphotographed strip with a muted land colour instead of black.
                for (int i = 0; i < pixels.Length; i++)
                {
                    Color32 c = pixels[i];
                    if (c.r >= 22 || c.g >= 22 || c.b >= 22) continue;
                    int grain = random.Next(-7, 8);
                    pixels[i] = new Color32((byte)(118 + grain), (byte)(112 + grain), (byte)(97 + grain), 255);
                }
                photo.SetPixels32(pixels);
                string photoPath = $"{Dir}/Imagery/backdrop_{ix}_{iz}.jpg";
                File.WriteAllBytes(photoPath, photo.EncodeToJPG(90));
                Object.DestroyImmediate(photo);
                AssetDatabase.ImportAsset(photoPath, ImportAssetOptions.ForceUpdate);

                var layer = new TerrainLayer
                {
                    diffuseTexture = ImportPhoto(photoPath),
                    tileSize = new Vector2(tileSize, tileSize),
                    smoothness = 0f,
                    metallic = 0f,
                };
                layer = SaveAsset(layer, $"{Dir}/BackdropLayer_{ix}_{iz}.terrainlayer");

                string dataPath = $"{Dir}/Backdrop_{ix}_{iz}.asset";
                var data = AssetDatabase.LoadAssetAtPath<TerrainData>(dataPath);
                if (data == null)
                {
                    data = new TerrainData();
                    AssetDatabase.CreateAsset(data, dataPath);
                }
                data.heightmapResolution = resolution;
                data.size = new Vector3(tileSize, BackdropRange, tileSize);
                data.SetHeights(0, 0, heights);
                data.SetHoles(0, 0, surface);
                data.terrainLayers = new[] { layer };
                data.alphamapResolution = 16;
                var alpha = new float[16, 16, 1];
                for (int z = 0; z < 16; z++)
                    for (int x = 0; x < 16; x++)
                        alpha[z, x, 0] = 1f;
                data.SetAlphamaps(0, 0, alpha);
                EditorUtility.SetDirty(data);

                GameObject go = Terrain.CreateTerrainGameObject(data);
                go.name = $"Backdrop {ix} {iz}";
                go.transform.SetParent(root, false);
                go.transform.position = new Vector3(-half + ix * tileSize, BaseY, -half + iz * tileSize);
                Terrain terrain = go.GetComponent<Terrain>();
                terrain.materialTemplate = material;
                terrain.heightmapPixelError = 8f;
                terrain.basemapDistance = 90000f;
                terrain.shadowCastingMode = ShadowCastingMode.Off;
                terrain.drawInstanced = true;
            }
        }
        log.Add($"surrounding terrain: {BackdropSize / 1000f:0} km across at {tileSize / 4000f:0} m per pixel");
    }

    // ------------------------------------------------------------------- ocean

    static void BuildOcean(bool[] water, List<string> log)
    {
        // How far out from the shore each water sample is: 0 at the water's edge, 1 well offshore.
        // The water shader fades in over this, so the real photographed shoreline stays crisp.
        const int size = 1024, reach = 8;
        var prefix = new int[(Grid + 1) * (Grid + 1)];
        for (int z = 0; z < Grid; z++)
            for (int x = 0; x < Grid; x++)
                prefix[(z + 1) * (Grid + 1) + x + 1] = (water[z * Grid + x] ? 1 : 0)
                    + prefix[z * (Grid + 1) + x + 1] + prefix[(z + 1) * (Grid + 1) + x] - prefix[z * (Grid + 1) + x];

        var pixels = new Color[size * size];
        for (int py = 0; py < size; py++)
        {
            for (int px = 0; px < size; px++)
            {
                int gx = px * (Grid - 1) / (size - 1), gz = py * (Grid - 1) / (size - 1);
                float depth = 0f;
                if (water[gz * Grid + gx])
                {
                    int x0 = Mathf.Max(gx - reach, 0), x1 = Mathf.Min(gx + reach + 1, Grid);
                    int z0 = Mathf.Max(gz - reach, 0), z1 = Mathf.Min(gz + reach + 1, Grid);
                    int sum = prefix[z1 * (Grid + 1) + x1] - prefix[z0 * (Grid + 1) + x1] - prefix[z1 * (Grid + 1) + x0] + prefix[z0 * (Grid + 1) + x0];
                    float share = sum / (float)((x1 - x0) * (z1 - z0));
                    depth = Mathf.Clamp01((share - 0.5f) * 2f);
                }
                pixels[py * size + px] = new Color(depth, depth, depth);
            }
        }
        var depthMap = new Texture2D(size, size, TextureFormat.RGB24, true, true) { wrapMode = TextureWrapMode.Clamp };
        depthMap.SetPixels(pixels);
        depthMap.Apply(true);
        depthMap = SaveAsset(depthMap, Dir + "/ShoreDepth.asset");

        var material = new Material(Shader.Find("AceCombat/Water"));
        material.SetTexture("_WaveNormals", AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Generated/WaveSlopes.asset"));
        material.SetTexture("_ShoreDepth", depthMap);
        material.SetFloat("_WorldSize", MapSize);
        // Pacific water off southern California: a greener blue than the invented sea, and calmer in the bay.
        material.SetColor("_DeepColor", new Color(0.03f, 0.13f, 0.2f));
        material.SetColor("_ShallowColor", new Color(0.07f, 0.24f, 0.27f));
        material.SetFloat("_Foam", 0f);
        material = SaveAsset(material, Dir + "/Ocean.mat");

        // "Arena" is where the graphics builder expects to hang the cloud layer.
        Transform arena = new GameObject("Arena").transform;
        GameObject ocean = GameObject.CreatePrimitive(PrimitiveType.Plane);
        ocean.name = "Ocean";
        ocean.transform.SetParent(arena, false);
        ocean.transform.position = new Vector3(0f, WaterLevel, 0f);
        // Far bigger than the map, so the Pacific runs to the horizon.
        ocean.transform.localScale = new Vector3(10000f, 1f, 10000f);
        var renderer = ocean.GetComponent<Renderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        log.Add("ocean and bay use the animated water, blended into the photographed shoreline");
    }

    // -------------------------------------------------------------------- city

    static Material LitMaterial(string path, Color color, Texture2D texture)
    {
        var material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        material.SetColor("_BaseColor", color);
        if (texture != null) material.SetTexture("_BaseMap", texture);
        material.SetFloat("_Smoothness", 0.05f);
        return SaveAsset(material, path);
    }

    /// <summary>Sets up the component that raises the buildings at startup (see CityBuildings).</summary>
    static void BuildCity(Transform root, List<string> log)
    {
        var data = AssetDatabase.LoadAssetAtPath<TextAsset>(Dir + "/buildings.bytes");
        if (data == null)
        {
            log.Add("no buildings.bytes: city left flat");
            return;
        }

        // Roofs reuse each terrain tile's aerial photo, so a roof looks exactly like it does from the air.
        var roofs = new Material[Tiles * Tiles];
        for (int iz = 0; iz < Tiles; iz++)
            for (int ix = 0; ix < Tiles; ix++)
                roofs[iz * Tiles + ix] = LitMaterial($"{Dir}/Roof_{ix}_{iz}.mat", Color.white, AssetDatabase.LoadAssetAtPath<Texture2D>(TilePath(ix, iz)));

        var go = new GameObject("City Buildings");
        go.transform.SetParent(root, false);
        var city = go.AddComponent<CityBuildings>();
        city.data = data;
        city.roofByTile = roofs;
        city.mapSize = MapSize;
        city.tilesPerSide = Tiles;
        city.wallsLow = LitMaterial(Dir + "/WallsLow.mat", new Color(0.74f, 0.70f, 0.63f), null);
        // Mid-rise and tower facades are the photo textures already in the project, where available.
        city.wallsMid = AssetDatabase.LoadAssetAtPath<Material>("Assets/Generated/WorldPhotoOffice.mat") ?? city.wallsLow;
        city.wallsTall = AssetDatabase.LoadAssetAtPath<Material>("Assets/Generated/WorldPhotoGlassA.mat") ?? city.wallsMid;
        log.Add("city buildings will be raised from OpenStreetMap footprints at startup");
    }

    // ------------------------------------------------------------------ places

    static void BuildRunway(Transform root, List<string> log)
    {
        // The runway is already in the photo; this is the invisible surface the wheels actually roll on.
        Quaternion rotation = Quaternion.Euler(0f, RunwayHeading, 0f);
        float height = Ground(RunwayCentre.x, RunwayCentre.z);
        var runway = new GameObject("Runway SAN 09-27");
        runway.transform.SetParent(root, false);
        runway.transform.SetPositionAndRotation(new Vector3(RunwayCentre.x, height + 0.1f, RunwayCentre.z), rotation);
        runway.AddComponent<BoxCollider>().size = new Vector3(60f, 1f, RunwayLength);

        var touchdown = new GameObject("Touchdown").transform;
        touchdown.SetParent(runway.transform, false);
        touchdown.localPosition = new Vector3(0f, 0.5f, -RunwayLength * 0.5f + 400f);

        var surface = runway.AddComponent<LandingSurface>();
        surface.displayName = "SAN RUNWAY";
        surface.arrested = false;
        surface.catapult = false;
        surface.touchdown = touchdown;
        log.Add($"runway landing surface at {height:0.0} m, heading {RunwayHeading:0}");
    }

    static void PlaceCarrierAndPlayer(List<string> log)
    {
        GameObject carrier = GameObject.Find("Carrier");
        GameObject player = GameObject.Find("Player");
        if (carrier == null || player == null) return;

        carrier.transform.SetPositionAndRotation(CarrierPosition, Quaternion.identity);
        var plane = player.GetComponent<PlaneController>();
        player.transform.SetPositionAndRotation(CarrierPosition + new Vector3(0f, 20f + plane.gearHeight, -40f), Quaternion.identity);

        Camera cam = Camera.main;
        if (cam != null)
        {
            var chase = cam.GetComponent<ChaseCamera>();
            cam.transform.SetPositionAndRotation(player.transform.TransformPoint(chase != null ? chase.offset : new Vector3(0f, 3.5f, -16f)), Quaternion.identity);
            cam.farClipPlane = 75000f;
        }
        log.Add("carrier stationed 8 km west-south-west of the bay, off Point Loma");
    }

    static void PlaceTargets(List<string> log)
    {
        // The mission's targets now sit on real ground: around the airport, the harbour and Point Loma.
        var spots = new Dictionary<string, Vector2>
        {
            { "Hangar 1", new Vector2(-2150f, 2010f) },
            { "Hangar 2", new Vector2(-1900f, 1960f) },
            { "Hangar 3", new Vector2(-1500f, 1900f) },
            { "Tank 1", new Vector2(-2750f, 1640f) },
            { "Tank 2", new Vector2(-2700f, 1600f) },
            { "Tank 3", new Vector2(-900f, 1250f) },
            { "Radar", new Vector2(-6400f, -1500f) },
            { "SAM 1", new Vector2(-2500f, 790f) },
            { "SAM 2", new Vector2(-3700f, -2300f) },
        };
        GameObject group = GameObject.Find("GroundTargets");
        int moved = 0;
        if (group != null)
        {
            foreach (Transform target in group.transform)
            {
                if (!spots.TryGetValue(target.name, out Vector2 spot)) continue;
                var box = target.GetComponent<BoxCollider>();
                float halfHeight = box != null ? box.size.y * 0.5f : 3f;
                target.SetPositionAndRotation(new Vector3(spot.x, Ground(spot.x, spot.y) + halfHeight, spot.y), Quaternion.Euler(0f, RunwayHeading, 0f));
                moved++;
            }
        }

        Vector3[] patrols = { new Vector3(-1500f, 900f, 2500f), new Vector3(1500f, 1100f, 500f), new Vector3(-3500f, 800f, -1500f) };
        EnemyPlane[] enemies = Object.FindObjectsByType<EnemyPlane>(FindObjectsSortMode.None);
        for (int i = 0; i < enemies.Length; i++)
        {
            enemies[i].transform.position = patrols[i % patrols.Length];
            // Patrol the bay area rather than the old map's much larger circle.
            enemies[i].areaRadius = 4500f;
            EditorUtility.SetDirty(enemies[i]);
        }
        log.Add($"{moved} ground targets and {enemies.Length} enemy aircraft repositioned");
    }

    static void SetAtmosphere()
    {
        // Coastal haze: distant ground fades toward the sky colour instead of ending at a hard edge.
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogColor = new Color(0.66f, 0.74f, 0.82f);
        RenderSettings.fogStartDistance = 3000f;
        RenderSettings.fogEndDistance = 62000f;
    }
}

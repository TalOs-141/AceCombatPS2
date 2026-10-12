using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

/// <summary>
/// Generates the game's art from code: aircraft meshes, textures, particle
/// effects, sea, clouds and terrain paint. Everything lands in Assets/Generated
/// and is safe to regenerate; run it from the AceCombat menu after changing it.
/// </summary>
public static class GraphicsBuilder
{
    const string Dir = "Assets/Generated";
    const string Lit = "Universal Render Pipeline/Lit";
    const string Unlit = "Universal Render Pipeline/Unlit";
    const string ParticleUnlit = "Universal Render Pipeline/Particles/Unlit";

    const float WorldSize = 40000f;
    const float SeaFloor = 8f;

    [MenuItem("AceCombat/Rebuild Graphics")]
    public static void BuildAllMenu() => Debug.Log(BuildAll());

    public static string BuildAll()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return "Stop Play mode first.";
        if (!AssetDatabase.IsValidFolder(Dir)) AssetDatabase.CreateFolder("Assets", "Generated");

        var log = new List<string>();
        BuildAircraft(log);
        BuildEffects(log);
        BuildGroundTargets(log);
        BuildCarrier(log);
        BuildWorld(log);
        // The real-world map's terrain is photographed, not painted.
        if (GameObject.Find("SanDiego") == null) BuildTerrainPaint(log);
        ApplyQuality(log);

        AssetDatabase.SaveAssets();
        Scene scene = SceneManager.GetActiveScene();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        return string.Join("; ", log);
    }

    // ------------------------------------------------------------------ assets

    /// <summary>Creates the asset, or overwrites an existing one in place so references to it survive.</summary>
    internal static T Save<T>(T asset, string name, string extension = "asset") where T : Object
    {
        string path = $"{Dir}/{name}.{extension}";
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

    internal static Material Mat(string name, string shader, Color color, Texture2D texture = null, float smoothness = 0.15f)
    {
        var m = new Material(Shader.Find(shader));
        m.SetColor("_BaseColor", color);
        if (texture != null) m.SetTexture("_BaseMap", texture);
        if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smoothness);
        return Save(m, name, "mat");
    }

    static void MakeTransparent(Material m, bool additive, bool doubleSided = false)
    {
        m.SetFloat("_Surface", 1f);
        m.SetFloat("_Blend", additive ? 2f : 0f);
        m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        m.SetFloat("_DstBlend", (float)(additive ? BlendMode.One : BlendMode.OneMinusSrcAlpha));
        m.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
        m.SetFloat("_DstBlendAlpha", (float)(additive ? BlendMode.One : BlendMode.OneMinusSrcAlpha));
        m.SetFloat("_ZWrite", 0f);
        if (doubleSided) m.SetFloat("_Cull", (float)CullMode.Off);
        m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        m.SetOverrideTag("RenderType", "Transparent");
        m.renderQueue = (int)RenderQueue.Transparent;
        EditorUtility.SetDirty(m);
    }

    // ---------------------------------------------------------------- textures

    /// <summary>Noise that repeats exactly once across 0..1 in both u and v, so textures tile.</summary>
    internal static float TileNoise(float u, float v, float frequency, float seed)
    {
        float a = u * Mathf.PI * 2f, b = v * Mathf.PI * 2f;
        float x = Mathf.Cos(a) * frequency, y = Mathf.Sin(a) * frequency;
        float z = Mathf.Cos(b) * frequency, w = Mathf.Sin(b) * frequency;
        const float o = 100f;
        return (Mathf.PerlinNoise(x + seed + o, z + seed * 1.3f + o)
                + Mathf.PerlinNoise(y + seed * 2.1f + o, w + seed * 0.7f + o)
                + Mathf.PerlinNoise(x + w + seed * 3.3f + o, y + z + seed * 1.9f + o)) / 3f;
    }

    internal static float TileFbm(float u, float v, float frequency, float seed, int octaves)
    {
        float sum = 0f, amplitude = 1f, total = 0f;
        for (int i = 0; i < octaves; i++)
        {
            sum += TileNoise(u, v, frequency, seed + i * 17f) * amplitude;
            total += amplitude;
            amplitude *= 0.5f;
            frequency *= 2f;
        }
        return sum / total;
    }

    /// <summary>0 below edge0, 1 above edge1, smooth in between. (Mathf.SmoothStep takes its arguments differently.)</summary>
    static float Step(float edge0, float edge1, float x)
    {
        return Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(edge0, edge1, x));
    }

    internal static float Hash(int x, int y, int seed)
    {
        unchecked
        {
            int h = x * 374761393 + y * 668265263 + seed * 982451653;
            h = (h ^ (h >> 13)) * 1274126177;
            return ((h ^ (h >> 16)) & 0xFFFF) / 65535f;
        }
    }

    internal delegate Color Pixel(float u, float v, int x, int y);

    internal static Texture2D Tex(string name, int size, bool alpha, Pixel pixel, bool repeat = true)
    {
        var tex = new Texture2D(size, size, alpha ? TextureFormat.RGBA32 : TextureFormat.RGB24, true)
        {
            wrapMode = repeat ? TextureWrapMode.Repeat : TextureWrapMode.Clamp,
            anisoLevel = 8,
        };
        var pixels = new Color[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
                pixels[y * size + x] = pixel((x + 0.5f) / size, (y + 0.5f) / size, x, y);
        tex.SetPixels(pixels);
        tex.Apply(true);
        return Save(tex, name);
    }

    /// <summary>
    /// Top-down paint scheme for a jet: splinter camouflage, panel lines, a dark
    /// radome and wing roundels. Mapped onto the mesh by its X/Z position.
    /// </summary>
    static Texture2D JetPaint(string name, Color light, Color mid, Color dark, Color roundel, int seed)
    {
        return Tex(name, 256, false, (u, v, x, y) =>
        {
            // Quantised low-frequency noise gives hard-edged camouflage patches.
            float n = Mathf.PerlinNoise(u * 5f + seed, v * 6f + seed * 0.37f);
            Color c = n < 0.38f ? dark : n < 0.6f ? mid : light;

            // Fine grime so flat areas aren't perfectly uniform.
            c *= 0.93f + Mathf.PerlinNoise(u * 60f, v * 60f + seed) * 0.12f;

            if (x % 32 == 0 || y % 40 == 0) c *= 0.8f;
            if (v > 0.9f) c = Color.Lerp(c, new Color(0.18f, 0.18f, 0.2f), 0.85f);

            foreach (float cu in new[] { 0.225f, 0.775f })
            {
                float d = Vector2.Distance(new Vector2(u, v), new Vector2(cu, 0.4f));
                if (d < 0.022f) c = Color.white;
                else if (d < 0.04f) c = roundel;
            }
            c.a = 1f;
            return c;
        }, repeat: false);
    }

    // ------------------------------------------------------------------ meshes

    struct Ring
    {
        public float z, halfWidth, halfHeight, y, x;
        public Ring(float z, float halfWidth, float halfHeight, float y, float x = 0f)
        {
            this.z = z; this.halfWidth = halfWidth; this.halfHeight = halfHeight; this.y = y; this.x = x;
        }
    }

    class MeshBuilder
    {
        readonly List<Vector3> vertices = new List<Vector3>();
        readonly List<Vector2> uvs = new List<Vector2>();
        readonly List<int>[] triangles;

        public MeshBuilder(int subMeshes)
        {
            triangles = new List<int>[subMeshes];
            for (int i = 0; i < subMeshes; i++) triangles[i] = new List<int>();
        }

        int Vertex(Vector3 p)
        {
            vertices.Add(p);
            // Planar projection from above: X across a 12 m span, Z along a 16 m length.
            uvs.Add(new Vector2(p.x / 12f + 0.5f, p.z / 16f + 0.5f));
            return vertices.Count - 1;
        }

        /// <summary>Adds a triangle wound so that it faces along <paramref name="outward"/>.</summary>
        void Triangle(int sub, int a, int b, int c, Vector3 outward)
        {
            if (Vector3.Dot(Vector3.Cross(vertices[b] - vertices[a], vertices[c] - vertices[a]), outward) < 0f)
            {
                int swap = b; b = c; c = swap;
            }
            triangles[sub].Add(a); triangles[sub].Add(b); triangles[sub].Add(c);
        }

        void FlatQuad(int sub, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 outward)
        {
            // Own vertices per face, so edges stay crisp.
            int ia = Vertex(a), ib = Vertex(b), ic = Vertex(c), id = Vertex(d);
            Triangle(sub, ia, ib, ic, outward);
            Triangle(sub, ia, ic, id, outward);
        }

        /// <summary>Skins a tube through a series of elliptical cross-sections. Smooth shaded.</summary>
        public void Loft(int sub, IList<Ring> rings, int sides, bool capFirst, bool capLast, int capSub)
        {
            var index = new int[rings.Count][];
            for (int r = 0; r < rings.Count; r++)
            {
                index[r] = new int[sides];
                for (int s = 0; s < sides; s++)
                {
                    float angle = s * Mathf.PI * 2f / sides;
                    index[r][s] = Vertex(new Vector3(
                        rings[r].x + Mathf.Cos(angle) * rings[r].halfWidth,
                        rings[r].y + Mathf.Sin(angle) * rings[r].halfHeight,
                        rings[r].z));
                }
            }

            for (int r = 0; r < rings.Count - 1; r++)
            {
                Vector3 axis = new Vector3((rings[r].x + rings[r + 1].x) * 0.5f, (rings[r].y + rings[r + 1].y) * 0.5f, (rings[r].z + rings[r + 1].z) * 0.5f);
                for (int s = 0; s < sides; s++)
                {
                    int s1 = (s + 1) % sides;
                    int a = index[r][s], b = index[r][s1], c = index[r + 1][s1], d = index[r + 1][s];
                    Vector3 outward = (vertices[a] + vertices[b] + vertices[c] + vertices[d]) * 0.25f - axis;
                    Triangle(sub, a, b, c, outward);
                    Triangle(sub, a, c, d, outward);
                }
            }

            if (capFirst) Cap(capSub, rings[0], index[0], rings[0].z - rings[1].z);
            if (capLast) Cap(capSub, rings[rings.Count - 1], index[rings.Count - 1], rings[rings.Count - 1].z - rings[rings.Count - 2].z);
        }

        void Cap(int sub, Ring ring, int[] rim, float direction)
        {
            Vector3 outward = new Vector3(0f, 0f, Mathf.Sign(direction));
            Vector3 centre = new Vector3(ring.x, ring.y, ring.z);
            for (int s = 0; s < rim.Length; s++)
            {
                // Fresh vertices so the cap doesn't smooth into the tube wall.
                int a = Vertex(centre), b = Vertex(vertices[rim[s]]), c = Vertex(vertices[rim[(s + 1) % rim.Length]]);
                Triangle(sub, a, b, c, outward);
            }
        }

        /// <summary>
        /// A flat four-cornered surface (wing, fin) given thickness. Corners run
        /// root-front, root-back, tip-back, tip-front.
        /// </summary>
        public void Panel(int sub, Vector3 rootFront, Vector3 rootBack, Vector3 tipBack, Vector3 tipFront, float rootThickness, float tipThickness)
        {
            Vector3 normal = Vector3.Cross(rootBack - rootFront, tipFront - rootFront).normalized;
            Vector3 r = normal * (rootThickness * 0.5f), t = normal * (tipThickness * 0.5f);
            Vector3 centroid = (rootFront + rootBack + tipBack + tipFront) * 0.25f;

            FlatQuad(sub, rootFront + r, rootBack + r, tipBack + t, tipFront + t, normal);
            FlatQuad(sub, rootFront - r, rootBack - r, tipBack - t, tipFront - t, -normal);

            Vector3[] top = { rootFront + r, rootBack + r, tipBack + t, tipFront + t };
            Vector3[] bottom = { rootFront - r, rootBack - r, tipBack - t, tipFront - t };
            for (int i = 0; i < 4; i++)
            {
                int j = (i + 1) % 4;
                Vector3 outward = (top[i] + top[j]) * 0.5f - centroid;
                FlatQuad(sub, top[i], top[j], bottom[j], bottom[i], outward);
            }
        }

        /// <summary>Adds the panel and its mirror image across the aircraft centreline.</summary>
        public void PanelPair(int sub, Vector3 rootFront, Vector3 rootBack, Vector3 tipBack, Vector3 tipFront, float rootThickness, float tipThickness)
        {
            Panel(sub, rootFront, rootBack, tipBack, tipFront, rootThickness, tipThickness);
            Panel(sub, Mirror(rootFront), Mirror(rootBack), Mirror(tipBack), Mirror(tipFront), rootThickness, tipThickness);
        }

        static Vector3 Mirror(Vector3 p) => new Vector3(-p.x, p.y, p.z);

        public Mesh Build(string name)
        {
            var mesh = new Mesh { name = name };
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.subMeshCount = triangles.Length;
            for (int i = 0; i < triangles.Length; i++) mesh.SetTriangles(triangles[i], i);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
    }

    /// <summary>Twin-engine, twin-tail fighter about 15 m long. Submeshes: 0 airframe, 1 canopy, 2 dark trim.</summary>
    static Mesh JetMesh()
    {
        const int Body = 0, Canopy = 1, Trim = 2;
        var b = new MeshBuilder(3);

        b.Loft(Body, new[]
        {
            new Ring(7.6f, 0.03f, 0.03f, -0.15f),
            new Ring(6.6f, 0.28f, 0.26f, -0.10f),
            new Ring(5.2f, 0.52f, 0.50f, -0.02f),
            new Ring(3.6f, 0.72f, 0.66f, 0.05f),
            new Ring(1.5f, 0.95f, 0.70f, 0.05f),
            new Ring(-1.0f, 1.15f, 0.62f, 0f),
            new Ring(-3.5f, 1.15f, 0.60f, 0f),
            new Ring(-5.6f, 1.00f, 0.55f, 0f),
            new Ring(-6.3f, 0.90f, 0.50f, 0f),
        }, 12, false, true, Trim);

        b.Loft(Canopy, new[]
        {
            new Ring(4.9f, 0.02f, 0.02f, 0.50f),
            new Ring(4.3f, 0.30f, 0.30f, 0.62f),
            new Ring(3.3f, 0.42f, 0.46f, 0.72f),
            new Ring(2.2f, 0.40f, 0.43f, 0.70f),
            new Ring(1.2f, 0.18f, 0.18f, 0.62f),
        }, 10, false, false, Canopy);

        foreach (float side in new[] { -1f, 1f })
        {
            // Engine intakes under the shoulders, open (dark) at the front.
            b.Loft(Body, new[]
            {
                new Ring(2.0f, 0.36f, 0.34f, -0.42f, side * 0.95f),
                new Ring(-1.5f, 0.42f, 0.38f, -0.38f, side * 0.85f),
            }, 8, true, false, Trim);

            // Exhaust nozzles.
            b.Loft(Trim, new[]
            {
                new Ring(-6.2f, 0.44f, 0.44f, 0f, side * 0.52f),
                new Ring(-7.0f, 0.36f, 0.36f, 0f, side * 0.52f),
            }, 10, false, true, Trim);
        }

        // Main wings, leading-edge extensions, tailplanes and canted fins.
        b.PanelPair(Body, new Vector3(1.0f, 0f, 1.2f), new Vector3(1.0f, 0f, -3.4f), new Vector3(5.4f, -0.05f, -3.2f), new Vector3(5.4f, -0.05f, -1.6f), 0.22f, 0.06f);
        b.PanelPair(Body, new Vector3(0.70f, 0.05f, 3.9f), new Vector3(1.0f, 0f, 1.2f), new Vector3(2.1f, 0f, 1.2f), new Vector3(0.78f, 0.05f, 3.9f), 0.10f, 0.06f);
        b.PanelPair(Body, new Vector3(1.0f, 0f, -4.3f), new Vector3(1.0f, 0f, -6.4f), new Vector3(3.0f, 0f, -6.6f), new Vector3(3.0f, 0f, -5.6f), 0.12f, 0.05f);
        b.PanelPair(Body, new Vector3(0.75f, 0.45f, -3.4f), new Vector3(0.75f, 0.45f, -6.0f), new Vector3(1.25f, 2.9f, -6.3f), new Vector3(1.25f, 2.9f, -5.2f), 0.12f, 0.05f);

        // Dorsal spine behind the canopy and a pitot probe on the nose.
        b.Loft(Body, new[]
        {
            new Ring(1.4f, 0.30f, 0.26f, 0.62f),
            new Ring(-1.5f, 0.36f, 0.24f, 0.52f),
            new Ring(-4.4f, 0.20f, 0.12f, 0.50f),
        }, 8, false, false, Body);
        b.Loft(Trim, new[] { new Ring(8.7f, 0.02f, 0.02f, -0.15f), new Ring(7.5f, 0.035f, 0.035f, -0.15f) }, 5, false, false, Trim);

        // Ventral fins under the tail, weapon pylons under the wings, and missile rails on the wingtips.
        b.PanelPair(Body, new Vector3(0.9f, -0.45f, -4.2f), new Vector3(0.9f, -0.45f, -5.8f), new Vector3(1.25f, -1.25f, -5.9f), new Vector3(1.25f, -1.25f, -5.0f), 0.08f, 0.04f);
        b.PanelPair(Trim, new Vector3(3.6f, -0.08f, -0.5f), new Vector3(3.6f, -0.08f, -2.3f), new Vector3(3.6f, -0.55f, -2.1f), new Vector3(3.6f, -0.55f, -0.8f), 0.14f, 0.1f);
        b.PanelPair(Trim, new Vector3(2.2f, -0.08f, 0.2f), new Vector3(2.2f, -0.08f, -1.8f), new Vector3(2.2f, -0.5f, -1.6f), new Vector3(2.2f, -0.5f, -0.1f), 0.14f, 0.1f);
        foreach (float side in new[] { -1f, 1f })
        {
            b.Loft(Trim, new[]
            {
                new Ring(-0.9f, 0.02f, 0.02f, -0.05f, side * 5.5f),
                new Ring(-1.3f, 0.09f, 0.09f, -0.05f, side * 5.5f),
                new Ring(-3.5f, 0.09f, 0.09f, -0.05f, side * 5.5f),
            }, 6, false, true, Trim);
        }

        return Save(b.Build("Jet"), "JetMesh");
    }

    /// <summary>Two exhaust plumes, one per engine, pointing back from the origin; 5 m long at scale 1.</summary>
    static Mesh FlameMesh()
    {
        var b = new MeshBuilder(1);
        foreach (float side in new[] { -1f, 1f })
        {
            b.Loft(0, new[]
            {
                new Ring(0f, 0.34f, 0.34f, 0f, side * 0.52f),
                new Ring(-1.6f, 0.26f, 0.26f, 0f, side * 0.52f),
                new Ring(-5f, 0.02f, 0.02f, 0f, side * 0.52f),
            }, 10, false, false, 0);
        }
        return Save(b.Build("AfterburnerFlame"), "AfterburnerFlameMesh");
    }

    // ---------------------------------------------------------------- aircraft

    static void BuildAircraft(List<string> log)
    {
        Mesh jet = JetMesh();
        Mesh flame = FlameMesh();

        Texture2D playerPaint = JetPaint("JetPaintPlayer",
            new Color(0.62f, 0.66f, 0.70f), new Color(0.48f, 0.53f, 0.58f), new Color(0.36f, 0.41f, 0.47f),
            new Color(0.12f, 0.25f, 0.55f), 3);
        Texture2D enemyPaint = JetPaint("JetPaintEnemy",
            new Color(0.66f, 0.56f, 0.40f), new Color(0.48f, 0.38f, 0.26f), new Color(0.30f, 0.33f, 0.22f),
            new Color(0.75f, 0.10f, 0.08f), 11);

        Material playerBody = Mat("JetPlayer", Lit, Color.white, playerPaint, 0.35f);
        Material enemyBody = Mat("JetEnemy", Lit, Color.white, enemyPaint, 0.35f);
        Material canopy = Mat("JetCanopy", Lit, new Color(0.05f, 0.12f, 0.2f), null, 0.92f);
        Material trim = Mat("JetTrim", Lit, new Color(0.12f, 0.12f, 0.14f), null, 0.3f);
        Material flameMat = Mat("AfterburnerFlame", ParticleUnlit, new Color(1f, 0.55f, 0.15f, 0.85f));
        MakeTransparent(flameMat, additive: true, doubleSided: true);

        // Player, in the open scene.
        GameObject player = GameObject.Find("Player");
        if (player != null)
        {
            Transform model = player.transform.Find("Model");
            int vehicles = BuildHangar(player, model, jet, playerBody, canopy, trim);

            Transform burner = model.Find("Afterburner");
            if (burner != null)
            {
                burner.localPosition = new Vector3(0f, 0f, -7.0f);
                for (int i = burner.childCount - 1; i >= 0; i--) Object.DestroyImmediate(burner.GetChild(i).gameObject);
                var flameGo = new GameObject("Flame", typeof(MeshFilter), typeof(MeshRenderer));
                flameGo.transform.SetParent(burner, false);
                flameGo.GetComponent<MeshFilter>().sharedMesh = flame;
                var fr = flameGo.GetComponent<MeshRenderer>();
                fr.sharedMaterial = flameMat;
                fr.shadowCastingMode = ShadowCastingMode.Off;
            }

            Camera cam = Camera.main;
            if (cam != null)
            {
                cam.nearClipPlane = 0.5f;
                var chase = cam.GetComponent<ChaseCamera>();
                // Which renderers the cockpit view hides is decided at runtime by the vehicle selector.
                if (chase != null) chase.hideInCockpit = new Renderer[0];
                EditorUtility.SetDirty(cam);
            }
            EditorUtility.SetDirty(player);
            log.Add($"hangar: {vehicles} flyable vehicles");
        }

        // Enemy prefab.
        const string enemyPath = "Assets/Prefabs/Enemy.prefab";
        GameObject enemy = PrefabUtility.LoadPrefabContents(enemyPath);
        ReplaceBody(enemy.transform.Find("Model"), jet, enemyBody, canopy, trim, "Spitfire", "Spitfire_Red");
        PrefabUtility.SaveAsPrefabAsset(enemy, enemyPath);
        PrefabUtility.UnloadPrefabContents(enemy);
        log.Add("enemy jet rebuilt");
    }

    /// <summary>Swaps the old box-built airframe under a Model transform for the generated mesh.</summary>
    const string ShipDir = "Assets/ThirdParty/Quaternius/UltimateSpaceships";

    static bool HasShip(string ship) => AssetDatabase.LoadAssetAtPath<GameObject>($"{ShipDir}/{ship}/{ship}.fbx") != null;

    /// <summary>
    /// Places one of the downloaded Quaternius ships under a parent, scaled so its
    /// longest side is <paramref name="size"/> metres and centred on the parent.
    /// Returns its renderers, or null if the ship's files aren't in the project.
    /// </summary>
    static Renderer[] AddShip(Transform parent, string ship, string textureName, float size, string objectName = "Body")
    {
        var model = AssetDatabase.LoadAssetAtPath<GameObject>($"{ShipDir}/{ship}/{ship}.fbx");
        var texture = AssetDatabase.LoadAssetAtPath<Texture2D>($"{ShipDir}/{ship}/{textureName}.png");
        if (model == null || texture == null) return null;
        return AddModel(parent, model, Mat("Ship" + ship, Lit, Color.white, texture, 0.35f), size, Vector3.zero, objectName);
    }

    /// <summary>
    /// Places any imported model under a parent, turned by <paramref name="turn"/> so its nose
    /// points forward, scaled so its longest side is <paramref name="size"/> metres, and centred.
    /// A material, if given, replaces the model's own.
    /// </summary>
    static Renderer[] AddModel(Transform parent, GameObject model, Material material, float size, Vector3 turn, string objectName = "Body")
    {
        if (model == null) return null;
        GameObject go = Object.Instantiate(model, parent);
        go.name = objectName;
        // The model file carries its own rotation and scale (it may have been authored Z-up); keep those.
        go.transform.localPosition = Vector3.zero;
        go.transform.localRotation = Quaternion.Euler(turn) * go.transform.localRotation;
        Vector3 authoredScale = go.transform.localScale;

        // Measure the model in the parent's space, whatever its own internal hierarchy looks like.
        var bounds = new Bounds();
        bool any = false;
        Renderer[] renderers = go.GetComponentsInChildren<Renderer>();
        foreach (Renderer r in renderers)
        {
            var filter = r.GetComponent<MeshFilter>();
            if (filter == null || filter.sharedMesh == null) continue;
            Matrix4x4 toParent = parent.worldToLocalMatrix * r.transform.localToWorldMatrix;
            Bounds mesh = filter.sharedMesh.bounds;
            for (int corner = 0; corner < 8; corner++)
            {
                Vector3 p = mesh.center + Vector3.Scale(mesh.extents, new Vector3((corner & 1) * 2 - 1, ((corner >> 1) & 1) * 2 - 1, ((corner >> 2) & 1) * 2 - 1));
                p = toParent.MultiplyPoint3x4(p);
                if (any) bounds.Encapsulate(p);
                else { bounds = new Bounds(p, Vector3.zero); any = true; }
            }
            if (material == null) continue;
            var materials = new Material[r.sharedMaterials.Length];
            for (int m = 0; m < materials.Length; m++) materials[m] = material;
            r.sharedMaterials = materials;
        }
        if (!any) return null;

        float scale = size / Mathf.Max(bounds.size.x, bounds.size.z);
        go.transform.localScale = authoredScale * scale;
        go.transform.localPosition = -bounds.center * scale;
        return renderers;
    }

    /// <summary>
    /// Fills the player's hangar: every flyable vehicle's model goes under Model/Bodies,
    /// and the VehicleSelector is told about each. Returns how many there are.
    /// </summary>
    static int BuildHangar(GameObject player, Transform model, Mesh jet, Material jetBody, Material canopy, Material trim)
    {
        var keep = new HashSet<string> { "Gear", "Afterburner" };
        for (int i = model.childCount - 1; i >= 0; i--)
            if (!keep.Contains(model.GetChild(i).name)) Object.DestroyImmediate(model.GetChild(i).gameObject);

        Transform bodies = new GameObject("Bodies").transform;
        bodies.SetParent(model, false);
        var hangar = new List<VehicleSelector.Vehicle>();

        Transform Holder(string name)
        {
            Transform holder = new GameObject(name).transform;
            holder.SetParent(bodies, false);
            return holder;
        }

        // The Quaternius sci-fi ships (the red Spitfire is kept for the enemy).
        (string ship, string texture)[] ships =
        {
            ("Striker", "Striker_Blue"), ("Executioner", "Executioner_Blue"), ("Insurgent", "Insurgent_Blue"),
            ("Zenith", "Zenith_Red"), ("Imperial", "Imperial_Orange"), ("Omen", "Omen_Orange"),
            ("Challenger", "Challenger_Green"), ("Dispatcher", "Dispatcher_Purple"), ("Bob", "Bob_Orange"), ("Pancake", "Pancake_Orange"),
        };
        foreach ((string ship, string texture) in ships)
        {
            Transform holder = Holder(ship);
            if (AddShip(holder, ship, texture, 15f) == null) Object.DestroyImmediate(holder.gameObject);
            else hangar.Add(new VehicleSelector.Vehicle { displayName = ship, body = holder.gameObject });
        }

        // NASA's models keep their own materials. Each needs turning to face forward.
        (string name, string path, float size, Vector3 turn)[] nasa =
        {
            ("Global Hawk (NASA)", "Assets/ThirdParty/NASA/GlobalHawk/GlobalHawk.glb", 18f, NasaTurnGlobalHawk),
            ("Space Shuttle (NASA)", "Assets/ThirdParty/NASA/SpaceShuttle/SpaceShuttle.glb", 17f, NasaTurnShuttle),
        };
        foreach ((string name, string path, float size, Vector3 turn) in nasa)
        {
            Transform holder = Holder(name);
            if (AddModel(holder, AssetDatabase.LoadAssetAtPath<GameObject>(path), null, size, turn) == null) Object.DestroyImmediate(holder.gameObject);
            else hangar.Add(new VehicleSelector.Vehicle { displayName = name, body = holder.gameObject, showAfterburner = false });
        }

        // The generated jet stays available.
        {
            Transform holder = Holder("Fighter jet");
            var go = new GameObject("Body", typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(holder, false);
            go.GetComponent<MeshFilter>().sharedMesh = jet;
            go.GetComponent<MeshRenderer>().sharedMaterials = new[] { jetBody, canopy, trim };
            hangar.Add(new VehicleSelector.Vehicle { displayName = "Fighter jet", body = holder.gameObject });
        }

        // And a helicopter, built from scratch.
        {
            Transform holder = Holder("Helicopter");
            BuildHelicopter(holder, canopy, trim, out Transform rotor, out Transform tailRotor);
            hangar.Add(new VehicleSelector.Vehicle
            {
                displayName = "Helicopter", body = holder.gameObject, helicopter = true, rotor = rotor, tailRotor = tailRotor,
                showGear = false, showAfterburner = false, cockpitOffset = new Vector3(0f, 0.55f, 3.3f),
            });
        }

        // In the editor only the first is shown; at runtime the selector shows the chosen one.
        for (int i = 0; i < hangar.Count; i++) hangar[i].body.SetActive(i == 0);

        var selector = player.GetComponent<VehicleSelector>();
        if (selector == null) selector = player.AddComponent<VehicleSelector>();
        selector.vehicles = hangar.ToArray();
        EditorUtility.SetDirty(selector);
        return hangar.Count;
    }

    // How far each NASA model has to be turned for its nose to point along +Z.
    static readonly Vector3 NasaTurnGlobalHawk = new Vector3(0f, 0f, 0f);
    static readonly Vector3 NasaTurnShuttle = new Vector3(-90f, 90f, 0f);

    /// <summary>
    /// A simple attack helicopter, about 14 m long: cabin, tail boom, stub wings
    /// for the missile rails, skids, and separate main and tail rotors that spin.
    /// The skids' underside is 2.4 m below the centre, matching the plane's gear height.
    /// </summary>
    static void BuildHelicopter(Transform parent, Material canopy, Material trim, out Transform rotor, out Transform tailRotor)
    {
        const int Body = 0, Glass = 1, Trim = 2;
        Material paint = Mat("HeliBody", Lit, new Color(0.30f, 0.34f, 0.29f), null, 0.25f);
        var b = new MeshBuilder(3);

        b.Loft(Body, new[]
        {
            new Ring(5.2f, 0.15f, 0.20f, -0.50f), new Ring(4.4f, 0.75f, 0.70f, -0.35f), new Ring(3.0f, 1.00f, 1.05f, -0.10f),
            new Ring(1.0f, 1.10f, 1.20f, 0f), new Ring(-1.2f, 1.05f, 1.15f, 0.05f), new Ring(-2.6f, 0.60f, 0.70f, 0.35f),
            new Ring(-4.0f, 0.30f, 0.34f, 0.55f), new Ring(-8.2f, 0.16f, 0.20f, 0.75f),
        }, 10, false, true, Body);
        b.Loft(Glass, new[]
        {
            new Ring(4.9f, 0.30f, 0.25f, 0.00f), new Ring(4.2f, 0.70f, 0.55f, 0.25f), new Ring(3.0f, 0.90f, 0.75f, 0.55f),
            new Ring(1.9f, 0.85f, 0.60f, 0.75f), new Ring(1.3f, 0.50f, 0.30f, 0.80f),
        }, 10, false, false, Glass);
        // Engine housing under the rotor.
        b.Loft(Body, new[] { new Ring(1.2f, 0.60f, 0.40f, 1.25f), new Ring(-0.5f, 0.70f, 0.45f, 1.30f), new Ring(-2.2f, 0.50f, 0.35f, 1.15f) }, 8, true, true, Trim);

        // Tail fin and tailplane, stub wings, and skids on struts.
        b.Panel(Body, new Vector3(0f, 0.8f, -7.2f), new Vector3(0f, 0.8f, -8.4f), new Vector3(0f, 2.6f, -8.9f), new Vector3(0f, 2.6f, -8.1f), 0.12f, 0.08f);
        b.PanelPair(Body, new Vector3(0.1f, 0.85f, -6.6f), new Vector3(0.1f, 0.85f, -7.4f), new Vector3(1.5f, 0.85f, -7.5f), new Vector3(1.5f, 0.85f, -6.9f), 0.1f, 0.06f);
        b.PanelPair(Body, new Vector3(1.0f, -0.2f, 0.6f), new Vector3(1.0f, -0.2f, -0.9f), new Vector3(3.7f, -0.35f, -0.8f), new Vector3(3.7f, -0.35f, 0.3f), 0.2f, 0.12f);
        foreach (float side in new[] { -1f, 1f })
            b.Loft(Trim, new[] { new Ring(2.7f, 0.09f, 0.09f, -2.3f, side * 1.35f), new Ring(-2.5f, 0.09f, 0.09f, -2.3f, side * 1.35f) }, 6, true, true, Trim);
        foreach (float z in new[] { 1.5f, -1.5f })
            b.PanelPair(Trim, new Vector3(0.9f, -0.9f, z + 0.1f), new Vector3(0.9f, -0.9f, z - 0.1f), new Vector3(1.35f, -2.3f, z - 0.1f), new Vector3(1.35f, -2.3f, z + 0.1f), 0.1f, 0.1f);

        var body = new GameObject("Body", typeof(MeshFilter), typeof(MeshRenderer));
        body.transform.SetParent(parent, false);
        body.GetComponent<MeshFilter>().sharedMesh = Save(b.Build("Helicopter"), "HelicopterMesh");
        body.GetComponent<MeshRenderer>().sharedMaterials = new[] { paint, canopy, trim };

        // Main rotor: four blades and a hub, spinning about the vertical.
        var blades = new MeshBuilder(1);
        blades.PanelPair(0, new Vector3(0.3f, 0f, 0.18f), new Vector3(0.3f, 0f, -0.18f), new Vector3(7f, 0f, -0.14f), new Vector3(7f, 0f, 0.14f), 0.06f, 0.04f);
        foreach (float end in new[] { -1f, 1f })
            blades.Panel(0, new Vector3(-0.18f, 0f, end * 0.3f), new Vector3(0.18f, 0f, end * 0.3f), new Vector3(0.14f, 0f, end * 7f), new Vector3(-0.14f, 0f, end * 7f), 0.06f, 0.04f);
        blades.Panel(0, new Vector3(-0.45f, 0f, 0.45f), new Vector3(-0.45f, 0f, -0.45f), new Vector3(0.45f, 0f, -0.45f), new Vector3(0.45f, 0f, 0.45f), 0.5f, 0.5f);
        var rotorGo = new GameObject("Rotor", typeof(MeshFilter), typeof(MeshRenderer));
        rotorGo.transform.SetParent(parent, false);
        rotorGo.transform.localPosition = new Vector3(0f, 1.95f, -0.4f);
        rotorGo.GetComponent<MeshFilter>().sharedMesh = Save(blades.Build("HelicopterRotor"), "HelicopterRotorMesh");
        rotorGo.GetComponent<MeshRenderer>().sharedMaterial = trim;
        rotor = rotorGo.transform;

        // Tail rotor: two blades in the vertical plane, spinning about the sideways axis.
        var tail = new MeshBuilder(1);
        foreach (float end in new[] { -1f, 1f })
            tail.Panel(0, new Vector3(0f, end * 0.1f, 0.1f), new Vector3(0f, end * 0.1f, -0.1f), new Vector3(0f, end * 1.15f, -0.08f), new Vector3(0f, end * 1.15f, 0.08f), 0.04f, 0.03f);
        var tailGo = new GameObject("TailRotor", typeof(MeshFilter), typeof(MeshRenderer));
        tailGo.transform.SetParent(parent, false);
        tailGo.transform.localPosition = new Vector3(0.22f, 1.75f, -8.45f);
        tailGo.GetComponent<MeshFilter>().sharedMesh = Save(tail.Build("HelicopterTailRotor"), "HelicopterTailRotorMesh");
        tailGo.GetComponent<MeshRenderer>().sharedMaterial = trim;
        tailRotor = tailGo.transform;
    }

    static Renderer[] ReplaceBody(Transform model, Mesh mesh, Material body, Material canopy, Material trim, string ship, string shipTexture)
    {
        var keep = new HashSet<string> { "Gear", "Afterburner" };
        for (int i = model.childCount - 1; i >= 0; i--)
        {
            Transform child = model.GetChild(i);
            if (!keep.Contains(child.name)) Object.DestroyImmediate(child.gameObject);
        }

        // Prefer the downloaded ship; fall back to the generated jet if its files are missing.
        Renderer[] shipRenderers = AddShip(model, ship, shipTexture, 15f);
        if (shipRenderers != null) return shipRenderers;

        var go = new GameObject("Body", typeof(MeshFilter), typeof(MeshRenderer));
        go.transform.SetParent(model, false);
        go.GetComponent<MeshFilter>().sharedMesh = mesh;
        var renderer = go.GetComponent<MeshRenderer>();
        renderer.sharedMaterials = new[] { body, canopy, trim };
        return new Renderer[] { renderer };
    }

    // ---------------------------------------------------------- ground targets

    /// <summary>Replaces a ground-target prefab's old box visuals with a generated model. Its collider and scripts are untouched.</summary>
    static void Remodel(string prefabName, System.Action<Transform> build)
    {
        string path = $"Assets/Prefabs/{prefabName}.prefab";
        GameObject contents = PrefabUtility.LoadPrefabContents(path);
        for (int i = contents.transform.childCount - 1; i >= 0; i--)
        {
            Transform child = contents.transform.GetChild(i);
            // The SAM's launch point is used by its script, not drawn.
            if (child.GetComponent<Renderer>() != null || child.GetComponentInChildren<Renderer>() != null)
                Object.DestroyImmediate(child.gameObject);
        }
        build(contents.transform);
        PrefabUtility.SaveAsPrefabAsset(contents, path);
        PrefabUtility.UnloadPrefabContents(contents);
    }

    static Transform Part(Transform parent, string name, MeshKit kit, Vector3 localPosition, Vector3 localEuler)
    {
        Mesh mesh = Save(kit.BuildMesh(name, out Material[] materials), name + "Mesh");
        var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPosition;
        go.transform.localEulerAngles = localEuler;
        go.GetComponent<MeshFilter>().sharedMesh = mesh;
        go.GetComponent<MeshRenderer>().sharedMaterials = materials;
        return go.transform;
    }

    static void BuildGroundTargets(List<string> log)
    {
        Texture2D camoTex = Tex("GroundCamoTex", 128, false, (u, v, x, y) =>
        {
            float n = TileFbm(u, v, 2.5f, 31f, 2);
            Color c = n < 0.47f ? new Color(0.20f, 0.25f, 0.14f) : n < 0.53f ? new Color(0.30f, 0.27f, 0.17f) : new Color(0.33f, 0.38f, 0.22f);
            return c * (0.92f + TileNoise(u, v, 30f, 2f) * 0.16f);
        });
        Material camo = Mat("GroundCamo", Lit, Color.white, camoTex, 0.1f);
        camo.SetTextureScale("_BaseMap", new Vector2(0.5f, 0.5f));
        Material dark = Mat("GroundDark", Lit, new Color(0.1f, 0.11f, 0.1f), null, 0.15f);
        Material metal = Mat("GroundMetal", Lit, new Color(0.5f, 0.52f, 0.5f), null, 0.4f);
        Material missile = Mat("GroundMissile", Lit, new Color(0.88f, 0.88f, 0.84f), null, 0.3f);
        Material door = Mat("GroundDoor", Lit, new Color(0.16f, 0.18f, 0.2f), null, 0.3f);

        // Tank: 8 x 5 x 12 m hit box, pivot at its centre, so the ground is 2.5 m below.
        Remodel("Tank", root =>
        {
            var k = new MeshKit();
            Vector3 g = Vector3.down * 2.5f;
            foreach (float side in new[] { -1f, 1f })
            {
                k.Box(dark, dark, g + new Vector3(side * 3.1f, 0f, 0f), 1.7f, 11.4f, 1.7f);
                // Road wheels.
                for (int i = 0; i < 6; i++) k.Box(metal, null, g + new Vector3(side * 4f, 0.25f, -4.6f + i * 1.85f), 0.15f, 1.3f, 1.3f);
            }
            k.Box(camo, camo, g + Vector3.up * 1.1f, 7.6f, 11f, 1.5f);
            k.Box(camo, camo, g + new Vector3(0f, 1.1f, 6.2f), 7f, 1.6f, 0.9f);
            k.Cylinder(camo, camo, g + new Vector3(0f, 2.6f, -0.6f), 2.6f, 1.5f, 10, 2.1f);
            k.Box(camo, camo, g + new Vector3(0f, 2.6f, -3.2f), 3.4f, 2.4f, 1.3f);
            k.Box(dark, dark, g + new Vector3(0f, 3.1f, 4.8f), 0.5f, 8f, 0.5f);
            k.Box(dark, dark, g + new Vector3(0.9f, 4.1f, -0.8f), 0.9f, 0.9f, 0.5f);
            Part(root, "TankModel", k, Vector3.zero, Vector3.zero);
        });

        // SAM launcher: 10 x 8 x 12 m hit box, ground 4 m below the pivot. An eight-wheel truck with a raised missile rack.
        Remodel("SamSite", root =>
        {
            var k = new MeshKit();
            Vector3 g = Vector3.down * 4f;
            k.Box(camo, camo, g + Vector3.up * 1.3f, 3.6f, 11.5f, 1.3f);
            k.Box(camo, camo, g + new Vector3(0f, 2.6f, 4.3f), 3.4f, 2.6f, 2.1f);
            k.Box(dark, null, g + new Vector3(0f, 3.4f, 5.65f), 3f, 0.1f, 1f);
            foreach (float side in new[] { -1f, 1f })
                for (int i = 0; i < 4; i++) k.Box(dark, dark, g + new Vector3(side * 1.9f, 0f, -4.2f + i * 2.8f), 0.7f, 1.5f, 1.5f);
            // Outriggers and a radar set alongside.
            k.Box(metal, null, g + new Vector3(0f, 0f, -1.5f), 7.5f, 0.5f, 1.3f);
            k.Box(camo, camo, g + new Vector3(-4.2f, 0f, 2f), 2.4f, 2.4f, 2.6f);
            k.Dome(metal, g + new Vector3(-4.2f, 3.6f, 2f), 1.5f, 8, 3, 0.6f);
            Part(root, "SamTruck", k, Vector3.zero, Vector3.zero);

            var rack = new MeshKit();
            rack.Box(camo, camo, new Vector3(0f, -0.4f, 0f), 3.4f, 8.5f, 0.5f);
            for (int i = 0; i < 4; i++)
            {
                Vector3 tube = new Vector3(-1.2f + i * 0.8f, 0.1f, 0f);
                rack.Box(missile, missile, tube, 0.55f, 8f, 0.55f);
                rack.Box(dark, dark, tube + new Vector3(0f, 0f, 4.1f), 0.35f, 0.5f, 0.35f);
            }
            Part(root, "SamRack", rack, new Vector3(0f, -0.9f, -2f), new Vector3(-38f, 0f, 0f));
        });

        // Hangar: 60 x 24 x 80 m hit box, ground 12 m below the pivot.
        Remodel("Hangar", root =>
        {
            var k = new MeshKit();
            Vector3 g = Vector3.down * 12f;
            k.Box(camo, null, g, 60f, 80f, 13f, 0f, 12f, 12f);
            k.Gable(camo, g + Vector3.up * 13f, 60f, 80f, 10f, 0f, 1.5f);
            k.Box(door, null, g + new Vector3(0f, 0f, 40.2f), 44f, 0.6f, 12f);
            // Door rails, roof vents and a lean-to workshop.
            k.Box(metal, null, g + new Vector3(0f, 12f, 40.5f), 56f, 0.5f, 0.6f);
            for (int i = 0; i < 4; i++) k.Box(metal, metal, g + new Vector3(0f, 22.5f, -28f + i * 18f), 3f, 5f, 1.5f);
            k.Box(camo, camo, g + new Vector3(-36f, 0f, -10f), 12f, 40f, 6f, 0f, 12f, 12f);
            Part(root, "HangarModel", k, Vector3.zero, Vector3.zero);
        });

        // Radar station: 14 x 22 x 14 m hit box, ground 11 m below the pivot.
        Remodel("RadarStation", root =>
        {
            var k = new MeshKit();
            Vector3 g = Vector3.down * 11f;
            k.Box(camo, camo, g, 13f, 13f, 5f, 0f, 12f, 12f);
            k.Cylinder(metal, metal, g + Vector3.up * 5f, 1.3f, 9f, 8);
            k.Box(metal, null, g + new Vector3(4f, 5f, -4f), 0.3f, 0.3f, 6f);
            Part(root, "RadarHut", k, Vector3.zero, Vector3.zero);

            var dish = new MeshKit();
            dish.Dome(metal, Vector3.zero, 6.5f, 12, 3, 0.35f);
            dish.Box(dark, null, new Vector3(0f, 0f, 0f), 0.4f, 0.4f, 4.5f);
            // Tipped up on edge so the dish faces the horizon.
            Part(root, "RadarDish", dish, new Vector3(0f, 6.5f, 0f), new Vector3(-70f, 0f, 0f));
        });

        log.Add("tank, SAM launcher, hangar and radar station remodelled");
    }

    // ----------------------------------------------------------------- carrier

    /// <summary>
    /// Replaces the carrier's box visuals with a proper ship: flared hull, shaped
    /// flight deck with markings, island with bridge and masts, and aircraft
    /// parked on deck. The existing colliders and landing surface are kept.
    /// </summary>
    static void BuildCarrier(List<string> log)
    {
        GameObject carrier = GameObject.Find("Carrier");
        if (carrier == null) return;

        for (int i = carrier.transform.childCount - 1; i >= 0; i--)
        {
            Transform child = carrier.transform.GetChild(i);
            if (child.GetComponent<Collider>() != null)
            {
                // Solid parts stay for collisions, but the new model does the looking-good.
                var old = child.GetComponent<Renderer>();
                if (old != null) old.enabled = false;
            }
            else if (child.name != "Touchdown") Object.DestroyImmediate(child.gameObject);
        }

        Material hull = Mat("CarrierGrey", Lit, new Color(0.44f, 0.47f, 0.5f), null, 0.25f);
        Material boot = Mat("CarrierBoot", Lit, new Color(0.1f, 0.1f, 0.12f), null, 0.2f);
        Material antifoul = Mat("CarrierRed", Lit, new Color(0.36f, 0.1f, 0.08f), null, 0.15f);
        Texture2D asphalt = ThirdParty("Asphalt031", false);
        Material deck = Mat("CarrierDeckSurface", Lit, new Color(0.5f, 0.5f, 0.52f), asphalt, 0.1f);
        Material white = Mat("CarrierPaintWhite", Unlit, new Color(0.9f, 0.9f, 0.86f));
        Material yellow = Mat("CarrierPaintYellow", Unlit, new Color(0.9f, 0.75f, 0.2f));
        Material glass = Mat("CarrierGlass", Lit, new Color(0.06f, 0.1f, 0.14f), null, 0.9f);
        Material light = Mat("CarrierLight", Unlit, new Color(1f, 0.92f, 0.65f));
        Material radome = Mat("CarrierRadome", Lit, new Color(0.88f, 0.88f, 0.85f), null, 0.4f);

        var k = new MeshKit();
        const float deckTop = 20f, deckUnder = 19.2f, paint = 20.04f;

        // Hull: grey topsides, a black boot stripe at the waterline, red below it.
        k.Hull(hull, new[]
        {
            new Vector3(-166f, 17f, 15.5f), new Vector3(-130f, 19.5f, 18f), new Vector3(0f, 20f, 18.5f),
            new Vector3(90f, 19.5f, 17f), new Vector3(130f, 15f, 11f), new Vector3(155f, 8f, 4.6f), new Vector3(171f, 0.6f, 0.4f),
        }, deckUnder, 1.2f);
        k.Hull(boot, new[]
        {
            new Vector3(-166f, 15.5f, 15.2f), new Vector3(-130f, 18f, 17.6f), new Vector3(0f, 18.5f, 18.1f),
            new Vector3(90f, 17f, 16.6f), new Vector3(130f, 11f, 10.6f), new Vector3(155f, 4.6f, 4.3f), new Vector3(171f, 0.4f, 0.3f),
        }, 1.2f, -0.6f);
        k.Hull(antifoul, new[]
        {
            new Vector3(-166f, 15.2f, 12f), new Vector3(-130f, 17.6f, 15f), new Vector3(0f, 18.1f, 16f),
            new Vector3(90f, 16.6f, 13f), new Vector3(130f, 10.6f, 7f), new Vector3(155f, 4.3f, 2f), new Vector3(170f, 0.3f, 0.1f),
        }, -0.6f, -7f);

        // Flight deck: wider than the hull, with the angled landing area swept out to port.
        k.Prism(hull, deck, new[]
        {
            new Vector2(-13f, 168f), new Vector2(15f, 168f), new Vector2(27f, 120f), new Vector2(39f, 60f),
            new Vector2(42f, -40f), new Vector2(41f, -150f), new Vector2(31f, -166f), new Vector2(-25f, -166f),
            new Vector2(-34f, -128f), new Vector2(-34f, 40f), new Vector2(-23f, 112f),
        }, deckUnder, deckTop, 18f);

        // Catwalks and sponsons under the deck edge.
        k.Box(hull, hull, new Vector3(-27f, 15.5f, -45f), 12f, 170f, 3.7f);
        k.Box(hull, hull, new Vector3(31f, 15.5f, 5f), 14f, 190f, 3.7f);
        for (float z = -150f; z <= 150f; z += 30f) k.Box(boot, null, new Vector3(-20.5f, 10f, z), 0.5f, 6f, 5f);

        // Island: stacked superstructure with a band of bridge windows, funnel, masts and radars.
        Vector3 island = new Vector3(34f, deckTop, -28f);
        k.Box(hull, boot, island, 10f, 36f, 11f);
        k.Box(hull, boot, island + new Vector3(0f, 11f, 2f), 9f, 26f, 5f);
        k.Box(glass, hull, island + new Vector3(-0.2f, 16f, 5f), 9.8f, 17f, 2.3f);
        k.Box(hull, boot, island + new Vector3(0f, 18.3f, 5f), 10.6f, 18f, 1.2f);
        k.Box(boot, boot, island + new Vector3(0.5f, 11f, -13f), 6f, 9f, 13f);
        k.Box(boot, null, island + new Vector3(0f, 19.5f, 6f), 0.9f, 0.9f, 18f);
        k.Box(boot, null, island + new Vector3(0f, 31f, 6f), 9f, 0.5f, 0.5f);
        k.Box(boot, null, island + new Vector3(0f, 27f, 6f), 6f, 0.4f, 0.4f);
        k.Box(hull, hull, island + new Vector3(0f, 22.5f, 8.5f), 6f, 2.4f, 2.6f);
        k.Dome(radome, island + new Vector3(0f, 19.5f, -4f), 2.4f, 10, 4, 1f, true);
        k.Dome(radome, island + new Vector3(0f, 24f, -14f), 1.8f, 10, 4, 1f, true);
        k.Box(yellow, yellow, island + new Vector3(0f, 0f, 22f), 4f, 6f, 5f);

        // Deck paint.
        k.GroundRect(white, new Vector3(0f, paint, 0f), 0.8f, 320f);
        foreach (float side in new[] { -1f, 1f }) k.GroundRect(white, new Vector3(side * 14f, paint, -50f), 0.6f, 230f);
        k.GroundRect(white, new Vector3(0f, paint, -115f), 26f, 2.5f);
        k.GroundRect(yellow, new Vector3(-13f, paint, -40f), 0.8f, 250f, -7f);
        k.GroundRect(white, new Vector3(-27f, paint, -45f), 0.6f, 240f, -7f);
        k.GroundRect(white, new Vector3(1f, paint, -35f), 0.6f, 240f, -7f);
        // Catapult tracks on the bow and waist, and the lift outlines.
        foreach (float x in new[] { -6f, 7f }) k.GroundRect(boot, new Vector3(x, paint, 100f), 0.7f, 120f);
        k.GroundRect(boot, new Vector3(-22f, paint, 20f), 0.7f, 100f, -7f);
        foreach (Vector2 lift in new[] { new Vector2(35f, 70f), new Vector2(35f, -95f), new Vector2(-28f, -70f) })
        {
            k.GroundRect(yellow, new Vector3(lift.x, paint, lift.y), 13f, 27f);
            k.GroundRect(deck, new Vector3(lift.x, paint + 0.01f, lift.y), 12f, 26f, 0f, 18f);
        }
        // Red-and-white foul line keeping parked aircraft clear of the landing area.
        for (int n = 0; n < 38; n++) k.GroundRect(n % 2 == 0 ? white : yellow, new Vector3(20f, paint, -150f + n * 8f), 0.6f, 4f);
        // Arrestor wires and deck-edge lights.
        for (int i = 0; i < 4; i++) k.Box(boot, null, new Vector3(0f, deckTop, -105f + i * 12f), 27f, 0.4f, 0.12f);
        for (float z = -160f; z <= 160f; z += 12f)
        {
            k.Box(light, light, new Vector3(-14.5f, deckTop, z), 0.5f, 0.5f, 0.25f);
            k.Box(light, light, new Vector3(14.5f, deckTop, z), 0.5f, 0.5f, 0.25f);
        }

        Part(carrier.transform, "CarrierModel", k, Vector3.zero, Vector3.zero);

        // Aircraft parked along the starboard side, clear of the catapults and the landing area.
        Mesh jet = AssetDatabase.LoadAssetAtPath<Mesh>(Dir + "/JetMesh.asset");
        var jetMaterials = new[]
        {
            AssetDatabase.LoadAssetAtPath<Material>(Dir + "/JetPlayer.mat"),
            AssetDatabase.LoadAssetAtPath<Material>(Dir + "/JetCanopy.mat"),
            AssetDatabase.LoadAssetAtPath<Material>(Dir + "/JetTrim.mat"),
        };
        var parked = new GameObject("Parked Aircraft").transform;
        parked.SetParent(carrier.transform, false);
        float[] spots = { 12f, 30f, 48f, 112f, 130f, -66f, -124f };
        var stands = new MeshKit();
        for (int i = 0; i < spots.Length; i++)
        {
            var go = new GameObject("Parked Jet");
            go.transform.SetParent(parked, false);
            float x = spots[i] > 100f ? 19f : 31f;
            go.transform.localPosition = new Vector3(x, deckTop + 1.9f, spots[i]);
            go.transform.localRotation = Quaternion.Euler(0f, 215f + (i % 2) * 10f, 0f);
            if (AddShip(go.transform, "Striker", "Striker_Blue", 15f) == null)
            {
                go.AddComponent<MeshFilter>().sharedMesh = jet;
                go.AddComponent<MeshRenderer>().sharedMaterials = jetMaterials;
            }
            foreach (Vector3 leg in new[] { new Vector3(0f, 0f, 4.2f), new Vector3(-1.7f, 0f, -1.6f), new Vector3(1.7f, 0f, -1.6f) })
                stands.Box(boot, null, go.transform.localPosition + go.transform.localRotation * leg + Vector3.down * 1.9f, 0.3f, 0.3f, 1.4f);
        }
        Part(carrier.transform, "ParkedStands", stands, Vector3.zero, Vector3.zero);

        EditorUtility.SetDirty(carrier);
        log.Add($"carrier rebuilt with {spots.Length} parked aircraft");
    }

    // ----------------------------------------------------------------- effects

    static void BuildEffects(List<string> log)
    {
        Texture2D soft = Tex("SoftCircle", 64, true, (u, v, x, y) =>
        {
            float r = Vector2.Distance(new Vector2(u, v), new Vector2(0.5f, 0.5f)) * 2f;
            float a = Mathf.Pow(Mathf.Clamp01(1f - r), 1.5f);
            return new Color(1f, 1f, 1f, a);
        }, repeat: false);

        Texture2D puff = Tex("SmokePuff", 64, true, (u, v, x, y) =>
        {
            float r = Vector2.Distance(new Vector2(u, v), new Vector2(0.5f, 0.5f)) * 2f;
            float lump = Mathf.PerlinNoise(u * 5f + 3f, v * 5f + 7f);
            float a = Mathf.Clamp01(1f - r - (1f - lump) * 0.35f) * 1.4f;
            float shade = 0.75f + lump * 0.25f;
            return new Color(shade, shade, shade, Mathf.Clamp01(a));
        }, repeat: false);

        Material fire = Mat("FxFire", ParticleUnlit, Color.white, soft);
        MakeTransparent(fire, additive: true);
        Material smoke = Mat("FxSmoke", ParticleUnlit, Color.white, puff);
        MakeTransparent(smoke, additive: false);
        Material trail = Mat("FxTrail", ParticleUnlit, Color.white, puff);
        MakeTransparent(trail, additive: false, doubleSided: true);
        trail.SetTextureScale("_BaseMap", new Vector2(0.2f, 1f));

        Gradient fireColors = Colors(
            (new Color(1f, 0.95f, 0.7f), 1f, 0f), (new Color(1f, 0.6f, 0.15f), 1f, 0.5f), (new Color(0.7f, 0.2f, 0.05f), 0f, 1f));
        Gradient smokeColors = Colors(
            (new Color(0.2f, 0.17f, 0.15f), 0f, 0f), (new Color(0.14f, 0.14f, 0.14f), 0.9f, 0.2f), (new Color(0.3f, 0.3f, 0.3f), 0f, 1f));
        Gradient dustColors = Colors(
            (new Color(0.6f, 0.54f, 0.45f), 0.8f, 0f), (new Color(0.6f, 0.56f, 0.5f), 0f, 1f));
        Gradient sparkColors = Colors(
            (new Color(1f, 1f, 0.85f), 1f, 0f), (new Color(1f, 0.7f, 0.2f), 0f, 1f));

        Burst("Explosion", 60f, 1.2f, fire, fireColors, 26, smoke, smokeColors, 16);
        Burst("MissileBlast", 50f, 1.0f, fire, fireColors, 22, smoke, smokeColors, 12);
        Burst("ExplosionSmall", 22f, 0.7f, fire, fireColors, 14, smoke, smokeColors, 8);
        Burst("ImpactSpark", 6f, 0.25f, fire, sparkColors, 7, null, null, 0);
        Burst("ImpactDust", 8f, 0.7f, null, null, 0, smoke, dustColors, 6);

        foreach (string missile in new[] { "Missile", "EnemyMissile" })
        {
            string path = $"Assets/Prefabs/{missile}.prefab";
            GameObject contents = PrefabUtility.LoadPrefabContents(path);
            TrailRenderer tr = contents.GetComponentInChildren<TrailRenderer>();
            if (tr != null)
            {
                tr.sharedMaterial = trail;
                tr.textureMode = LineTextureMode.Tile;
                tr.time = 3f;
                tr.minVertexDistance = 3f;
                tr.widthCurve = new AnimationCurve(new Keyframe(0f, 1.4f), new Keyframe(0.3f, 3.5f), new Keyframe(1f, 7f));
                tr.colorGradient = Colors(
                    (new Color(1f, 0.85f, 0.6f), 0.9f, 0f), (Color.white, 0.6f, 0.08f), (new Color(0.8f, 0.8f, 0.8f), 0f, 1f));
            }
            PrefabUtility.SaveAsPrefabAsset(contents, path);
            PrefabUtility.UnloadPrefabContents(contents);
        }

        log.Add("explosions, impacts and missile trails rebuilt as particle effects");
    }

    static Gradient Colors(params (Color color, float alpha, float time)[] keys)
    {
        var gradient = new Gradient();
        var colorKeys = new GradientColorKey[keys.Length];
        var alphaKeys = new GradientAlphaKey[keys.Length];
        for (int i = 0; i < keys.Length; i++)
        {
            colorKeys[i] = new GradientColorKey(keys[i].color, keys[i].time);
            alphaKeys[i] = new GradientAlphaKey(keys[i].alpha, keys[i].time);
        }
        gradient.SetKeys(colorKeys, alphaKeys);
        return gradient;
    }

    /// <summary>
    /// A one-shot burst: a quick fireball plus slower smoke. Overwrites the
    /// existing prefab of the same name so everything that spawns it keeps working.
    /// </summary>
    static void Burst(string name, float size, float life, Material fireMat, Gradient fireColors, int fireCount, Material smokeMat, Gradient smokeColors, int smokeCount)
    {
        var root = new GameObject(name);
        root.AddComponent<TimedDestroy>().lifetime = life * 3f + 0.5f;

        if (fireCount > 0)
        {
            var go = new GameObject("Fire", typeof(ParticleSystem));
            go.transform.SetParent(root.transform, false);
            Configure(go.GetComponent<ParticleSystem>(), fireMat, fireColors, fireCount,
                life * 0.7f, life * 1.5f, size * 0.05f, size * 0.45f, size * 0.35f, size * 0.6f, size * 0.12f, 0.5f);
        }
        if (smokeCount > 0)
        {
            var go = new GameObject("Smoke", typeof(ParticleSystem));
            go.transform.SetParent(root.transform, false);
            Configure(go.GetComponent<ParticleSystem>(), smokeMat, smokeColors, smokeCount,
                life * 1.4f, life * 2.8f, size * 0.04f, size * 0.22f, size * 0.4f, size * 0.75f, size * 0.2f, 0.35f);
        }

        PrefabUtility.SaveAsPrefabAsset(root, $"Assets/Prefabs/{name}.prefab");
        Object.DestroyImmediate(root);
    }

    static void Configure(ParticleSystem ps, Material material, Gradient colors, int count,
        float lifeMin, float lifeMax, float speedMin, float speedMax, float sizeMin, float sizeMax, float radius, float startScale)
    {
        ParticleSystem.MainModule main = ps.main;
        main.loop = false;
        main.duration = 1f;
        main.playOnAwake = true;
        main.startLifetime = new ParticleSystem.MinMaxCurve(lifeMin, lifeMax);
        main.startSpeed = new ParticleSystem.MinMaxCurve(speedMin, speedMax);
        main.startSize = new ParticleSystem.MinMaxCurve(sizeMin, sizeMax);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = count + 4;

        ParticleSystem.EmissionModule emission = ps.emission;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)count) });

        ParticleSystem.ShapeModule shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = radius;

        ParticleSystem.ColorOverLifetimeModule color = ps.colorOverLifetime;
        color.enabled = true;
        color.color = new ParticleSystem.MinMaxGradient(colors);

        // Puffs swell as they age.
        ParticleSystem.SizeOverLifetimeModule sizeOverLife = ps.sizeOverLifetime;
        sizeOverLife.enabled = true;
        sizeOverLife.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, startScale), new Keyframe(0.3f, 0.9f), new Keyframe(1f, 1f)));

        var renderer = ps.GetComponent<ParticleSystemRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
    }

    // ------------------------------------------------------------------- world

    internal static void BuildWorld(List<string> log)
    {
        GameObject arena = GameObject.Find("Arena");
        if (arena == null) return;

        // Sea: shaded by Assets/Shaders/Water.shader, which needs two generated maps.
        Transform sea = arena.transform.Find("Sea");
        Shader waterShader = Shader.Find("AceCombat/Water");
        if (sea != null && waterShader != null)
        {
            var water = new Material(waterShader);
            water.SetTexture("_WaveNormals", WaveSlopes());
            water.SetTexture("_ShoreDepth", ShoreDepth());
            water.SetFloat("_WorldSize", WorldSize);
            sea.GetComponent<Renderer>().sharedMaterial = Save(water, "Water", "mat");
            // The shader animates itself; the old texture scroller is no longer needed.
            var scroll = sea.GetComponent<ScrollTexture>();
            if (scroll != null) Object.DestroyImmediate(scroll);
        }

        // Cloud layer above the highest peaks.
        Texture2D cloudTex = Tex("CloudTex", 256, true, (u, v, x, y) =>
        {
            float n = TileFbm(u, v, 1.4f, 9f, 4);
            float a = Step(0.47f, 0.56f, n);
            float shade = 0.86f + Step(0.52f, 0.62f, n) * 0.14f;
            return new Color(shade, shade, shade + 0.02f, a * 0.85f);
        });
        Material cloudMat = Mat("Clouds", Unlit, Color.white, cloudTex);
        MakeTransparent(cloudMat, additive: false, doubleSided: true);
        cloudMat.SetTextureScale("_BaseMap", new Vector2(5f, 5f));
        Transform oldClouds = arena.transform.Find("Clouds");
        if (oldClouds != null) Object.DestroyImmediate(oldClouds.gameObject);
        GameObject clouds = GameObject.CreatePrimitive(PrimitiveType.Plane);
        clouds.name = "Clouds";
        Object.DestroyImmediate(clouds.GetComponent<Collider>());
        clouds.transform.SetParent(arena.transform, false);
        clouds.transform.position = new Vector3(0f, 3600f, 0f);
        clouds.transform.localScale = new Vector3(WorldSize / 10f * 1.5f, 1f, WorldSize / 10f * 1.5f);
        var cloudRenderer = clouds.GetComponent<Renderer>();
        cloudRenderer.sharedMaterial = cloudMat;
        cloudRenderer.shadowCastingMode = ShadowCastingMode.Off;
        cloudRenderer.receiveShadows = false;
        clouds.AddComponent<ScrollTexture>().speed = new Vector2(0.0025f, 0.0008f);

        // Buildings: concrete facades with rows of windows instead of blank grey.
        Texture2D windows = Tex("BuildingTex", 128, false, (u, v, x, y) =>
        {
            const int cells = 4;
            float cu = u * cells % 1f, cv = v * cells % 1f;
            float concrete = 0.5f + Mathf.PerlinNoise(u * 40f, v * 40f) * 0.12f;
            Color c = new Color(concrete, concrete, concrete * 0.97f);
            if (cu > 0.2f && cu < 0.8f && cv > 0.25f && cv < 0.78f)
            {
                bool lit = Hash((int)(u * cells), (int)(v * cells), 4) > 0.82f;
                c = lit ? new Color(0.95f, 0.85f, 0.55f) : new Color(0.13f, 0.18f, 0.25f);
            }
            return c;
        });
        Transform buildings = arena.transform.Find("Buildings");
        if (buildings != null)
        {
            var variants = new[]
            {
                BuildingMat("BuildingA", windows, new Color(0.95f, 0.95f, 0.92f), new Vector2(2f, 3f)),
                BuildingMat("BuildingB", windows, new Color(0.82f, 0.78f, 0.72f), new Vector2(3f, 5f)),
                BuildingMat("BuildingC", windows, new Color(0.72f, 0.78f, 0.84f), new Vector2(2f, 6f)),
            };
            for (int i = 0; i < buildings.childCount; i++)
                buildings.GetChild(i).GetComponent<Renderer>().sharedMaterial = variants[i % variants.Length];
        }

        log.Add("sea, clouds and buildings textured");
    }

    /// <summary>
    /// A tiling field of ripples, stored as the surface's slope in X (red) and Z (green).
    /// The water shader overlays several copies at different sizes and speeds.
    /// </summary>
    static Texture2D WaveSlopes()
    {
        const int size = 256;
        var height = new float[size, size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
                height[y, x] = TileFbm((x + 0.5f) / size, (y + 0.5f) / size, 3f, 5f, 4);

        var slope = new Vector2[size * size];
        float steepest = 0.0001f;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                // Differences wrap round the edges so the texture tiles seamlessly.
                float dx = height[y, (x + 1) % size] - height[y, (x + size - 1) % size];
                float dy = height[(y + 1) % size, x] - height[(y + size - 1) % size, x];
                slope[y * size + x] = new Vector2(dx, dy);
                steepest = Mathf.Max(steepest, Mathf.Abs(dx), Mathf.Abs(dy));
            }
        }

        // Linear (not sRGB) data: these are numbers for the shader, not colours.
        var texture = new Texture2D(size, size, TextureFormat.RGB24, true, true) { wrapMode = TextureWrapMode.Repeat, anisoLevel = 8 };
        var pixels = new Color[size * size];
        for (int i = 0; i < pixels.Length; i++)
            pixels[i] = new Color(slope[i].x / steepest * 0.5f + 0.5f, slope[i].y / steepest * 0.5f + 0.5f, 1f);
        texture.SetPixels(pixels);
        texture.Apply(true);
        return Save(texture, "WaveSlopes");
    }

    /// <summary>How deep the water is across the map: black at the waterline, white once it is properly deep.</summary>
    static Texture2D ShoreDepth()
    {
        const int size = 1024;
        const float seaLevel = -1.5f;
        Terrain terrain = Terrain.activeTerrain;
        var texture = new Texture2D(size, size, TextureFormat.RGB24, true, true) { wrapMode = TextureWrapMode.Clamp };
        var pixels = new Color[size * size];
        TerrainData data = terrain != null ? terrain.terrainData : null;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float depth = 1f;
                if (data != null)
                {
                    float ground = data.GetInterpolatedHeight((x + 0.5f) / size, (y + 0.5f) / size) - SeaFloor;
                    depth = Mathf.Clamp01((seaLevel - ground) / (SeaFloor + seaLevel));
                }
                pixels[y * size + x] = new Color(depth, depth, depth);
            }
        }
        texture.SetPixels(pixels);
        texture.Apply(true);
        return Save(texture, "ShoreDepth");
    }

    static Material BuildingMat(string name, Texture2D texture, Color tint, Vector2 tiling)
    {
        Material m = Mat(name, Lit, tint, texture, 0.1f);
        m.SetTextureScale("_BaseMap", tiling);
        return m;
    }

    // ----------------------------------------------------------------- terrain

    /// <summary>Loads one of the downloaded ambientCG maps, fixing its import settings the first time.</summary>
    internal static Texture2D ThirdParty(string asset, bool normalMap)
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

    /// <param name="brighten">Multiplies the texture's colour; used to lift photos that are darker than the landscape needs.</param>
    static TerrainLayer Layer(string name, Texture2D texture, float tile, Texture2D normal = null, float brighten = 1f)
    {
        var layer = new TerrainLayer
        {
            diffuseTexture = texture,
            normalMapTexture = normal,
            tileSize = new Vector2(tile, tile),
            smoothness = 0f,
            metallic = 0f,
            diffuseRemapMax = new Vector4(brighten, brighten, brighten, 1f),
        };
        return Save(layer, name, "terrainlayer");
    }

    /// <summary>Road centre lines to draw into the overlay; filled in by the world builder.</summary>
    internal static readonly List<List<Vector3>> OverlayRoads = new List<List<Vector3>>();

    const int OverlaySize = 4096;
    const string OverlayPath = Dir + "/TerrainOverlay.png";

    /// <summary>
    /// Paints one large aerial-photo-style picture of the whole map from the
    /// terrain's shape: every pixel is coloured by what would be there (rock
    /// on steep ground, snow up high, woodland, individual farm plots, beach,
    /// the city's street grid, roads) and shaded by the slope it lies on.
    /// Unlike a tiled texture, nothing in it repeats.
    /// </summary>
    static Texture2D BakeOverlay(TerrainData data, List<string> log)
    {
        int res = data.heightmapResolution;
        float[,] heightmap = data.GetHeights(0, 0, res, res);
        float cell = WorldSize / (res - 1);
        const int size = OverlaySize;
        float metresPerPixel = WorldSize / size;

        // Height in metres above sea level at a world position.
        float Height(float wx, float wz)
        {
            float fx = Mathf.Clamp((wx / WorldSize + 0.5f) * (res - 1), 0f, res - 1.001f);
            float fz = Mathf.Clamp((wz / WorldSize + 0.5f) * (res - 1), 0f, res - 1.001f);
            int x = (int)fx, z = (int)fz;
            float tx = fx - x, tz = fz - z;
            float h = Mathf.Lerp(Mathf.Lerp(heightmap[z, x], heightmap[z, x + 1], tx), Mathf.Lerp(heightmap[z + 1, x], heightmap[z + 1, x + 1], tx), tz);
            return h * WorldLayout.MaxHeight - SeaFloor;
        }

        // Roads are stamped into a mask first, a few pixels wide.
        var roadMask = new byte[size * size];
        foreach (List<Vector3> line in OverlayRoads)
        {
            for (int i = 1; i < line.Count; i++)
            {
                int steps = Mathf.Max(1, Mathf.CeilToInt(Vector3.Distance(line[i], line[i - 1]) / (metresPerPixel * 0.5f)));
                for (int s = 0; s <= steps; s++)
                {
                    Vector3 p = Vector3.Lerp(line[i - 1], line[i], s / (float)steps);
                    int px = (int)((p.x / WorldSize + 0.5f) * size), pz = (int)((p.z / WorldSize + 0.5f) * size);
                    for (int dz = -1; dz <= 1; dz++)
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            int x = px + dx, z = pz + dz;
                            if (x >= 0 && z >= 0 && x < size && z < size) roadMask[z * size + x] = 255;
                        }
                }
            }
        }

        Light sunLight = RenderSettings.sun != null ? RenderSettings.sun : Object.FindFirstObjectByType<Light>();
        Vector3 sun = sunLight != null ? -sunLight.transform.forward : new Vector3(0.4f, 0.8f, 0.45f).normalized;

        Color dryGrass = new Color(0.44f, 0.45f, 0.23f), lushGrass = new Color(0.19f, 0.35f, 0.13f);
        Color tundra = new Color(0.43f, 0.39f, 0.29f), sand = new Color(0.78f, 0.72f, 0.54f);
        Color forestDark = new Color(0.05f, 0.14f, 0.07f), forestLight = new Color(0.13f, 0.25f, 0.11f);
        Color rockDark = new Color(0.31f, 0.28f, 0.26f), rockLight = new Color(0.57f, 0.53f, 0.48f);
        Color snowColor = new Color(0.94f, 0.96f, 0.99f), roadColor = new Color(0.33f, 0.31f, 0.29f);
        Color[] crops =
        {
            new Color(0.30f, 0.44f, 0.18f), new Color(0.47f, 0.52f, 0.22f), new Color(0.62f, 0.56f, 0.30f),
            new Color(0.42f, 0.32f, 0.21f), new Color(0.24f, 0.38f, 0.16f), new Color(0.70f, 0.64f, 0.36f),
        };
        Rect city = WorldLayout.City;

        var pixels = new Color32[size * size];
        for (int py = 0; py < size; py++)
        {
            float v = (py + 0.5f) / size, wz = (v - 0.5f) * WorldSize;
            for (int px = 0; px < size; px++)
            {
                float u = (px + 0.5f) / size, wx = (u - 0.5f) * WorldSize;

                float h = Height(wx, wz);
                float gx = (Height(wx + cell, wz) - Height(wx - cell, wz)) / (2f * cell);
                float gz = (Height(wx, wz + cell) - Height(wx, wz - cell)) / (2f * cell);
                float slope = Mathf.Atan(Mathf.Sqrt(gx * gx + gz * gz)) * Mathf.Rad2Deg;
                Vector3 normal = new Vector3(-gx, 1f, -gz).normalized;

                float broad = Mathf.PerlinNoise(wx / 2600f + 11f, wz / 2600f + 7f);
                float mid = Mathf.PerlinNoise(wx / 420f + 3f, wz / 420f + 19f);
                float fine = Mathf.PerlinNoise(wx / 55f + 41f, wz / 55f + 5f);

                // Grassland, drier and browner with altitude.
                Color c = Color.Lerp(dryGrass, lushGrass, broad * 0.65f + mid * 0.35f) * (0.9f + fine * 0.2f);
                c = Color.Lerp(c, tundra, Step(550f, 950f, h + (mid - 0.5f) * 220f));

                // Farmland on flat valley floors: each plot gets its own crop and furrow direction.
                float flat = 1f - Step(3f, 7f, slope);
                float low = 1f - Step(90f, 180f, h);
                float farm = Step(0.42f, 0.5f, Mathf.PerlinNoise(u * 9f + 4f, v * 9f + 9f));
                float fieldsW = flat * low * farm;
                if (fieldsW > 0.01f)
                {
                    // The field pattern turns to a different angle from district to district.
                    float angle = Mathf.Floor(Mathf.PerlinNoise(wx / 3500f + 60f, wz / 3500f + 80f) * 5f) * 0.4f;
                    float ca = Mathf.Cos(angle), sa = Mathf.Sin(angle);
                    float rx = wx * ca - wz * sa, rz = wx * sa + wz * ca;
                    float fxp = rx / 230f, fzp = rz / 140f;
                    int ix = Mathf.FloorToInt(fxp), iz = Mathf.FloorToInt(fzp);
                    float inX = fxp - ix, inZ = fzp - iz;
                    Color crop = crops[(int)(Hash(ix, iz, 7) * 5.99f)];
                    float furrow = Mathf.Sin((Hash(ix, iz, 19) > 0.5f ? rx : rz) * 0.55f) * 0.05f;
                    crop *= 0.93f + Hash(ix, iz, 3) * 0.14f + furrow;
                    bool hedge = inX < 0.035f || inZ < 0.05f;
                    c = Color.Lerp(c, hedge ? forestDark * 1.4f : crop, fieldsW);
                }

                // Woodland, on the same patches the trees are planted on.
                float woods = Step(0.45f, 0.58f, Mathf.PerlinNoise(u * 26f + 30f, v * 26f + 50f));
                float treeline = 1f - Step(700f, 950f, h);
                float crowns = Mathf.PerlinNoise(wx / 13f, wz / 13f);
                c = Color.Lerp(c, Color.Lerp(forestDark, forestLight, crowns), woods * treeline * (1f - fieldsW));

                // Bare rock on steep ground, with layered strata; snow above the snow line; sand at the waterline.
                float rockW = Step(16f, 30f, slope + (fine - 0.5f) * 8f);
                float strata = Mathf.PerlinNoise(wx / 160f + 9f, h / 22f);
                c = Color.Lerp(c, Color.Lerp(rockDark, rockLight, strata * 0.7f + fine * 0.3f), rockW);
                float snowW = Step(1000f, 1350f, h + (mid - 0.5f) * 300f) * (1f - rockW * 0.45f);
                c = Color.Lerp(c, snowColor, snowW);
                c = Color.Lerp(c, sand, 1f - Step(-2f, 3.5f, h));

                // Mown grass around the airport.
                float airfield = 1f - Step(1f, 1.25f, Mathf.Sqrt((wx / 800f) * (wx / 800f) + (wz / 1750f) * (wz / 1750f)));
                c = Color.Lerp(c, new Color(0.36f, 0.46f, 0.22f) * (0.94f + fine * 0.12f), airfield * 0.75f);

                // Built-up ground: pale blocks divided by darker streets.
                float urban = WorldLayout.UrbanWeight(wx, wz);
                if (urban > 0f)
                {
                    float grey = 0.47f + fine * 0.1f;
                    if (city.Contains(new Vector2(wx, wz)))
                    {
                        float bx = (wx - city.xMin) % 170f, bz = (wz - city.yMin) % 140f;
                        if (bx < 10f || bx > 160f || bz < 10f || bz > 130f) grey = 0.24f;
                    }
                    c = Color.Lerp(c, new Color(grey, grey, grey * 1.02f), urban);
                }

                if (roadMask[py * size + px] != 0) c = Color.Lerp(c, roadColor, 0.85f);

                // Gentle baked shading: enough to give the hills relief in the picture
                // without doubling up on the live sunlight.
                float lit = Mathf.Clamp01(Vector3.Dot(normal, sun) * 0.9f + 0.1f);
                c *= Mathf.Lerp(0.8f, 1.08f, lit);

                pixels[py * size + px] = new Color32(
                    (byte)(Mathf.Clamp01(c.r) * 255f), (byte)(Mathf.Clamp01(c.g) * 255f), (byte)(Mathf.Clamp01(c.b) * 255f), 255);
            }
        }

        // Written as a normal PNG so it can be opened and inspected like any picture.
        var texture = new Texture2D(size, size, TextureFormat.RGB24, false);
        texture.SetPixels32(pixels);
        texture.Apply(false);
        System.IO.File.WriteAllBytes(OverlayPath, texture.EncodeToPNG());
        Object.DestroyImmediate(texture);
        AssetDatabase.ImportAsset(OverlayPath, ImportAssetOptions.ForceUpdate);

        var importer = (TextureImporter)AssetImporter.GetAtPath(OverlayPath);
        // No alpha channel, and a compression format without one: the terrain shader reads
        // a texture's alpha as glossiness, which would make the whole landscape look wet.
        if (importer.maxTextureSize != size || importer.wrapMode != TextureWrapMode.Clamp
            || importer.alphaSource != TextureImporterAlphaSource.None
            || importer.textureCompression != TextureImporterCompression.Compressed)
        {
            importer.maxTextureSize = size;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.anisoLevel = 16;
            importer.alphaSource = TextureImporterAlphaSource.None;
            importer.textureCompression = TextureImporterCompression.Compressed;
            importer.SaveAndReimport();
        }
        log.Add($"aerial overlay baked: {size} x {size} pixels, {metresPerPixel:0.0} m per pixel");
        return AssetDatabase.LoadAssetAtPath<Texture2D>(OverlayPath);
    }

    internal static void BuildTerrainPaint(List<string> log)
    {
        Terrain terrain = Terrain.activeTerrain;
        if (terrain == null) return;
        TerrainData data = terrain.terrainData;

        Texture2D grass = Tex("GrassTex", 256, false, (u, v, x, y) =>
        {
            float n = TileFbm(u, v, 2f, 3f, 4);
            float fine = TileNoise(u, v, 24f, 8f);
            Color c = Color.Lerp(new Color(0.20f, 0.31f, 0.14f), new Color(0.40f, 0.48f, 0.24f), n);
            return c * (0.9f + fine * 0.2f);
        });
        Texture2D rock = Tex("RockTex", 256, false, (u, v, x, y) =>
        {
            float n = TileFbm(u, v, 3f, 7f, 4);
            // Stretched noise reads as strata.
            float strata = TileNoise(u * 0.25f, v, 14f, 2f);
            Color c = Color.Lerp(new Color(0.27f, 0.24f, 0.21f), new Color(0.56f, 0.52f, 0.46f), n * 0.6f + strata * 0.4f);
            return c;
        });
        Texture2D snow = Tex("SnowTex", 128, false, (u, v, x, y) =>
        {
            float n = TileFbm(u, v, 3f, 11f, 3);
            return Color.Lerp(new Color(0.78f, 0.82f, 0.88f), new Color(0.98f, 0.98f, 1f), n);
        });
        Texture2D forest = Tex("ForestTex", 256, false, (u, v, x, y) =>
        {
            // Tight blobs of dark and darker green: tree crowns seen from above.
            float crowns = TileNoise(u, v, 22f, 5f);
            float patch = TileFbm(u, v, 2f, 13f, 3);
            Color c = Color.Lerp(new Color(0.07f, 0.16f, 0.07f), new Color(0.17f, 0.30f, 0.13f), crowns);
            return c * (0.85f + patch * 0.3f);
        });
        Texture2D fields = Tex("FieldsTex", 256, false, (u, v, x, y) =>
        {
            // A patchwork of farm plots, each its own crop colour and furrow direction.
            const int plots = 5;
            int px = (int)(u * plots), py = (int)(v * plots);
            float pick = Hash(px, py, 7);
            Color crop = pick < 0.3f ? new Color(0.33f, 0.45f, 0.20f)
                : pick < 0.55f ? new Color(0.50f, 0.52f, 0.24f)
                : pick < 0.8f ? new Color(0.58f, 0.50f, 0.30f)
                : new Color(0.40f, 0.31f, 0.20f);
            float along = Hash(px, py, 19) > 0.5f ? u : v;
            float furrow = Mathf.Sin(along * 256f * 0.8f) * 0.05f;
            float fu = u * plots % 1f, fv = v * plots % 1f;
            bool hedge = fu < 0.04f || fv < 0.04f;
            Color c = crop * (1f + furrow);
            return hedge ? new Color(0.12f, 0.2f, 0.1f) : c;
        });

        Texture2D paved = Tex("PavedTex", 128, false, (u, v, x, y) =>
        {
            float n = 0.4f + TileFbm(u, v, 3f, 6f, 3) * 0.14f;
            // Paving slabs.
            if (x % 16 == 0 || y % 16 == 0) n *= 0.88f;
            return new Color(n, n, n * 0.98f);
        });

        // Fine soil texture for farmland; the plots themselves come from the overlay.
        Texture2D crop = Tex("CropDetailTex", 128, false, (u, v, x, y) =>
        {
            float furrow = 0.5f + Mathf.Sin(u * Mathf.PI * 32f) * 0.07f + TileNoise(u, v, 14f, 6f) * 0.1f;
            return new Color(furrow * 1.02f, furrow * 0.97f, furrow * 0.78f);
        });
        Texture2D overlay = BakeOverlay(data, log);

        Texture2D grassPhoto = ThirdParty("Grass001", false);
        Texture2D rockPhoto = ThirdParty("Rock058", false);

        data.terrainLayers = new[]
        {
            // Photo grass and rock from ambientCG when present. They tile much smaller than the
            // generated versions, trading broad colour variation for detail when flying low.
            grassPhoto != null ? Layer("GrassLayer", grassPhoto, 70f, ThirdParty("Grass001", true)) : Layer("GrassLayer", grass, 420f),
            rockPhoto != null ? Layer("RockLayer", rockPhoto, 150f, ThirdParty("Rock058", true), 1.5f) : Layer("RockLayer", rock, 650f),
            Layer("SnowLayer", snow, 900f),
            Layer("ForestLayer", forest, 160f),
            Layer("FieldsLayer", crop, 45f),
            // Photo asphalt from ambientCG if it has been downloaded, otherwise the generated paving.
            Layer("PavedLayer", AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/ThirdParty/ambientCG/Asphalt031/Asphalt031_1K-JPG_Color.jpg") ?? paved, 45f),
            // The aerial picture, stretched once over the entire map.
            Layer("OverlayLayer", overlay, WorldSize),
        };

        int res = data.alphamapResolution;
        // Share of the ground colour that comes from the close-up detail textures; the rest is the aerial picture.
        const float detailShare = 0.36f;
        var alpha = new float[res, res, 7];
        for (int z = 0; z < res; z++)
        {
            for (int x = 0; x < res; x++)
            {
                float u = x / (float)(res - 1), v = z / (float)(res - 1);
                float height = data.GetInterpolatedHeight(u, v) - SeaFloor;
                float steep = data.GetSteepness(u, v);
                float jitter = Mathf.PerlinNoise(u * 60f, v * 60f) - 0.5f;

                float rockW = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(16f, 30f, steep + jitter * 6f));
                // Bare band along the waterline reads as a beach.
                rockW = Mathf.Max(rockW, 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-2f, 3f, height)));
                float snowW = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(1000f, 1350f, height + jitter * 250f)) * (1f - rockW * 0.45f);
                rockW *= 1f - snowW;
                float green = Mathf.Max(0f, 1f - rockW - snowW);

                // Farmland on the flat valley floors, woodland in patches on the hillsides.
                float flat = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(3f, 7f, steep));
                float low = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(90f, 180f, height));
                float farm = Step(0.42f, 0.5f, Mathf.PerlinNoise(u * 9f + 4f, v * 9f + 9f));
                float fieldsW = green * flat * low * farm;

                float woods = Step(0.45f, 0.58f, Mathf.PerlinNoise(u * 26f + 30f, v * 26f + 50f));
                float treeline = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(700f, 950f, height));
                float forestW = (green - fieldsW) * woods * treeline;

                // Built-up ground is paved over, whatever the landscape underneath wanted.
                float urban = WorldLayout.UrbanWeight((u - 0.5f) * WorldSize, (v - 0.5f) * WorldSize);
                float natural = 1f - urban;

                alpha[z, x, 0] = Mathf.Max(0f, green - fieldsW - forestW) * natural;
                alpha[z, x, 1] = rockW * natural;
                alpha[z, x, 2] = snowW * natural;
                alpha[z, x, 3] = forestW * natural;
                alpha[z, x, 4] = fieldsW * natural;
                alpha[z, x, 5] = urban;

                for (int layer = 0; layer < 6; layer++) alpha[z, x, layer] *= detailShare;
                alpha[z, x, 6] = 1f - detailShare;
            }
        }
        data.SetAlphamaps(0, 0, alpha);
        EditorUtility.SetDirty(data);
        terrain.Flush();
        log.Add("terrain repainted");
    }

    // ----------------------------------------------------------------- quality

    static void ApplyQuality(List<string> log)
    {
        var urp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
        if (urp == null)
        {
            log.Add("no URP asset active; quality settings unchanged");
            return;
        }

        // Far enough for the plane's shadow on the deck and buildings' shadows on approach.
        urp.shadowDistance = 600f;
        urp.shadowCascadeCount = 4;
        urp.msaaSampleCount = 4;
        EditorUtility.SetDirty(urp);
        QualitySettings.anisotropicFiltering = AnisotropicFiltering.ForceEnable;
        log.Add($"quality: 4x anti-aliasing, shadows to {urp.shadowDistance:0} m on '{urp.name}'");
    }
}

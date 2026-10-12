using UnityEngine;

/// <summary>
/// The map on paper: how big the world is, where the natural land and sea lie,
/// and where each built-up site sits. Shared by the builders so terrain shape,
/// terrain paint and placed objects always agree.
/// </summary>
public static class WorldLayout
{
    public const float WorldSize = 40000f;
    public const float MaxHeight = 2600f;
    /// <summary>The terrain object sits this far below sea level so the sea bed can drop under the water.</summary>
    public const float SeaFloor = 8f;
    public const int Resolution = 1025;

    // Coastal city east of the airport, with the port and refinery on its waterfront.
    public static readonly Rect City = Rect.MinMaxRect(3500f, -5700f, 6900f, -2900f);
    public const float CityHeight = 12f;
    public static readonly Rect Port = Rect.MinMaxRect(3500f, -7000f, 6900f, -6250f);
    public const float PortHeight = 5f;
    /// <summary>The quay edge: land to the north of this line, harbour water to the south.</summary>
    public const float QuayZ = -7000f;

    /// <summary>Where the mountain observatory was placed; found by the world builder (the highest peak in the north-west).</summary>
    public static Vector3 Observatory;

    /// <summary>0 below edge0, 1 above edge1, smooth in between.</summary>
    public static float Step(float edge0, float edge1, float x)
    {
        return Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(edge0, edge1, x));
    }

    /// <summary>Distance from a point to a rectangle on the ground; 0 anywhere inside it.</summary>
    public static float Outside(Rect rect, float x, float z)
    {
        float dx = Mathf.Max(rect.xMin - x, 0f, x - rect.xMax);
        float dz = Mathf.Max(rect.yMin - z, 0f, z - rect.yMax);
        return Mathf.Sqrt(dx * dx + dz * dz);
    }

    /// <summary>1 on paved, built-up ground (city and port), fading to 0 just outside.</summary>
    public static float UrbanWeight(float x, float z)
    {
        float city = 1f - Step(0f, 80f, Outside(City, x, z));
        float port = 1f - Step(0f, 60f, Outside(Port, x, z));
        return Mathf.Max(city, port);
    }

    /// <summary>
    /// Height in metres of the untouched landscape: a flat plain around the
    /// airport, hills rising into mountains inland, valleys winding through,
    /// and sea across the south. Sea bed returns -SeaFloor.
    /// </summary>
    public static float NaturalHeight(float wx, float wz)
    {
        float r = Mathf.Sqrt(wx * wx + wz * wz);

        float coastZ = -7200f + (Mathf.PerlinNoise(wx / 3200f + 700f, 0.5f) - 0.5f) * 1800f;
        float landMask = Step(coastZ - 500f, coastZ + 500f, wz);

        // Only the airport itself is flat: an oval around the runway.
        float baseDist = Mathf.Sqrt((wx / 800f) * (wx / 800f) + (wz / 1750f) * (wz / 1750f));
        float baseMask = Step(1f, 2.3f, baseDist);
        // Hills near the airport grow into full mountains further out, and shrink toward the shore.
        float envelope = Mathf.Lerp(0.30f, 1f, Step(2500f, 9500f, r));
        float shore = Step(coastZ, coastZ + 3500f, wz);

        float sum = 0f, amplitude = 1f, frequency = 1f / 5200f, total = 0f;
        for (int octave = 0; octave < 6; octave++)
        {
            float p = Mathf.PerlinNoise(wx * frequency + 100f + octave * 37.1f, wz * frequency + 200f + octave * 53.7f);
            float ridge = 1f - Mathf.Abs(p * 2f - 1f);
            sum += (octave < 2 ? ridge * ridge : p) * amplitude;
            total += amplitude;
            amplitude *= 0.5f;
            frequency *= 2.1f;
        }
        float h = Mathf.Pow(Mathf.Clamp01(sum / total), 1.6f);

        // Broad valleys winding through the hills, so there are low routes to fly.
        float valley = Mathf.Abs(Mathf.PerlinNoise(wx / 6500f + 300f, wz / 6500f + 500f) * 2f - 1f);
        float valleyMask = Step(0.02f, 0.28f, valley);

        float land = baseMask * envelope * h * Mathf.Lerp(0.12f, 1f, valleyMask) * Mathf.Lerp(0.15f, 1f, shore);
        return landMask * (SeaFloor + land * MaxHeight) - SeaFloor;
    }
}

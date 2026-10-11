using UnityEngine;

/// <summary>
/// Temporary immediate-mode combat HUD: target boxes, lock indicator,
/// off-screen target arrow, gun reticle, missile status and kill messages.
/// </summary>
public class CombatHud : MonoBehaviour
{
    public WeaponSystem weapons;
    public float gunReticleDistance = 900f;
    public float messageDuration = 2.5f;

    static readonly Color HudGreen = new Color(0.4f, 1f, 0.4f);
    static readonly Color HudRed = new Color(1f, 0.25f, 0.2f);

    // The HUD is laid out for a 720-pixel-tall screen and scaled to fit.
    const float ReferenceHeight = 720f;
    static float scale = 1f;
    static float ViewWidth => Screen.width / scale;
    static float ViewHeight => Screen.height / scale;

    // NonSerialized: otherwise Unity restores these as blank default styles after a script reload.
    [System.NonSerialized] GUIStyle label;
    [System.NonSerialized] GUIStyle bigLabel;
    string message;
    float messageUntil;
    bool hadTargets;

    void OnEnable() => Target.AnyDestroyed += OnTargetDestroyed;
    void OnDisable() => Target.AnyDestroyed -= OnTargetDestroyed;

    void OnTargetDestroyed(Target target)
    {
        message = "DESTROYED";
        messageUntil = Time.time + messageDuration;
    }

    void OnGUI()
    {
        Camera cam = Camera.main;
        if (cam == null || weapons == null) return;

        if (label == null)
        {
            label = new GUIStyle(GUI.skin.label) { fontSize = 18, fontStyle = FontStyle.Bold, alignment = TextAnchor.UpperCenter };
            bigLabel = new GUIStyle(label) { fontSize = 34 };
        }

        if (Target.All.Count > 0) hadTargets = true;

        scale = Screen.height / ReferenceHeight;
        label.fontSize = Mathf.RoundToInt(18f * scale);
        bigLabel.fontSize = Mathf.RoundToInt(34f * scale);

        DrawTargets(cam);
        DrawGunReticle(cam);
        DrawWeaponStatus();
        DrawMessages();
        GUI.color = Color.white;
    }

    void DrawTargets(Camera cam)
    {
        Target current = weapons.CurrentTarget;

        foreach (Target t in Target.All)
        {
            if (t == current) continue;
            if (ToGui(cam, t.transform.position, out Vector2 p)) Box(p, 14f, HudGreen, 1f);
        }

        if (current == null) return;

        float distance = Vector3.Distance(weapons.transform.position, current.transform.position);
        Color color = weapons.IsLocked ? HudRed : HudGreen;

        if (ToGui(cam, current.transform.position, out Vector2 pos))
        {
            Box(pos, 26f, color, 2f);

            // Lock ring closes in on the box while the lock builds.
            if (weapons.LockProgress > 0f && !weapons.IsLocked)
                Box(pos, Mathf.Lerp(90f, 30f, weapons.LockProgress), HudRed, 1f);

            GUI.color = color;
            GUI.Label(R(pos.x - 100f, pos.y + 30f, 200f, 24f), $"{current.displayName}  {distance:0} m", label);
            if (weapons.IsLocked)
                GUI.Label(R(pos.x - 100f, pos.y - 56f, 200f, 24f), "LOCK", label);
        }
        else
        {
            // Off screen: point toward it from the centre of the view.
            Vector3 local = cam.transform.InverseTransformPoint(current.transform.position);
            Vector2 dir = new Vector2(local.x, -local.y);
            if (dir.sqrMagnitude < 0.0001f) dir = Vector2.up;
            dir.Normalize();

            Vector2 centre = new Vector2(ViewWidth, ViewHeight) * 0.5f;
            float radius = ViewHeight * 0.38f;
            for (int i = 0; i < 4; i++)
                Fill(centre + dir * (radius - i * 14f), 10f - i * 2f, color);

            Vector2 textPos = centre + dir * (radius - 80f);
            GUI.color = color;
            GUI.Label(R(textPos.x - 100f, textPos.y - 12f, 200f, 24f), $"{distance:0} m", label);
        }
    }

    void DrawGunReticle(Camera cam)
    {
        Vector3 aim = weapons.transform.position + weapons.transform.forward * gunReticleDistance;
        if (!ToGui(cam, aim, out Vector2 p)) return;

        GUI.color = HudGreen;
        GUI.DrawTexture(R(p.x - 12f, p.y - 1f, 8f, 2f), Texture2D.whiteTexture);
        GUI.DrawTexture(R(p.x + 4f, p.y - 1f, 8f, 2f), Texture2D.whiteTexture);
        GUI.DrawTexture(R(p.x - 1f, p.y - 12f, 2f, 8f), Texture2D.whiteTexture);
        GUI.DrawTexture(R(p.x - 1f, p.y + 4f, 2f, 8f), Texture2D.whiteTexture);
    }

    void DrawWeaponStatus()
    {
        float x = ViewWidth - 220f;
        float y = ViewHeight - 90f;

        GUI.color = HudGreen;
        label.alignment = TextAnchor.UpperLeft;
        GUI.Label(R(x, y, 200f, 24f), $"MSL  {weapons.MissileAmmo}", label);
        GUI.Label(R(x, y + 48f, 200f, 24f), $"TGT  {Target.All.Count}", label);
        label.alignment = TextAnchor.UpperCenter;

        for (int i = 0; i < weapons.RailCount; i++)
        {
            float ready = weapons.RailReadiness(i);
            float barX = x + i * 70f, barY = y + 28f;
            GUI.color = new Color(HudGreen.r, HudGreen.g, HudGreen.b, 0.25f);
            GUI.DrawTexture(R(barX, barY, 60f, 10f), Texture2D.whiteTexture);
            GUI.color = ready >= 1f ? HudGreen : HudRed;
            GUI.DrawTexture(R(barX, barY, 60f * ready, 10f), Texture2D.whiteTexture);
        }
    }

    void DrawMessages()
    {
        Rect line = R(0f, ViewHeight * 0.22f, ViewWidth, 44f);

        if (hadTargets && Target.All.Count == 0)
        {
            GUI.color = HudGreen;
            GUI.Label(line, "MISSION ACCOMPLISHED", bigLabel);
        }
        else if (Time.time < messageUntil)
        {
            GUI.color = HudRed;
            GUI.Label(line, message, bigLabel);
        }
    }

    /// <summary>Rect in 720p layout units, converted to real pixels.</summary>
    static Rect R(float x, float y, float width, float height)
    {
        return new Rect(x * scale, y * scale, width * scale, height * scale);
    }

    /// <summary>World point to HUD layout units. False if behind the camera or off screen.</summary>
    static bool ToGui(Camera cam, Vector3 world, out Vector2 gui)
    {
        Vector3 s = cam.WorldToScreenPoint(world);
        gui = new Vector2(s.x, Screen.height - s.y) / scale;
        return s.z > 0f && s.x >= 0f && s.x <= Screen.width && s.y >= 0f && s.y <= Screen.height;
    }

    static void Box(Vector2 centre, float size, Color color, float thickness)
    {
        float h = size * 0.5f;
        GUI.color = color;
        GUI.DrawTexture(R(centre.x - h, centre.y - h, size, thickness), Texture2D.whiteTexture);
        GUI.DrawTexture(R(centre.x - h, centre.y + h - thickness, size, thickness), Texture2D.whiteTexture);
        GUI.DrawTexture(R(centre.x - h, centre.y - h, thickness, size), Texture2D.whiteTexture);
        GUI.DrawTexture(R(centre.x + h - thickness, centre.y - h, thickness, size), Texture2D.whiteTexture);
    }

    static void Fill(Vector2 centre, float size, Color color)
    {
        GUI.color = color;
        GUI.DrawTexture(R(centre.x - size * 0.5f, centre.y - size * 0.5f, size, size), Texture2D.whiteTexture);
    }
}

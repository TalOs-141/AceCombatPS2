using UnityEngine;

/// <summary>
/// Temporary on-screen readout of speed/altitude/heading until the real HUD exists.
/// </summary>
public class FlightDebugHud : MonoBehaviour
{
    public PlaneController plane;

    // NonSerialized: otherwise Unity restores this as a blank default style after a script reload.
    [System.NonSerialized] GUIStyle style;

    void OnGUI()
    {
        if (plane == null) return;
        if (style == null)
        {
            style = new GUIStyle(GUI.skin.label) { fontSize = 22, fontStyle = FontStyle.Bold };
            style.normal.textColor = new Color(0.4f, 1f, 0.4f);
        }

        float scale = Screen.height / 720f;
        style.fontSize = Mathf.RoundToInt(20f * scale);

        float heading = plane.transform.eulerAngles.y;
        GUI.Label(new Rect(20 * scale, 20 * scale, 400 * scale, 30 * scale), $"SPEED  {plane.Speed * 3.6f:0} km/h", style);
        GUI.Label(new Rect(20 * scale, 50 * scale, 400 * scale, 30 * scale), $"ALT    {plane.Altitude:0} m", style);
        GUI.Label(new Rect(20 * scale, 80 * scale, 400 * scale, 30 * scale), $"HDG    {heading:000}", style);
    }
}

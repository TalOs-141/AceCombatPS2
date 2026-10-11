using UnityEngine;

/// <summary>
/// Temporary on-screen readout of speed/altitude/heading until the real HUD exists.
/// </summary>
public class FlightDebugHud : MonoBehaviour
{
    public PlaneController plane;

    GUIStyle style;

    void OnGUI()
    {
        if (plane == null) return;
        if (style == null)
        {
            style = new GUIStyle(GUI.skin.label) { fontSize = 22, fontStyle = FontStyle.Bold };
            style.normal.textColor = new Color(0.4f, 1f, 0.4f);
        }

        float heading = plane.transform.eulerAngles.y;
        GUI.Label(new Rect(20, 20, 400, 30), $"SPEED  {plane.Speed * 3.6f:0} km/h", style);
        GUI.Label(new Rect(20, 50, 400, 30), $"ALT    {plane.Altitude:0} m", style);
        GUI.Label(new Rect(20, 80, 400, 30), $"HDG    {heading:000}", style);
    }
}

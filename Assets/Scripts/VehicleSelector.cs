using System;
using UnityEngine;

/// <summary>
/// The player's hangar. Every flyable vehicle's model sits under the player,
/// switched off; at the start of a mission this turns on the chosen one and
/// sets the flight model, effects and camera to suit it. The choice is made in
/// the pause menu and remembered between sessions.
/// </summary>
[RequireComponent(typeof(PlaneController))]
public class VehicleSelector : MonoBehaviour
{
    [Serializable]
    public class Vehicle
    {
        public string displayName = "Vehicle";
        [Tooltip("Root of this vehicle's model; enabled only while it is the selected one.")]
        public GameObject body;
        public bool helicopter;
        public Transform rotor;
        public Transform tailRotor;
        [Tooltip("Show the generic landing-gear legs (off for craft with their own, such as skids).")]
        public bool showGear = true;
        public bool showAfterburner = true;
        [Tooltip("Pilot's eye position for the cockpit view, in the player's local space.")]
        public Vector3 cockpitOffset = new Vector3(0f, 1.05f, 3.1f);
    }

    public Vehicle[] vehicles = new Vehicle[0];

    const string PrefKey = "AceCombat.Vehicle";

    public static int Selected
    {
        get => PlayerPrefs.GetInt(PrefKey, 0);
        set
        {
            PlayerPrefs.SetInt(PrefKey, value);
            PlayerPrefs.Save();
        }
    }

    public int Count => vehicles.Length;
    public Vehicle Current => vehicles.Length == 0 ? null : vehicles[Mathf.Clamp(Selected, 0, vehicles.Length - 1)];

    public string NameOf(int index) => vehicles.Length == 0 ? "" : vehicles[(index % vehicles.Length + vehicles.Length) % vehicles.Length].displayName;

    void Awake()
    {
        if (vehicles.Length == 0) return;
        int chosen = Mathf.Clamp(Selected, 0, vehicles.Length - 1);
        for (int i = 0; i < vehicles.Length; i++)
            if (vehicles[i].body != null) vehicles[i].body.SetActive(i == chosen);

        Vehicle vehicle = vehicles[chosen];
        var plane = GetComponent<PlaneController>();
        plane.helicopter = vehicle.helicopter;
        plane.rotor = vehicle.rotor;
        plane.tailRotor = vehicle.tailRotor;

        // Hand the plane only the effects this vehicle should show.
        if (!vehicle.showGear && plane.gearRoot != null)
        {
            plane.gearRoot.gameObject.SetActive(false);
            plane.gearRoot = null;
        }
        if (!vehicle.showAfterburner && plane.afterburner != null)
        {
            plane.afterburner.gameObject.SetActive(false);
            plane.afterburner = null;
        }

        Camera main = Camera.main;
        ChaseCamera chase = main != null ? main.GetComponent<ChaseCamera>() : null;
        if (chase != null && vehicle.body != null)
        {
            // In the cockpit view the pilot shouldn't be looking through their own airframe.
            chase.hideInCockpit = vehicle.body.GetComponentsInChildren<Renderer>();
            chase.cockpitOffset = vehicle.cockpitOffset;
        }
    }
}

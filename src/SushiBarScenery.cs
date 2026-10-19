using UnityEngine;

namespace DaveTheDiverUltrawide;

/// <summary>
/// The sushi bar is a 2D building in front of the 3D lobby environment. On a 16:9 screen the
/// building covers the ground mesh Lobby_Env_SushiBar/Lobby_Ground001/Ground000 completely, but on
/// ultrawide its far end shows right of the kitchen as a flat brown slab. Hiding it leaves open sea
/// there, which fits a floating restaurant (nothing of it is visible inside the 16:9 area).
/// </summary>
public static class SushiBarScenery
{
    const string EnvRoot = "Lobby_Env_SushiBar";
    const string GroundPath = "Lobby_Ground001/Ground000";
    const float CheckInterval = 0.5f;

    static Renderer _ground;
    static float _nextCheck;

    static bool Active => Plugin.Enabled.Value && UltrawideBehaviour.IsWide();

    public static void Update()
    {
        if (_ground == null)
        {
            if (Time.unscaledTime < _nextCheck) return;
            _nextCheck = Time.unscaledTime + CheckInterval;
            // GameObject.Find walks every active object (~0.3 ms in a dive), so only look while the
            // sushi bar camera (Camera_SushiBar/MainCamera) is the main camera.
            var cam = Camera.main;
            if (cam == null || !cam.transform.root.name.Contains("SushiBar")) return;
            var env = GameObject.Find(EnvRoot);
            var t = env != null ? env.transform.Find(GroundPath) : null;
            _ground = t != null ? t.GetComponent<Renderer>() : null;
            if (_ground == null) return;
        }

        bool show = !Active;
        if (_ground.enabled != show)
        {
            _ground.enabled = show;
            if (Plugin.DebugLog) Plugin.L.LogInfo($"Sushi bar ground mesh {(show ? "shown" : "hidden")}");
        }
    }
}

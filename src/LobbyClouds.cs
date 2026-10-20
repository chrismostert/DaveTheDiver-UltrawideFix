using System.Collections.Generic;
using UnityEngine;

namespace DaveTheDiverUltrawide;

/// <summary>
/// The sky on the boat scene is made of flat cloud cards (Mesh_QuantumMetaCloud_* meshes, generated
/// around the cloud texture inside a 10-unit-wide quad) in the weather prefab under Lobby_Env/Env.
/// Most cards end in a soft, faded tip, but one cloud texture (Lobby_Cloud02) is opaque right up to
/// its border, so its card ends in a straight cut. That end lies just outside the 16:9 view; at 32:9
/// it shows as a hard vertical edge in the sky left of the boat. When that end is on screen in a side
/// area, the card is stretched along its length (the other end stays put) until the end is off screen.
/// </summary>
public static class LobbyClouds
{
    const string EnvRoot = "Lobby_Env";
    const float CheckInterval = 0.5f;
    const float MaxStretch = 2f;
    const float Margin = 0.03f; // of the screen width, so small camera moves don't reveal the end

    // mesh name -> the local x end (-1/+1) where the cloud is cut off. Found by sampling every
    // Lobby_CloudNN texture's mask (green channel) along the UV column of each mesh x end; this is
    // the only end that is opaque at the border. Used by the rain and storm materials (*Rain01, *Storm01).
    static readonly Dictionary<string, int> Cuts = new() { ["Mesh_QuantumMetaCloud_2"] = 1 };

    class Card
    {
        public Transform T;
        public Vector3 Pos, Scale;
    }

    static readonly List<Card> _cards = new();
    static readonly List<int> _ids = new();
    static GameObject _env;
    static float _nextCheck;
    static int _w, _h;
    static bool _active;

    static bool Active => Plugin.Enabled.Value && UltrawideBehaviour.IsWide();

    public static void Update()
    {
        if (Time.unscaledTime < _nextCheck) return;
        _nextCheck = Time.unscaledTime + CheckInterval;

        // Only look while the boat camera (Camera_Lobby/MainCamera) is the main camera.
        var cam = Camera.main;
        if (cam == null || cam.transform.root.name != "Camera_Lobby") return;
        if (_env == null)
        {
            _cards.Clear();
            _ids.Clear();
            _env = GameObject.Find(EnvRoot);
            if (_env == null) return;
        }

        // The weather prefab (Lobby_Rain(Clone) etc.) is swapped at runtime: refit when the set of
        // active mesh renderers changes, the resolution changes or the fix is switched on/off.
        var rends = _env.GetComponentsInChildren<MeshRenderer>(false);
        bool changed = rends.Length != _ids.Count;
        for (int i = 0; !changed && i < rends.Length; i++) changed = rends[i].GetInstanceID() != _ids[i];
        bool active = Active;
        if (!changed && active == _active && Screen.width == _w && Screen.height == _h) return;

        Restore();
        _ids.Clear();
        foreach (var r in rends) _ids.Add(r.GetInstanceID());
        _active = active;
        _w = Screen.width;
        _h = Screen.height;
        if (!active) return;

        foreach (var r in rends)
        {
            var mf = r.GetComponent<MeshFilter>();
            var mesh = mf != null ? mf.sharedMesh : null;
            if (mesh == null || !Cuts.TryGetValue(mesh.name, out int side)) continue;
            Fit(r.transform, mesh.bounds, side, cam);
        }
    }

    /// <summary>Stretches the card along its local x axis if its cut end is on screen in a side area.</summary>
    static void Fit(Transform t, Bounds lb, int side, Camera cam)
    {
        float w = Screen.width, h = Screen.height;
        float strip = h * 16f / 9f, l = (w - strip) / 2, r = w - l;
        float end = lb.center.x + side * lb.extents.x, opp = lb.center.x - side * lb.extents.x;
        if (!Project(t, lb, end, cam, out float x0, out float x1, out float y0, out float y1)) return;
        if (y1 < 0 || y0 > h) return;
        bool left = x1 < l && x1 > 0, right = x0 > r && x0 < w;
        if (!left && !right) return;

        // smallest stretch that moves the end off screen
        float k = 1f;
        while (k < MaxStretch - 0.01f)
        {
            k += 0.05f;
            if (!Project(t, lb, opp + k * (end - opp), cam, out x0, out x1, out _, out _)) { k -= 0.05f; break; }
            if (left ? x1 < -Margin * w : x0 > w * (1 + Margin)) break;
        }
        if (k <= 1f) return;

        _cards.Add(new Card { T = t, Pos = t.localPosition, Scale = t.localScale });
        var anchor = new Vector3(opp, lb.center.y, lb.center.z);
        var fixedPoint = t.TransformPoint(anchor);
        var s = t.localScale;
        t.localScale = new Vector3(s.x * k, s.y, s.z);
        t.position += fixedPoint - t.TransformPoint(anchor);
        if (Plugin.DebugLog) Plugin.L.LogInfo($"Cloud card {t.name} stretched {k:F2}x ({(left ? "left" : "right")} side)");
    }

    /// <summary>Screen extent of the card's cross-section at local x; false if it reaches behind the camera.</summary>
    static bool Project(Transform t, Bounds lb, float x, Camera cam, out float x0, out float x1, out float y0, out float y1)
    {
        x0 = y0 = float.MaxValue;
        x1 = y1 = float.MinValue;
        for (int i = 0; i < 4; i++)
        {
            var p = new Vector3(x, lb.center.y + ((i & 1) == 0 ? -1 : 1) * lb.extents.y, lb.center.z + ((i & 2) == 0 ? -1 : 1) * lb.extents.z);
            var s = cam.WorldToScreenPoint(t.TransformPoint(p));
            if (s.z <= cam.nearClipPlane) return false;
            x0 = Mathf.Min(x0, s.x); x1 = Mathf.Max(x1, s.x);
            y0 = Mathf.Min(y0, s.y); y1 = Mathf.Max(y1, s.y);
        }
        return true;
    }

    static void Restore()
    {
        foreach (var c in _cards)
        {
            if (c.T == null) continue;
            c.T.localPosition = c.Pos;
            c.T.localScale = c.Scale;
        }
        _cards.Clear();
    }
}

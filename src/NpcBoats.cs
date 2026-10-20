using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace DaveTheDiverUltrawide;

/// <summary>
/// Story NPC boats (Bacon, Sato, Clara, ...) wait under the boat scene's NPCBoatDock at parking spots
/// just outside the 16:9 view and sail in from there when an event calls them. On ultrawide the
/// parked boats peek in at both screen edges. This hides a parked boat (renderers and lights only, so
/// animations and story logic are untouched) from when the scene starts or it has left again until
/// the game calls it in. As a fallback a hidden boat is shown as soon as it moves away from its
/// parking spot, in case something else (e.g. a cutscene timeline) moves it.
/// </summary>
public static class NpcBoats
{
    const float MoveThreshold = 1f;   // world units along x; the floating bob is much smaller

    class Boat
    {
        public Transform T;
        public int Id; // instance ID of T's GameObject
        public float ParkX;
        public bool HaveParkX;
        public bool Hidden;
        public readonly List<Renderer> Renderers = new();
        public readonly List<Light> Lights = new();
        public readonly List<Renderer> HiddenRenderers = new();
        public readonly List<Light> HiddenLights = new();
    }

    static LobbyNPCBoatDockCtrl _dock;
    static readonly List<Boat> Boats = new();
    // Instance IDs of boat objects the game has called in (never reused within a session).
    static readonly HashSet<int> InUse = new();
    static int _recheck; // the hidden boat whose renderers are rechecked this frame (one per frame)
    internal static int Reenabled; // renderers/lights found turned back on while hidden

    static bool Active => Plugin.Enabled.Value && UltrawideBehaviour.IsWide();

    public static void Init(Harmony harmony) => harmony.PatchAll(typeof(Hooks));

    [HarmonyPatch]
    static class Hooks
    {
        [HarmonyPostfix, HarmonyPatch(typeof(LobbyNPCBoatToggle), nameof(LobbyNPCBoatToggle.Start))]
        static void ToggleStart(LobbyNPCBoatToggle __instance)
        {
            try { SetDock(__instance.GetComponent<LobbyNPCBoatDockCtrl>()); }
            catch (Exception e) { Plugin.L.LogError(e); }
        }

        [HarmonyPrefix, HarmonyPatch(typeof(LobbyNPCBoatDockCtrl), nameof(LobbyNPCBoatDockCtrl.EnterNPCBoat))]
        static void Enter(LobbyNPCBoatDockCtrl __instance, CharacterType type) => Called(__instance, type, "enters");

        [HarmonyPrefix, HarmonyPatch(typeof(LobbyNPCBoatDockCtrl), nameof(LobbyNPCBoatDockCtrl.AppearedNPCBoat))]
        static void Appeared(LobbyNPCBoatDockCtrl __instance, CharacterType type) => Called(__instance, type, "docked");

        [HarmonyPostfix, HarmonyPatch(typeof(LobbyNPCBoatDockCtrl), nameof(LobbyNPCBoatDockCtrl.OnExitFinished))]
        static void ExitFinished(LobbyNPCBoatDockCtrl __instance)
        {
            try
            {
                // m_CurrentBoatInfo stays set after the boat has left.
                var t = Target(__instance.m_CurrentBoatInfo);
                if (t != null && InUse.Remove(t.gameObject.GetInstanceID()) && Plugin.DebugLog)
                    Plugin.L.LogInfo($"NPC boat {t.name} left");
            }
            catch (Exception e) { Plugin.L.LogError(e); }
        }

        static void Called(LobbyNPCBoatDockCtrl dock, CharacterType type, string what)
        {
            try
            {
                SetDock(dock);
                var boats = dock.NPCBoats;
                if (boats == null) return;
                foreach (var info in boats)
                {
                    if (info == null || info.character != type) continue;
                    var t = Target(info);
                    if (t == null) continue;
                    InUse.Add(t.gameObject.GetInstanceID());
                    var b = Boats.Find(x => x.T == t);
                    if (b != null) Show(b); // visible from this frame on, before the animation starts
                    if (Plugin.DebugLog) Plugin.L.LogInfo($"NPC boat {t.name} {what}");
                }
            }
            catch (Exception e) { Plugin.L.LogError(e); }
        }
    }

    /// <summary>The object that represents a boat: its animator, or the spawn root for boats loaded on demand.</summary>
    static Transform Target(LobbyNPCBoatDockCtrl.NPCBoatInfo info)
    {
        if (info == null) return null;
        if (info.useAssetReference) return info.root;
        return info.boatAnimCtrol != null ? info.boatAnimCtrol.transform : null;
    }

    static void SetDock(LobbyNPCBoatDockCtrl dock)
    {
        if (dock == null || (_dock != null && _dock.Pointer == dock.Pointer)) return;
        _dock = dock;
        Boats.Clear();
        var infos = dock.NPCBoats;
        if (infos == null) return;
        foreach (var info in infos)
        {
            var t = Target(info);
            if (t != null) Boats.Add(new Boat { T = t, Id = t.gameObject.GetInstanceID() });
        }
        if (Plugin.DebugLog) Plugin.L.LogInfo($"NPC boat dock found with {Boats.Count} boats");
    }

    /// <summary>Called every LateUpdate.</summary>
    public static void Update()
    {
        if (Boats.Count == 0) return;
        if (_dock == null) { Boats.Clear(); _dock = null; return; } // scene unloaded

        bool active = Active;
        _recheck = (_recheck + 1) % Boats.Count;
        for (int i = 0; i < Boats.Count; i++)
        {
            var b = Boats[i];
            if (b.T == null) continue;
            bool inUse = InUse.Contains(b.Id);
            float x = b.T.position.x;
            if (!inUse && !b.HaveParkX) { b.ParkX = x; b.HaveParkX = true; }
            bool parked = !inUse && Mathf.Abs(x - b.ParkX) < MoveThreshold;

            if (active && parked) Hide(b, i == _recheck);
            else Show(b);
        }
    }

    static void Hide(Boat b, bool recheck)
    {
        if (!b.Hidden)
        {
            // Collected when hiding starts: boats loaded on demand only have content once called in.
            b.Renderers.Clear();
            foreach (var r in b.T.GetComponentsInChildren<Renderer>(true)) b.Renderers.Add(r);
            b.Lights.Clear();
            foreach (var l in b.T.GetComponentsInChildren<Light>(true)) b.Lights.Add(l);
            b.Hidden = true;
            recheck = true;
            if (Plugin.DebugLog)
                Plugin.L.LogInfo($"NPC boat {b.T.name} parked at x={b.ParkX:F1}: hidden ({b.Renderers.Count} renderers, {b.Lights.Count} lights)");
        }
        else if (!recheck) return;

        // Rechecked now and then in case something (an animation, the game's night lights) turns them back on.
        foreach (var r in b.Renderers)
        {
            if (r == null || !r.enabled) continue;
            r.enabled = false;
            if (!b.HiddenRenderers.Contains(r)) b.HiddenRenderers.Add(r);
            else Reenabled++;
        }
        foreach (var l in b.Lights)
        {
            if (l == null || !l.enabled) continue;
            l.enabled = false;
            if (!b.HiddenLights.Contains(l)) b.HiddenLights.Add(l);
            else Reenabled++;
        }
    }

    static void Show(Boat b)
    {
        if (!b.Hidden) return;
        b.Hidden = false;
        foreach (var r in b.HiddenRenderers) if (r != null) r.enabled = true;
        foreach (var l in b.HiddenLights) if (l != null) l.enabled = true;
        b.HiddenRenderers.Clear();
        b.HiddenLights.Clear();
        if (Plugin.DebugLog) Plugin.L.LogInfo($"NPC boat {b.T.name} shown");
    }

#if DIAG
    internal static string Describe()
    {
        if (_dock == null) return "no dock";
        var sb = new System.Text.StringBuilder($"  re-enabled while hidden: {Reenabled}\n");
        var cur = _dock.m_CurrentBoatInfo;
        foreach (var info in _dock.NPCBoats)
        {
            var t = Target(info);
            var b = Boats.Find(x => x.T == t);
            sb.AppendLine($"  {(int)info.character} {info.character} asset={info.useAssetReference} target={(t != null ? t.name : "-")} " +
                (t != null ? $"pos={t.position} inUse={InUse.Contains(t.gameObject.GetInstanceID())} " : "") +
                (b != null ? $"hidden={b.Hidden} park={b.ParkX:F1}" : "") + (cur != null && cur.Pointer == info.Pointer ? " CURRENT" : ""));
        }
        return sb.ToString();
    }

    internal static LobbyNPCBoatDockCtrl Dock => _dock;
#endif
}

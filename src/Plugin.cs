using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;

namespace DaveTheDiverUltrawide;

[BepInPlugin(Guid, "Dave the Diver Ultrawide Fix", Version)]
public class Plugin : BasePlugin
{
    public const string Guid = "dtd.ultrawide";
    public const string Version = "1.0.0";

    internal static ManualLogSource L;
    internal static ConfigEntry<bool> Enabled;
    internal static ConfigEntry<bool> WidenOverlays;
    internal static ConfigEntry<bool> ClipUI;
    internal static ConfigEntry<bool> FixWorldUi;
    internal static ConfigEntry<bool> ScaleCutscenes;
    internal static ConfigEntry<string> CoverImages;
    internal static bool DebugLog;

    public override void Load()
    {
        L = Log;
        Enabled = Config.Bind("General", "Enabled", true,
            "Render the game at the full width of ultrawide (wider than 16:9) resolutions instead of pillarboxing.");
        WidenOverlays = Config.Bind("General", "WidenOverlays", true,
            "Stretch full-screen fades and popup dimming backgrounds to the full screen width. HUD and menus stay in a centered 16:9 area.");
        ClipUI = Config.Bind("General", "ClipUIToCenter", true,
            "Hide UI that the game parks just outside the 16:9 area (e.g. slide-in tabs), exactly like on a 16:9 screen.");
        FixWorldUi = Config.Bind("General", "FixWorldUI", true,
            "Keep UI that follows things in the world (sushi bar order bubbles, speech bubbles, damage numbers, prompts) " +
            "lined up with its target. Without this it drifts toward the center of the screen.");
        ScaleCutscenes = Config.Bind("General", "ScaleCutscenes", true,
            "Show the small pixel-art scenes (e.g. after a Time Skip) as large as the screen height allows " +
            "instead of at their original 2/3 size.");
        CoverImages = Config.Bind("General", "CoverImages", "Title,DummySushiBar",
            "Comma-separated names of full-screen background images that are scaled up to fill the screen width " +
            "(cropping a little at the top and bottom) instead of showing bars at the sides.");
        DebugLog = Config.Bind("Debug", "VerboseLogging", false, "Log every change the fix makes.").Value;
#if DIAG
        DebugLog = true;
#endif
        var harmony = new Harmony(Guid);
        OverlayExtender.Init(harmony);
        WorldUiFix.Init(harmony);
        NpcBoats.Init(harmony);
#if DIAG
        harmony.PatchAll(typeof(WuiTrack));
#endif
        AddComponent<UltrawideBehaviour>();
#if DIAG
        AddComponent<DiagBehaviour>();
#endif
        L.LogInfo($"Ultrawide fix {Version} loaded (enabled={Enabled.Value})");
    }
}

/// <summary>
/// The game's CameraResolution computes a 16:9 viewport rect (m_CameraViewRect) for any screen
/// wider than 16:9, applies it to the main camera and its camera stack, and shows black
/// side masks. This watcher replaces that pillarbox rect with the full screen.
/// </summary>
public class UltrawideBehaviour : MonoBehaviour
{
    const float TargetAspect = 16f / 9f;
    static readonly Rect Full = new(0f, 0f, 1f, 1f);

    // Must be a native array: a managed Camera[] would be copied for the call and never filled.
    Il2CppReferenceArray<Camera> _cams = new(16);
    Rect _pillarRect;
    bool _havePillarRect;

    void LateUpdate()
    {
        try
        {
#if DIAG
            long t = System.Diagnostics.Stopwatch.GetTimestamp();
            if (Plugin.Enabled.Value) Apply();
            Perf.Mark(0, ref t);
            OverlayExtender.Update();
            Perf.Mark(1, ref t);
            WorldUiFix.Update();
            Perf.Mark(2, ref t);
            PixelCutscenes.Update();
            Perf.Mark(3, ref t);
            SushiBarScenery.Update();
            Perf.Mark(4, ref t);
            NpcBoats.Update();
            Perf.Mark(5, ref t);
            Perf.EndFrame();
#else
            if (Plugin.Enabled.Value) Apply();
            OverlayExtender.Update(); // also turns its masks off when disabled / not ultrawide
            WorldUiFix.Update();
            PixelCutscenes.Update();
            SushiBarScenery.Update();
            NpcBoats.Update();
#endif
        }
        catch (Exception e) { Plugin.L.LogError(e); enabled = false; }
    }

    internal static bool IsWide() => Screen.height > 0 && (float)Screen.width / Screen.height > TargetAspect + 0.01f;

    void Apply()
    {
        var cr = Singleton<CameraResolution>._instance;
        if (cr == null || !IsWide()) return;

        var view = cr.m_CameraViewRect;
        if (view != Full && view.width > 0f && view.width < 1f)
        {
            // The game just computed a pillarbox; remember it so we can find cameras that copied it.
            _pillarRect = view;
            _havePillarRect = true;
            cr.m_CameraViewRect = Full;
            Plugin.L.LogInfo($"Replaced pillarbox view rect {view} with full screen ({Screen.width}x{Screen.height})");
        }

        HideMask(cr.m_LeftLetterBoxModifier);
        HideMask(cr.m_RightLetterBoxModifier);

        var main = cr.MainCamera;
        if (main != null && IsPillar(main.rect)) main.rect = Full;

        if (!_havePillarRect) return;
        int n = Camera.allCamerasCount;
        if (_cams.Length < n) _cams = new Il2CppReferenceArray<Camera>(n * 2);
        n = Camera.GetAllCameras(_cams);
        for (int i = 0; i < n; i++)
        {
            var c = _cams[i];
            if (c != null && IsPillar(c.rect)) c.rect = Full;
        }
    }

    bool IsPillar(Rect r)
    {
        if (r.width >= 1f || r.width <= 0f || r.x <= 0f) return false;
        if (_havePillarRect && Approx(r, _pillarRect)) return true;
        // A horizontally centered, full-height 16:9 strip on this screen.
        float expectW = TargetAspect / ((float)Screen.width / Screen.height);
        return Mathf.Abs(r.width - expectW) < 0.01f && Mathf.Abs(r.x - (1f - expectW) * 0.5f) < 0.01f && r.height > 0.99f;
    }

    static bool Approx(Rect a, Rect b) =>
        Mathf.Abs(a.x - b.x) < 0.001f && Mathf.Abs(a.y - b.y) < 0.001f &&
        Mathf.Abs(a.width - b.width) < 0.001f && Mathf.Abs(a.height - b.height) < 0.001f;

    static void HideMask(LetterBoxModifier m)
    {
        if (m != null && m.gameObject.activeSelf) m.gameObject.SetActive(false);
    }
}

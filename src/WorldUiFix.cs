using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Il2CppInterop.Runtime;
using UnityEngine;
using UnityEngine.UI;

namespace DaveTheDiverUltrawide;

/// <summary>
/// UI that follows objects in the world (sushi bar order bubbles, speech bubbles, damage numbers,
/// interaction prompts, QTE markers...) is positioned by the game as
/// <c>camera.WorldToViewportPoint(target)</c> written into the anchors of an element inside the
/// 16:9 UI panel. Without the fix the camera viewport is that same 16:9 area, so it lines up. With the
/// camera widened to the full screen, viewport 0..1 spans the whole screen while the panel only spans
/// the middle, and everything drifts toward the center.
///
/// Fix: remap the x of WorldToViewportPoint for the widened game cameras so 0..1 again means the
/// 16:9 area (values outside 0..1 are the extra side areas). ViewportToWorldPoint is left alone: its
/// callers size full-screen effects and find the screen edges, which should use the full width.
///
/// World UI can now sit outside the 16:9 panel, so it's exempted from the panel clipping
/// (see <see cref="OverlayExtender"/>): graphics under known world-following components are made
/// non-maskable.
/// </summary>
public static class WorldUiFix
{
    const float PanelAspect = 16f / 9f;

    // Components that position UI at world objects (found by searching the game code for
    // WorldToViewportPoint / WorldToScreenPoint callers). Missing names are skipped.
    static readonly string[] WorldUiTypeNames =
    {
        "CustomerActionInfo", "StaffActionInfo", "TalkBoxInfo", "TalkBoxPanel",
        "DamageTextPoolPanel", "FloatingUI", "FollowTargetUI", "UIFollowWorldObject",
        "InputActionIndicatorPanel", "IndicatorController", "MiniPopUpPanel", "EmojiCollection",
        "WorldToViewportUI", "UpdatedWorldToViewportUI", "SubHelperUtilPanel", "CommandFightUI",
        "FishingSlideElementUI", "StickShakeFightUI", "IncreaseQTESlideUI", "MeleeTimingQTEUI",
        "LeatherBackShootTimingUI", "GuideHelperPanel", "FarmIndicatorCanvas",
        "Dredge.DredgeInteractionIcon", "MiniGame.Chase.ChaseUI_Indicator",
        "MiniGame.SeahorseRace.SeahorseRaceHUD_TagIcon",
        "JDLC.JungleSushiBarCookingProgressPanel", "JDLC.JungleSushiBarGrillOperationPanel",
        "JDLC.JungleSushiBarGrillProgressSlot", "JDLC.JungleSushiBarMonkeyActionInfo",
        "JDLC.JungleSushiBarMonkeyActionInfoPanel", "JDLC.JungleStaffActionInfo",
        "JDLC.DynamicTalkMenu", "JDLC.JungleGrabIndicatorPanel", "JDLC.JungleGrabRotateIndicatorPanel",
        "JDLC.JungleInteractionIndicatorPanel", "JDLC.JungleMissionUIMark", "JDLC.JungleRPGGetInfoPanel",
        "JDLC.JungleSpeechCanvasBubble", "JDLC.EscapeBellIndicator", "JDLC.MeleePerfectTimingQTEUI",
        "JDLC.MaroneRescue.MaroneEmojiCollection", "JDLC.WeedCutting.NotiUIHandller",
        "JDLC.WeedCutting.WeedCuttingIncreaseSlideUIQTE", "JDLC.WeedCutting.WeedCuttingMeleeTimingQTEUI",
        "JDLC.WeedCutting.WeedCuttingStickShakeQTE", "JDLC.RPG.RPGUIFollowWorldObject",
        "JDLC.BreedingFarm.FollowingArrow", "JDLC.Housing.HousingOverlayUI",
    };

    static readonly List<Il2CppSystem.Type> WorldUiTypes = new();
    static readonly Dictionary<int, int> ProcessedCounts = new(); // component instance id -> graphics made unmaskable
    static int _nextType;
#if DIAG
    internal static int DiagCalls;
#endif
    static int _cycle;

    public static void Init(Harmony harmony)
    {
        harmony.PatchAll(typeof(Hooks));

        // Interop assemblies of the game (BepInEx/interop); the types live in several of them.
        var assemblies = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic && a.Location.Replace('\\', '/').Contains("/BepInEx/interop/"))
            .ToArray();
        foreach (var name in WorldUiTypeNames)
        {
            var t = assemblies.Select(a => a.GetType(name)).FirstOrDefault(x => x != null);
            if (t == null) { Plugin.L.LogWarning($"World UI type not found: {name}"); continue; }
            WorldUiTypes.Add(Il2CppType.From(t));
        }
    }

    [HarmonyPatch]
    static class Hooks
    {
        // Both overloads are separate native functions (neither calls the other); the game's UI code
        // uses the one-argument version.
        [HarmonyPostfix]
        [HarmonyPatch(typeof(Camera), nameof(Camera.WorldToViewportPoint), typeof(Vector3))]
        static void WorldToViewportPoint(Camera __instance, ref Vector3 __result) => Remap(__instance, ref __result);

        [HarmonyPostfix]
        [HarmonyPatch(typeof(Camera), nameof(Camera.WorldToViewportPoint), typeof(Vector3), typeof(Camera.MonoOrStereoscopicEye))]
        static void WorldToViewportPointEye(Camera __instance, ref Vector3 __result) => Remap(__instance, ref __result);

        static void Remap(Camera cam, ref Vector3 v)
        {
#if DIAG
            DiagCalls++;
#endif
            if (!Plugin.Enabled.Value || !Plugin.FixWorldUi.Value) return;
            float k = WidenFactor(cam);
            if (k > 1f) v.x = 0.5f + (v.x - 0.5f) * k;
        }
    }

    /// <summary>How much wider the camera's view is than the 16:9 UI panel; 1 when not widened.</summary>
    static float WidenFactor(Camera cam)
    {
        if (cam == null || cam.targetTexture != null) return 1f;
        var r = cam.rect;
        if (r.width < 0.999f || r.x > 0.001f) return 1f; // still pillarboxed or a partial viewport
        int h = cam.pixelHeight;
        if (h <= 0) return 1f;
        float k = (float)cam.pixelWidth / h / PanelAspect;
        return k > 1.001f ? k : 1f;
    }

    /// <summary>
    /// Called every LateUpdate. Scans one world-UI type per frame and makes its graphics
    /// non-maskable so the panel clipping doesn't cut them off at the 16:9 border.
    /// </summary>
    public static void Update()
    {
        if (WorldUiTypes.Count == 0 || !Plugin.Enabled.Value || !Plugin.FixWorldUi.Value || !Plugin.ClipUI.Value) return;
        if (!UltrawideBehaviour.IsWide()) return;

        if (_nextType >= WorldUiTypes.Count) { _nextType = 0; _cycle++; }
        var type = WorldUiTypes[_nextType++];
        foreach (var obj in UnityEngine.Object.FindObjectsOfType(type, true))
        {
            var comp = obj.TryCast<Component>();
            if (comp == null) continue;
            int id = comp.GetInstanceID();
            bool known = ProcessedCounts.TryGetValue(id, out int count);
            // New components right away; known ones are rechecked now and then for new children.
            if (known && (_cycle + id) % 8 != 0) continue;
            var graphics = comp.GetComponentsInChildren<MaskableGraphic>(true);
            if (known && graphics.Length == count) continue;
            foreach (var g in graphics)
            {
                if (!g.maskable) continue;
                g.maskable = false;
                g.RecalculateClipping();
            }
            ProcessedCounts[id] = graphics.Length;
        }

        if (ProcessedCounts.Count > 2000) ProcessedCounts.Clear(); // destroyed objects; rebuilt on the next scans
    }
}

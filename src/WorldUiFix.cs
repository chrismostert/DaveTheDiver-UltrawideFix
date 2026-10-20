using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes;
using UnityEngine;
using UnityEngine.UI;

namespace DaveTheDiverUltrawide;

/// <summary>
/// UI that follows objects in the world (sushi bar order bubbles, speech bubbles, damage numbers,
/// interaction prompts, QTE markers...) is mostly positioned by the game as
/// <c>camera.WorldToViewportPoint(target)</c> written into the anchors of an element inside the
/// 16:9 UI panel. Without the fix the camera viewport is that same 16:9 area, so it lines up. With the
/// camera widened to the full screen, viewport 0..1 spans the whole screen while the panel only spans
/// the middle, and everything drifts toward the center.
///
/// Fix: while one of those positioning methods runs (Harmony prefix/postfix scope), remap the x of
/// WorldToViewportPoint for the widened game cameras so 0..1 again means the 16:9 area (values
/// outside 0..1 are the extra side areas). Other callers get the real viewport point: some UI maps it
/// onto the full-width root canvas itself (FollowTargetUI, FloatingUI: e.g. NPC emojis on the boat),
/// and image effects (sun shafts, depth of field, vignettes), see-through masks and visibility checks
/// need real screen coordinates. The method list comes from all callers of WorldToViewportPoint in
/// the game code, sorted by what they do with the result.
///
/// World UI can now sit outside the 16:9 panel, so it's exempted from the panel clipping
/// (see <see cref="OverlayExtender"/>): graphics under the hooked components (also the screen-point
/// based ones) are made non-maskable when the component first positions itself.
/// </summary>
public static class WorldUiFix
{
    const float PanelAspect = 16f / 9f;
    const float RecheckInterval = 2f; // seconds; components may add graphics later (pools)

    // (type, method, remap): remap = sets anchors / a 1920-wide position from WorldToViewportPoint
    // inside the 16:9 panel; false = positions on the full canvas or by screen point, only needs the
    // clipping exemption. Missing names are skipped.
    static readonly (string Type, string Method, bool Remap)[] Targets =
    {
        ("CustomerActionInfo", "UpdateAnchor", true),
        ("StaffActionInfo", "UpdateAnchor", true),
        ("TalkBoxInfo", "Show", true),
        ("TalkBoxInfo", "UpdateAnchor", true),
        ("TalkBoxPanel", "ShowMessage", true),
        ("DamageTextPoolPanel", "LateUpdate", true),
        ("DamageTextPoolPanel", "ShowDamageText", true),
        ("EmojiCollection", "UpdateEmojiPosition", true),
        ("IndicatorController", "LateUpdate", true),
        ("InputActionIndicatorPanel", "LateUpdate", true),
        ("MiniPopUpPanel", "LateUpdate", true),
        ("SubHelperUtilPanel", "LateUpdate", true),
        ("SubHelperUtilPanel", "ShowSubHelperEffect", true),
        ("GuideHelperPanel", "ShowGuidePointerUI", true),
        ("TargetHUDIndicatorMarker", "RefreshUI", true), // via CameraManager.GetViewportPoint
        ("UpdatedWorldToViewportUI", "Convert", true),
        ("WorldToViewportUI", "Convert", true),
        ("CommandFightUI", "LateUpdate", true),
        ("StickShakeFightUI", "LateUpdate", true),
        ("FishingSlideElementUI", "LateUpdate", true),
        ("IncreaseQTESlideUI", "LateUpdate", true),
        ("MeleeTimingQTEUI", "UIPositioningAction", true),
        ("LeatherBackShootTimingUI", "UIPositioningAction", true),
        ("Dredge.DredgeInteractionIcon", "LateUpdate", true),
        ("MiniGame.Chase.ChaseUI_Indicator", "Update", true),
        ("MiniGame.Feeding.FeedingHand", "RefreshInputIconPos", true), // (v - 0.5) * 1920
        ("MiniGame.Shooting.ShootingUI_Carving", "LateUpdate", true),
        ("MiniGame.CoOpGame.CoOpUI_CharacterFocus", "LateUpdate", true),
        ("MiniGame.CoOpGame.CoOpUI_Interaction", "LateUpdate", true),
        ("JDLC.EscapeBellIndicator", "LateUpdate", true),
        ("JDLC.MeleePerfectTimingQTEUI", "UpdateSnapPosition", true),
        ("JDLC.JungleSushiBarCookingProgressPanel", "Bind", true),
        ("JDLC.JungleSushiBarCookingProgressPanel", "SetAnchorPos", true),
        ("JDLC.JungleSushiBarGrillOperationPanel", "Init", true),
        ("JDLC.JungleSushiBarGrillProgressSlot", "BindTransform", true),
        ("JDLC.JungleSushiBarGrillProgressSlot", "SetData", true),
        ("JDLC.JungleSushiBarMonkeyActionInfoPanel", "UpdateAnchor", true), // explicit interface method
        ("JDLC.JungleSushiBarMonkeyActionInfo", "UpdateAnchor", true),
        ("JDLC.JungleStaffActionInfo", "UpdateAnchor", true),
        ("JDLC.DynamicTalkMenu", "ShowMenus", true),
        ("JDLC.DynamicTalkMenu", "UpdateBindPosition", true),
        ("JDLC.JungleGrabIndicatorPanel", "LateUpdate", true),
        ("JDLC.JungleGrabRotateIndicatorPanel", "LateUpdate", true),
        ("JDLC.JungleInteractionIndicatorPanel", "LateUpdate", true),
        ("JDLC.JungleSushiBarInteriorPlacementPanel", "SetIndicator", true),
        ("JDLC.JungleSushiBarInteriorPanel", "SetIndicator", true),
        ("JDLC.JungleMissionUIMark", "LateUpdate", true),
        ("JDLC.JungleRPGGetInfoPanel", "LateUpdate", true),
        ("JDLC.JungleSpeechCanvasBubble", "UpdateLocalPosition", true),
        ("JDLC.WeedCutting.WeedCuttingIncreaseSlideUIQTE", "LateUpdate", true),
        ("JDLC.WeedCutting.WeedCuttingMeleeTimingQTEUI", "UIPositioningAction", true),
        ("JDLC.WeedCutting.WeedCuttingStickShakeQTE", "LateUpdate", true),
        ("JDLC.MaroneRescue.MaroneEmojiCollection", "LateUpdate", true),
        ("JDLC.Basilo.Basilo_InteractionUI", "FocusKey", true),
        ("JDLC.GrowAGarden.GardenUIProvider", "FocusPlayer", true),

        ("FollowTargetUI", "LateUpdate", false), // viewport * root canvas size
        ("FloatingUI", "LateUpdate", false),     // viewport * root canvas size
        ("UIFollowWorldObject", "Update", false),
        ("JDLC.RPG.RPGUIFollowWorldObject", "LateUpdate", false),
        ("JDLC.BreedingFarm.FollowingArrow", "Update", false),
        ("JDLC.Housing.HousingOverlayUI", "GetWorldToUIPosition", false),
        ("JDLC.WeedCutting.NotiUIHandller", "LateUpdate", false),
        ("FarmIndicatorCanvas", "AddIndicator", false),
    };

    static int _remapScope; // > 0 while a remap target method runs
    // component -> when its graphics were last made non-maskable, and its child count then
    static readonly Dictionary<IntPtr, (float Next, int Children)> Checked = new();
#if DIAG
    internal static int DiagCalls, DiagRemapped;
    // For the alignment check (Diag "wui"): the hooked component running now and its last projection.
    internal static int DiagScope;
    internal static string DiagScopeType;
    internal static Vector3 DiagLastWorld;
    internal static Camera DiagLastCam;
#endif

    public static void Init(Harmony harmony)
    {
        harmony.PatchAll(typeof(Hooks));

        // Interop assemblies of the game (BepInEx/interop); the types live in several of them.
        var assemblies = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic && a.Location.Replace('\\', '/').Contains("/BepInEx/interop/"))
            .ToArray();
        var remapPrefix = new HarmonyMethod(typeof(WorldUiFix).GetMethod(nameof(RemapPrefix), BindingFlags.NonPublic | BindingFlags.Static));
        var remapPostfix = new HarmonyMethod(typeof(WorldUiFix).GetMethod(nameof(RemapPostfix), BindingFlags.NonPublic | BindingFlags.Static));
#if DIAG
        var clipPrefix = new HarmonyMethod(typeof(WorldUiFix).GetMethod(nameof(ClipPrefix), BindingFlags.NonPublic | BindingFlags.Static));
#else
        HarmonyMethod clipPrefix = null;
#endif
        var clipPostfix = new HarmonyMethod(typeof(WorldUiFix).GetMethod(nameof(ClipPostfix), BindingFlags.NonPublic | BindingFlags.Static));
        int patched = 0;
        foreach (var (typeName, methodName, remap) in Targets)
        {
            var t = assemblies.Select(a => a.GetType(typeName)).FirstOrDefault(x => x != null);
            var declared = t != null ? AccessTools.GetDeclaredMethods(t).Where(m => !m.IsStatic).ToList() : new List<MethodInfo>();
            var methods = declared.Where(m => m.Name == methodName).ToList();
            if (methods.Count == 0) // explicit interface implementations get a prefixed name in the interop assembly
                methods = declared.Where(m => m.Name.EndsWith("_" + methodName)).ToList();
            if (methods.Count == 0) { Plugin.L.LogWarning($"World UI method not found: {typeName}.{methodName}"); continue; }
            foreach (var m in methods)
            {
                try
                {
                    if (remap) harmony.Patch(m, prefix: remapPrefix, postfix: remapPostfix);
                    else harmony.Patch(m, prefix: clipPrefix, postfix: clipPostfix);
                    patched++;
                }
                catch (Exception e) { Plugin.L.LogWarning($"Could not hook {typeName}.{m.Name}: {e.Message}"); }
            }
        }
        if (Plugin.DebugLog) Plugin.L.LogInfo($"World UI: hooked {patched} methods");
    }

    static void RemapPrefix(Il2CppObjectBase __instance)
    {
        _remapScope++;
#if DIAG
        DiagEnter(__instance);
#endif
    }

    // The clipping exemption runs after the method: it may create or activate pooled elements.
    static void RemapPostfix(Il2CppObjectBase __instance)
    {
        if (_remapScope > 0) _remapScope--;
#if DIAG
        if (DiagScope > 0) DiagScope--;
#endif
        ExemptFromClipping(__instance);
    }

    static void ClipPostfix(Il2CppObjectBase __instance)
    {
#if DIAG
        if (DiagScope > 0) DiagScope--;
#endif
        ExemptFromClipping(__instance);
    }

#if DIAG
    static void ClipPrefix(Il2CppObjectBase __instance) => DiagEnter(__instance);

    static void DiagEnter(Il2CppObjectBase instance)
    {
        DiagScope++;
        DiagLastCam = null;
        try { DiagScopeType = instance?.Cast<Il2CppSystem.Object>().GetIl2CppType().FullName; } catch { DiagScopeType = "?"; }
    }
#endif

    [HarmonyPatch]
    static class Hooks
    {
        // Both overloads are separate native functions (neither calls the other); the game's UI code
        // uses the one-argument version.
        [HarmonyPostfix]
        [HarmonyPatch(typeof(Camera), nameof(Camera.WorldToViewportPoint), typeof(Vector3))]
        static void WorldToViewportPoint(Camera __instance, Vector3 __0, ref Vector3 __result) => Remap(__instance, __0, ref __result);

        [HarmonyPostfix]
        [HarmonyPatch(typeof(Camera), nameof(Camera.WorldToViewportPoint), typeof(Vector3), typeof(Camera.MonoOrStereoscopicEye))]
        static void WorldToViewportPointEye(Camera __instance, Vector3 __0, ref Vector3 __result) => Remap(__instance, __0, ref __result);

        static void Remap(Camera cam, Vector3 world, ref Vector3 v)
        {
#if DIAG
            DiagCalls++;
            if (DiagScope > 0) { DiagLastWorld = world; DiagLastCam = cam; }
            long t = System.Diagnostics.Stopwatch.GetTimestamp();
#endif
            if (_remapScope <= 0 || !Plugin.Enabled.Value || !Plugin.FixWorldUi.Value) return;
            float k = WidenFactor(cam);
            if (k > 1f) v.x = 0.5f + (v.x - 0.5f) * k;
#if DIAG
            DiagRemapped++;
            Perf.Mark(Perf.Remap, ref t);
#endif
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
    /// Makes the graphics under a world-UI component non-maskable so the panel clipping doesn't cut
    /// them off at the 16:9 border. Runs after the component positioned itself; again right away when
    /// it got new children (pools), otherwise at most every <see cref="RecheckInterval"/>.
    /// </summary>
    static void ExemptFromClipping(Il2CppObjectBase instance)
    {
        try
        {
            if (instance == null || !Plugin.Enabled.Value || !Plugin.FixWorldUi.Value || !Plugin.ClipUI.Value) return;
            if (!UltrawideBehaviour.IsWide()) return;
            var comp = instance.TryCast<Component>();
            if (comp == null) return;
            float now = Time.unscaledTime;
            int children = comp.transform.childCount;
            var key = instance.Pointer;
            if (Checked.TryGetValue(key, out var last) && now < last.Next && children == last.Children) return;
            if (Checked.Count > 2000) Checked.Clear(); // destroyed objects; rebuilt as they come back
            Checked[key] = (now + RecheckInterval, children);

            int n = 0;
            foreach (var g in comp.GetComponentsInChildren<MaskableGraphic>(true))
            {
                if (!g.maskable) continue;
                g.maskable = false;
                g.RecalculateClipping();
                n++;
            }
            if (n > 0 && Plugin.DebugLog) Plugin.L.LogInfo($"World UI {comp.name}: {n} graphics exempted from panel clipping");
        }
        catch (Exception e) { Plugin.L.LogError(e); }
    }

    /// <summary>Called every LateUpdate, outside any game method.</summary>
    public static void Update()
    {
        _remapScope = 0; // in case a hooked method threw before its postfix
#if DIAG
        DiagScope = 0;
#endif
    }
}

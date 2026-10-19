using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace DaveTheDiverUltrawide;

/// <summary>
/// Every UI canvas in the game nests its content in a full-screen "Panel" with a 16:9
/// AspectRatioFitter, so HUD and menus stay in a centered 16:9 box (which is what we want on
/// ultrawide). Two things go wrong with that on a wider screen, and this class fixes both:
///
/// 1. Full-screen overlays inside the box (scene fades, popup dimming curtains, cinematic bars)
///    only cover the middle of the screen. For every solid-color image that spans the full width
///    of the box, two "side strip" child images are added that extend it to the screen edges and
///    mirror its color, alpha and visibility every frame.
///
/// 2. UI that the game "parks" just outside the 16:9 area while hidden (slide-in tabs etc.)
///    becomes visible. A RectMask2D on each panel clips game UI to the 16:9 area, exactly what
///    is visible in the unmodded game. The side strips are not maskable, so they are not clipped.
///
/// The game's own layout is never modified. Images are checked when they get enabled
/// (Image.OnEnable hook), so there is no polling.
/// </summary>
public static class OverlayExtender
{
    const float PanelAspect = 16f / 9f;
    const string StripName = "UltrawideStrip";
    const int RecheckFrames = 3;
    const int RecheckFramesLikely = 240; // likely overlays may be mid-animation (scaled/rotated) when enabled

    class PanelInfo
    {
        public RectTransform Panel;
        public RectTransform Root;
        public RectMask2D Mask; // added by us, null if the panel isn't full-screen
        public bool FullScreen;
    }

    class Entry
    {
        public Image Src;
        public GameObject SrcGo;
        public PanelInfo Panel;
        public Image Left, Right;
        public RectTransform LeftRt, RightRt;
        public float Side = -1f, Inset = -1f;
    }

    /// <summary>A soft vignette that is stretched (not extended) to the full screen width.</summary>
    class Stretched
    {
        public Image Img;
        public PanelInfo Panel;
        public float OrigWidth, OrigPosX, Applied = -1f;
    }

    /// <summary>A full-screen background artwork that is scaled up uniformly to cover the screen.</summary>
    class Covered
    {
        public Image Img;
        public PanelInfo Panel;
        public Vector3 OrigScale;
        public float Applied = -1f;
    }

    static readonly Dictionary<int, PanelInfo> Panels = new();
    static readonly Dictionary<int, Covered> Covers = new();
    static HashSet<string> _coverNames;
    static readonly Dictionary<int, Stretched> StretchedImages = new();
    static readonly Dictionary<int, Entry> Entries = new();
    static readonly List<Entry> List = new();
    static readonly List<(Image img, int frame, int window)> Pending = new();
    static readonly List<(Image img, int frame, int window)> PendingSwap = new();
    static int _lastW, _lastH;
    static int _lastSceneCount, _lastSceneHandle, _rescanAtFrame = -1;
    static bool _wasActive;
    static bool _inHook;

    static bool Active => Plugin.Enabled.Value && UltrawideBehaviour.IsWide();
    static bool Widen => Plugin.WidenOverlays.Value;
    static bool Clip => Plugin.ClipUI.Value;

    public static void Init(Harmony harmony)
    {
        harmony.PatchAll(typeof(Hooks));
    }

    [HarmonyPatch]
    static class Hooks
    {
        [HarmonyPostfix]
        [HarmonyPatch(typeof(Image), nameof(Image.OnEnable))]
        static void ImageOnEnable(Image __instance)
        {
            if (_inHook || !_wasActive) return; // our own strips being enabled / not ultrawide
            try
            {
                _inHook = true;
                Consider(__instance, true, true);
            }
            catch (Exception e) { Plugin.L.LogError(e); }
            finally { _inHook = false; }
        }
    }

    /// <summary>Called every LateUpdate.</summary>
    public static void Update()
    {
        bool active = Active;
        if (active != _wasActive)
        {
            _wasActive = active;
            SetMasksEnabled(active && Clip);
            _lastW = _lastH = 0;
        }
        // Keep geometry in sync even when not ultrawide: everything shrinks back to the
        // original size at 16:9 or narrower.
        UpdateGeometryAll();
        if (!active) return;

        // Resolution changed (or just became ultrawide): check everything once.
        if (Screen.width != _lastW || Screen.height != _lastH)
        {
            _lastW = Screen.width;
            _lastH = Screen.height;
            FullScan();
        }

        // Safety net for UI whose layout settled late: one extra scan shortly after a scene change.
        int sceneCount = SceneManager.sceneCount;
        int sceneHandle = SceneManager.GetActiveScene().handle;
        if (sceneCount != _lastSceneCount || sceneHandle != _lastSceneHandle)
        {
            _lastSceneCount = sceneCount;
            _lastSceneHandle = sceneHandle;
            _rescanAtFrame = Time.frameCount + 30;
        }
        if (_rescanAtFrame > 0 && Time.frameCount >= _rescanAtFrame)
        {
            _rescanAtFrame = -1;
            FullScan();
        }

        if (Pending.Count > 0)
        {
            int frame = Time.frameCount;
            PendingSwap.Clear();
            PendingSwap.AddRange(Pending);
            Pending.Clear();
            foreach (var (img, f, window) in PendingSwap)
            {
                if (img == null || !img.isActiveAndEnabled) continue;
                if (Consider(img, false, false)) continue;
                if (frame - f < window) Pending.Add((img, f, window));
            }
        }
    }

    static readonly List<int> Dead = new();

    static void UpdateGeometryAll()
    {
        // Forget destroyed images now and then.
        if (Time.frameCount % 600 == 0)
        {
            Dead.Clear();
            foreach (var kv in StretchedImages) if (kv.Value.Img == null) Dead.Add(kv.Key);
            foreach (var k in Dead) StretchedImages.Remove(k);
            Dead.Clear();
            foreach (var kv in Covers) if (kv.Value.Img == null) Dead.Add(kv.Key);
            foreach (var k in Dead) Covers.Remove(k);
            Dead.Clear();
            foreach (var kv in Entries) if (kv.Value.Src == null) Dead.Add(kv.Key);
            foreach (var k in Dead) Entries.Remove(k);
        }

        foreach (var st in StretchedImages.Values)
            if (st.Img != null && st.Img.isActiveAndEnabled) UpdateStretch(st);
        foreach (var c in Covers.Values)
            if (c.Img != null && c.Img.isActiveAndEnabled) UpdateCover(c);

        for (int i = List.Count - 1; i >= 0; i--)
        {
            var e = List[i];
            if (e.SrcGo == null || e.Left == null || e.Right == null)
            {
                List.RemoveAt(i);
                continue;
            }
            if (!e.SrcGo.activeInHierarchy) continue;
            UpdateGeometry(e);
            Sync(e);
        }
    }

    static void FullScan()
    {
        var sw = Plugin.DebugLog ? System.Diagnostics.Stopwatch.StartNew() : null;
        foreach (var f in UnityEngine.Object.FindObjectsOfType<AspectRatioFitter>(true)) GetPanel(f);
        var all = UnityEngine.Object.FindObjectsOfType<Image>(true);
        foreach (var img in all) Consider(img, false, true);
        if (sw != null) Plugin.L.LogInfo($"Full scan: {Panels.Count} panels, {all.Length} images, {List.Count} extended, {sw.Elapsed.TotalMilliseconds:F1} ms");
    }

    static PanelInfo GetPanel(AspectRatioFitter fitter)
    {
        if (fitter == null || fitter.aspectMode != AspectRatioFitter.AspectMode.FitInParent ||
            Mathf.Abs(fitter.aspectRatio - PanelAspect) > 0.01f)
            return null;
        int id = fitter.GetInstanceID();
        if (!Panels.TryGetValue(id, out var p) || p.Panel == null)
        {
            var canvas = fitter.GetComponentInParent<Canvas>(true);
            if (canvas == null) return null;
            p = new PanelInfo
            {
                Panel = fitter.GetComponent<RectTransform>(),
                Root = canvas.rootCanvas.GetComponent<RectTransform>(),
            };
            Panels[id] = p;
        }
        // Not cached when false: right after a scene loads the fitter may not have run yet.
        if (!p.FullScreen) CheckFullScreen(p);
        return p;
    }

    /// <summary>
    /// Only full-screen panels (the 16:9 box spans the whole screen height) get side strips
    /// and clipping; a 16:9 box inside some menu is left alone.
    /// </summary>
    static void CheckFullScreen(PanelInfo p)
    {
        float panelH = p.Panel.rect.height * p.Panel.lossyScale.y;
        float rootH = p.Root.rect.height * p.Root.lossyScale.y;
        p.FullScreen = rootH > 0f && Mathf.Abs(panelH - rootH) / rootH < 0.01f;
        if (!p.FullScreen || !Clip) return;
        var go = p.Panel.gameObject;
        if (go.GetComponent<RectMask2D>() != null) return; // the game's own mask, don't touch it
        p.Mask = go.AddComponent<RectMask2D>();
        p.Mask.enabled = _wasActive;
        if (Plugin.DebugLog) Plugin.L.LogInfo($"Clipping UI panel {PathOf(p.Panel)}");
    }

    static void SetMasksEnabled(bool on)
    {
        foreach (var p in Panels.Values)
            if (p.Mask != null) p.Mask.enabled = on;
    }

    /// <returns>true when the image is (now) handled or can never be an overlay.</returns>
    static bool Consider(Image img, bool fromEnable, bool firstLook)
    {
        if (img == null) return true;
        int id = img.GetInstanceID();
        if (Entries.TryGetValue(id, out var existing))
        {
            if (existing.Left != null) return true;
            Entries.Remove(id); // strips were destroyed; rebuild
        }
        if (img.gameObject.name == StripName) return true;

        // Registers the panel (and its clip mask) even if this image isn't an overlay.
        var panel = GetPanel(img.GetComponentInParent<AspectRatioFitter>(true));
        if (panel == null || !Widen) return true;
        if (!panel.FullScreen)
        {
            // The panel may not be laid out yet; look again for a few frames.
            if (fromEnable) Pending.Add((img, Time.frameCount, RecheckFrames));
            return false;
        }

        // Cheap rejections first.
        var sprite = img.sprite;
        if (sprite != null && sprite.name.IndexOf("vignet", StringComparison.OrdinalIgnoreCase) >= 0)
            return ConsiderVignette(img, panel, id, firstLook);
        if (sprite != null && IsCoverName(img.gameObject.name))
            return ConsiderCover(img, panel, id, firstLook);
        if (sprite != null)
        {
            var sr = sprite.rect;
            if (sr.width > 32f || sr.height > 32f) return true; // real artwork
        }
        if (img.type == Image.Type.Filled) return true;

        if (!SpansPanel(img.rectTransform, panel.Panel, out var why))
        {
            // Layout may not be final yet right after instantiation, or an intro animation is
            // scaling/rotating it; look again for a while.
            if (firstLook && LooksLikeOverlay(img.rectTransform, panel.Panel))
            {
                Pending.Add((img, Time.frameCount, RecheckFramesLikely));
                if (Plugin.DebugLog) Plugin.L.LogInfo($"Waiting for {PathOf(img.transform)}: {why}");
            }
            else if (fromEnable) Pending.Add((img, Time.frameCount, RecheckFrames));
            return false;
        }

        var e = Create(img, panel);
        Entries[id] = e;
        List.Add(e);
        if (Plugin.DebugLog) Plugin.L.LogInfo($"Extended overlay {PathOf(img.transform)}");
        return true;
    }

    static bool IsCoverName(string name)
    {
        if (_coverNames == null)
        {
            _coverNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var n in Plugin.CoverImages.Value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                _coverNames.Add(n);
        }
        return _coverNames.Contains(name);
    }

    static bool ConsiderCover(Image img, PanelInfo panel, int id, bool firstLook)
    {
        if (Covers.ContainsKey(id)) return true;
        var rt = img.rectTransform;
        var pr = panel.Panel.rect;
        if (!SpansPanel(rt, panel.Panel, out _) || Mathf.Abs(rt.rect.height - pr.height) > 2f || Mathf.Abs(rt.rect.width - pr.width) > 2f)
        {
            if (firstLook) Pending.Add((img, Time.frameCount, RecheckFramesLikely));
            return false;
        }
        var c = new Covered { Img = img, Panel = panel, OrigScale = rt.localScale };
        Covers[id] = c;
        img.maskable = false;
        img.RecalculateClipping();
        UpdateCover(c);
        if (Plugin.DebugLog) Plugin.L.LogInfo($"Scaled background to cover screen: {PathOf(img.transform)}");
        return true;
    }

    static void UpdateCover(Covered c)
    {
        var p = c.Panel;
        if (p.Panel == null || p.Root == null) return;
        float rootW = p.Root.rect.width / Mathf.Max(p.Panel.localScale.x, 1e-4f);
        float factor = Mathf.Max(1f, rootW / Mathf.Max(p.Panel.rect.width, 1f));
        if (Mathf.Abs(factor - c.Applied) < 0.001f) return;
        c.Applied = factor;
        c.Img.rectTransform.localScale = c.OrigScale * factor;
    }

    static bool ConsiderVignette(Image img, PanelInfo panel, int id, bool firstLook)
    {
        if (StretchedImages.ContainsKey(id)) return true;
        var rt = img.rectTransform;
        // Stretching would also stretch child UI, so only do it for a vignette without any.
        if (img.GetComponentsInChildren<Graphic>(true).Length > 1) return true;
        if (!SpansPanel(rt, panel.Panel, out _))
        {
            if (firstLook) Pending.Add((img, Time.frameCount, RecheckFramesLikely));
            return false;
        }
        var st = new Stretched { Img = img, Panel = panel, OrigWidth = rt.sizeDelta.x, OrigPosX = rt.anchoredPosition.x };
        StretchedImages[id] = st;
        img.maskable = false;
        img.RecalculateClipping();
        UpdateStretch(st);
        if (Plugin.DebugLog) Plugin.L.LogInfo($"Stretched vignette {PathOf(img.transform)}");
        return true;
    }

    static void UpdateStretch(Stretched st)
    {
        var p = st.Panel;
        if (p.Panel == null || p.Root == null) return;
        float extra = Mathf.Max(0f, p.Root.rect.width / Mathf.Max(p.Panel.localScale.x, 1e-4f) - p.Panel.rect.width);
        if (Mathf.Abs(extra - st.Applied) < 0.25f) return;
        st.Applied = extra;
        var rt = st.Img.rectTransform;
        rt.sizeDelta = new Vector2(st.OrigWidth + extra, rt.sizeDelta.y);
        rt.anchoredPosition = new Vector2(st.OrigPosX + extra * (rt.pivot.x - 0.5f), rt.anchoredPosition.y);
    }

    /// <summary>Local size suggests a full-width overlay, even if a transform animation is running.</summary>
    static bool LooksLikeOverlay(RectTransform rt, RectTransform panel)
    {
        var r = rt.rect;
        return r.width >= panel.rect.width - 2f && r.height >= 1f;
    }

    /// <summary>
    /// True when the image spans (at least) the full width of the 16:9 panel, is centered on it,
    /// and is not rotated/scaled. Height can be anything, so cinematic bars and banners qualify too.
    /// </summary>
    static bool SpansPanel(RectTransform rt, RectTransform panel, out string why)
    {
        why = null;
        var pr = panel.rect;
        var r = rt.rect;
        // Quick size check before walking the hierarchy.
        if (r.width < pr.width - 2f || r.height < 1f)
        {
            // Only worth explaining for near-full-width rects.
            if (r.width > pr.width * 0.9f) why = $"width {r.width:F0} < panel {pr.width:F0}";
            return false;
        }
        for (var cur = rt.transform; cur != null && cur != panel.transform; cur = cur.parent)
        {
            var s = cur.localScale;
            if (Mathf.Abs(s.x - 1f) > 0.001f || Mathf.Abs(s.y - 1f) > 0.001f) { why = $"scale {s} on {cur.name}"; return false; }
            // Abs: an animation that ends at 360 degrees leaves q = (0,0,0,-1), which is not rotated.
            if (Mathf.Abs(Quaternion.Dot(cur.localRotation, Quaternion.identity)) < 0.99998f) { why = $"rotation {cur.localEulerAngles} on {cur.name}"; return false; }
        }
        var center = panel.InverseTransformPoint(rt.TransformPoint(r.center));
        if (Mathf.Abs(center.x - pr.center.x) > 2f) { why = $"off-center x={center.x - pr.center.x:F0}"; return false; }
        return true;
    }

    static Entry Create(Image src, PanelInfo panel)
    {
        var e = new Entry { Src = src, SrcGo = src.gameObject, Panel = panel };
        bool prev = _inHook;
        _inHook = true;
        try
        {
            (e.Right, e.RightRt) = MakeStrip(src, 1f);
            (e.Left, e.LeftRt) = MakeStrip(src, 0f);
        }
        finally { _inHook = prev; }
        UpdateGeometry(e);
        Sync(e);
        return e;
    }

    static (Image, RectTransform) MakeStrip(Image src, float side)
    {
        var go = new GameObject(StripName);
        go.layer = src.gameObject.layer;
        var rt = go.AddComponent<RectTransform>();
        rt.SetParent(src.rectTransform, false);
        // First children, so they render directly after the source image and below its other
        // children, just like the source itself.
        rt.SetAsFirstSibling();
        // Anchor to the left or right edge of the source and grow outward.
        rt.anchorMin = new Vector2(side, 0f);
        rt.anchorMax = new Vector2(side, 1f);
        rt.pivot = new Vector2(1f - side, 0.5f);
        go.AddComponent<LayoutElement>().ignoreLayout = true;
        var img = go.AddComponent<Image>();
        img.sprite = src.sprite;
        img.type = Image.Type.Simple;
        // Must not be clipped by the panel's RectMask2D. The maskable setter doesn't unregister an
        // already-enabled graphic from its clipper, so force that explicitly.
        img.maskable = false;
        img.RecalculateClipping();
        return (img, rt);
    }

    /// <summary>Each strip covers the area between the 16:9 panel edge and the screen edge.</summary>
    static void UpdateGeometry(Entry e)
    {
        var p = e.Panel;
        if (p.Panel == null || p.Root == null) return;
        float panelW = p.Panel.rect.width;
        float side = (p.Root.rect.width / Mathf.Max(p.Panel.localScale.x, 1e-4f) - panelW) * 0.5f;
        if (side < 0f) side = 0f;
        // An image wider than the panel is clipped at the panel edge, so start the strip there.
        float inset = Mathf.Max(0f, (e.Src.rectTransform.rect.width - panelW) * 0.5f);
        if (Mathf.Abs(side - e.Side) < 0.25f && Mathf.Abs(inset - e.Inset) < 0.25f) return;
        e.Side = side;
        e.Inset = inset;
        e.LeftRt.sizeDelta = new Vector2(side, 0f);
        e.RightRt.sizeDelta = new Vector2(side, 0f);
        e.LeftRt.anchoredPosition = new Vector2(inset, 0f);
        e.RightRt.anchoredPosition = new Vector2(-inset, 0f);
    }

    static void Sync(Entry e)
    {
        var src = e.Src;
        var c = src.color;
        bool en = src.enabled;
        float a = src.canvasRenderer.GetAlpha();
        bool ray = src.raycastTarget;
        var m = src.material;
        SyncOne(e.Left, c, en, a, ray, m);
        SyncOne(e.Right, c, en, a, ray, m);
    }

    static void SyncOne(Image s, Color c, bool en, float a, bool ray, Material m)
    {
        if (!s.gameObject.activeSelf) s.gameObject.SetActive(true);
        if (s.color != c) s.color = c;
        if (s.enabled != en) s.enabled = en;
        if (s.raycastTarget != ray) s.raycastTarget = ray;
        if (s.canvasRenderer.GetAlpha() != a) s.canvasRenderer.SetAlpha(a);
        if (s.material != m) s.material = m;
    }

    static string PathOf(Transform t)
    {
        var s = t.name;
        while (t.parent != null) { t = t.parent; s = t.name + "/" + s; }
        return s;
    }
}

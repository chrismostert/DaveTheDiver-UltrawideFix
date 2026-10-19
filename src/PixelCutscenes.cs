using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace DaveTheDiverUltrawide;

/// <summary>
/// The small pixel-art scenes (e.g. the Time Skip scenes TimeSkip_Loading_LobbyBoat_Day / _SushiBar_Night)
/// are a 320x180 masked image shown at 4x inside a 1920x1080 container, so they only fill 2/3 of the
/// 16:9 area and get a wide black frame on ultrawide screens. This scales the container up so the
/// frame fills the screen height (at 1440p exactly 8x, so the pixels stay crisp).
///
/// A frame is an Image with a Mask, about 16:9, at most 360 units tall in its own coordinates (low-res
/// pixel art). The scaled object is its nearest ancestor that covers the whole 16:9 panel, has scale 1,
/// and has no Graphic or Animator of its own, so the scene's own animations keep working.
/// </summary>
public static class PixelCutscenes
{
    const float PanelAspect = 16f / 9f;

    class Scaled
    {
        public Transform Target;
        public Vector3 OrigScale;
        public float Factor;
        public GameObject FrameGo;
    }

    static readonly Dictionary<int, Scaled> ByTarget = new();
    static readonly List<int> Dead = new();

    static bool Active => Plugin.Enabled.Value && Plugin.ScaleCutscenes.Value && UltrawideBehaviour.IsWide();

    public static bool IsFrame(Image img)
    {
        var r = img.rectTransform.rect;
        if (r.height < 1f || r.height > 360f) return false;
        if (Mathf.Abs(r.width / r.height - PanelAspect) > 0.05f) return false;
        return img.GetComponent<Mask>() != null;
    }

    /// <summary>Returns false while the frame isn't ready (e.g. an intro animation is scaling it).</summary>
    public static bool Consider(Image frame, RectTransform panel)
    {
        Transform target = null;
        for (var t = frame.transform.parent; t != null && t != panel; t = t.parent)
        {
            var rt = t.TryCast<RectTransform>();
            if (rt == null) return true;
            if (t.GetComponent<Graphic>() != null) break; // reached an overlay/background; don't scale those
            if (t.GetComponent<Animator>() != null) continue;
            var s = t.localScale;
            bool full = Mathf.Abs(rt.rect.width - panel.rect.width) < 2f && Mathf.Abs(rt.rect.height - panel.rect.height) < 2f;
            if (full && Mathf.Abs(s.x - 1f) < 0.001f && Mathf.Abs(s.y - 1f) < 0.001f)
            {
                target = t;
                break;
            }
        }
        if (target == null) return true; // not a scene we know how to scale

        int id = target.GetInstanceID();
        if (ByTarget.ContainsKey(id)) return true;

        // Frame height in panel units, with the target still at its original scale.
        float frameH = frame.rectTransform.rect.height * frame.transform.lossyScale.y / Mathf.Max(panel.lossyScale.y, 1e-6f);
        float frameW = frame.rectTransform.rect.width * frame.transform.lossyScale.x / Mathf.Max(panel.lossyScale.x, 1e-6f);
        if (frameH < panel.rect.height * 0.3f) return false; // still animating in; look again later
        float factor = Mathf.Min(panel.rect.height / frameH, panel.rect.width / Mathf.Max(frameW, 1f));
        if (factor < 1.01f) return true; // already as big as it gets

        var sc = new Scaled { Target = target, OrigScale = target.localScale, Factor = factor, FrameGo = frame.gameObject };
        ByTarget[id] = sc;
        Apply(sc);
        if (Plugin.DebugLog) Plugin.L.LogInfo($"Scaled pixel-art scene {target.name} by {factor:F2} (frame {frameW:F0}x{frameH:F0} -> full height)");
        return true;
    }

    static void Apply(Scaled sc)
    {
        var want = Active ? sc.OrigScale * sc.Factor : sc.OrigScale;
        if (sc.Target.localScale != want) sc.Target.localScale = want;
    }

    /// <summary>Called every LateUpdate: keeps the scale applied, undoes it when no longer ultrawide.</summary>
    public static void Update()
    {
        if (ByTarget.Count == 0) return;
        Dead.Clear();
        foreach (var kv in ByTarget)
        {
            var sc = kv.Value;
            if (sc.Target == null || sc.FrameGo == null) { Dead.Add(kv.Key); continue; }
            Apply(sc);
        }
        foreach (var k in Dead) ByTarget.Remove(k);
    }
}

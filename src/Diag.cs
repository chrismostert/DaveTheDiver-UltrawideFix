#if DIAG
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using BepInEx;
using Il2CppInterop.Runtime;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace DaveTheDiverUltrawide;

public class DiagBehaviour : MonoBehaviour
{
    float _t;
    void Update()
    {
        _t += Time.unscaledDeltaTime;
        if (_t < 0.5f) return;
        _t = 0;
        var trig = Path.Combine(Paths.BepInExRootPath, "dump.trigger");
        if (File.Exists(trig))
        {
            File.Delete(trig);
            try { Dump(); } catch (Exception e) { Plugin.L.LogError(e); }
        }
        var cmdf = Path.Combine(Paths.BepInExRootPath, "cmd.txt");
        if (File.Exists(cmdf))
        {
            var lines = File.ReadAllLines(cmdf);
            File.Delete(cmdf);
            foreach (var line in lines)
            {
                try { Cmd(line.Trim().Split(' ')); } catch (Exception e) { Plugin.L.LogError(e); }
            }
        }
    }

    void Cmd(string[] a)
    {
        Plugin.L.LogInfo("CMD " + string.Join(" ", a));
        switch (a[0])
        {
            case "res":
                Screen.SetResolution(int.Parse(a[1]), int.Parse(a[2]), (FullScreenMode)int.Parse(a[3]));
                break;
            case "dump":
                Dump();
                break;
            case "focus":
                Plugin.L.LogInfo($"focused={Application.isFocused} runInBg={Application.runInBackground} es={(UnityEngine.EventSystems.EventSystem.current != null ? UnityEngine.EventSystems.EventSystem.current.name : "-")}");
                break;
            case "click":
            {
                // click <full/path/of/object> (spaces in names: use '|' instead of space)
                var path = string.Join(" ", a, 1, a.Length - 1).Replace('|', ' ');
                var go = GameObject.Find(path);
                if (go == null) { Plugin.L.LogInfo("not found: " + path); break; }
                var es = UnityEngine.EventSystems.EventSystem.current;
                var ped = new UnityEngine.EventSystems.PointerEventData(es);
                UnityEngine.EventSystems.ExecuteEvents.Execute(go, ped, UnityEngine.EventSystems.ExecuteEvents.pointerEnterHandler);
                UnityEngine.EventSystems.ExecuteEvents.Execute(go, ped, UnityEngine.EventSystems.ExecuteEvents.pointerDownHandler);
                UnityEngine.EventSystems.ExecuteEvents.Execute(go, ped, UnityEngine.EventSystems.ExecuteEvents.pointerUpHandler);
                var ok = UnityEngine.EventSystems.ExecuteEvents.Execute(go, ped, UnityEngine.EventSystems.ExecuteEvents.pointerClickHandler);
                Plugin.L.LogInfo($"clicked {path}: {ok}");
                break;
            }
            case "invoke":
            {
                var path = string.Join(" ", a, 1, a.Length - 1).Replace('|', ' ');
                var go = GameObject.Find(path);
                if (go == null) { Plugin.L.LogInfo("not found: " + path); break; }
                var b = go.GetComponent<Button>();
                Plugin.L.LogInfo($"button={(b != null)} interactable={(b != null && b.interactable)} active={go.activeInHierarchy} listeners={(b != null ? b.onClick.GetPersistentEventCount() : -1)}");
                foreach (var cg in go.GetComponentsInParent<CanvasGroup>())
                    Plugin.L.LogInfo($"  CG {Path2(cg.transform)} inter={cg.interactable} block={cg.blocksRaycasts} alpha={cg.alpha}");
                b?.onClick.Invoke();
                break;
            }
            case "g1920":
            {
                var sb = new StringBuilder();
                foreach (var g in UnityEngine.Object.FindObjectsOfType<Graphic>())
                {
                    if (!g.isActiveAndEnabled || g.canvas == null) continue;
                    var rt = g.rectTransform;
                    var root = g.canvas.rootCanvas.GetComponent<RectTransform>();
                    if (root.rect.width <= 1930f) continue;
                    var r = rt.rect;
                    if (Mathf.Abs(r.width - 1920f) > 2f || r.height < 1000f) continue;
                    var img = g.TryCast<Image>();
                    sb.AppendLine($"{Path2(g.transform)} type={g.GetIl2CppType().Name} col={g.color} sprite={(img != null && img.sprite != null ? img.sprite.name : "-")} amin={rt.anchorMin} amax={rt.anchorMax} size={rt.sizeDelta} parentW={(rt.parent.TryCast<RectTransform>()?.rect.width ?? 0):F0}");
                }
                Plugin.L.LogInfo("G1920\n" + sb);
                break;
            }
            case "arf":
            {
                var sb = new StringBuilder();
                foreach (var f in UnityEngine.Object.FindObjectsOfType<AspectRatioFitter>(true))
                {
                    var rt = f.GetComponent<RectTransform>();
                    sb.AppendLine($"{Path2(f.transform)} act={f.isActiveAndEnabled} mode={f.aspectMode} ratio={f.aspectRatio:F3} rect={rt.rect.width:F0}x{rt.rect.height:F0}");
                }
                Plugin.L.LogInfo("ARF\n" + sb);
                break;
            }
            case "img":
            {
                var path = string.Join(" ", a, 1, a.Length - 1).Replace('|', ' ');
                var go = GameObject.Find(path);
                if (go == null) { Plugin.L.LogInfo("not found: " + path); break; }
                foreach (var g in go.GetComponents<Graphic>())
                {
                    var img = g.TryCast<Image>();
                    Plugin.L.LogInfo($"{g.GetIl2CppType().Name} col={g.color} mat={(g.material != null ? g.material.name + "/" + g.material.shader.name : "-")} default={(g.material == g.defaultMaterial)} raycast={g.raycastTarget} crAlpha={g.canvasRenderer.GetAlpha()}" +
                        (img != null ? $" sprite={(img.sprite != null ? img.sprite.name : "-")} type={img.type} fill={img.fillAmount}" : ""));
                }
                break;
            }
            case "at":
            {
                // at <screenX> <screenY> (pixels, origin bottom-left)
                var p = new Vector2(float.Parse(a[1]), float.Parse(a[2]));
                var sb = new StringBuilder();
                foreach (var g in UnityEngine.Object.FindObjectsOfType<Graphic>())
                {
                    if (!g.isActiveAndEnabled || g.canvas == null) continue;
                    var cam = g.canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : g.canvas.worldCamera;
                    if (!RectTransformUtility.RectangleContainsScreenPoint(g.rectTransform, p, cam)) continue;
                    var rt = g.rectTransform;
                    var img = g.TryCast<Image>();
                    sb.AppendLine($"{Path2(g.transform)} {g.GetIl2CppType().Name} col={g.color} sprite={(img != null && img.sprite != null ? img.sprite.name : "-")} rect={rt.rect.width:F0}x{rt.rect.height:F0} amin={rt.anchorMin} amax={rt.anchorMax} pos={rt.anchoredPosition} pivot={rt.pivot}");
                }
                Plugin.L.LogInfo("AT\n" + sb);
                break;
            }
            case "xf":
            {
                var path = string.Join(" ", a, 1, a.Length - 1).Replace('|', ' ');
                var go = GameObject.Find(path);
                if (go == null) { Plugin.L.LogInfo("not found: " + path); break; }
                for (var t = go.transform; t != null; t = t.parent)
                    Plugin.L.LogInfo($"  {t.name} rot={t.localEulerAngles} q={t.localRotation} scale={t.localScale}");
                break;
            }
            case "cloudls":
            {
                // The game keeps its saves in Steam Cloud (ISteamRemoteStorage) and overwrites the local
                // files from there on every start.
                int count = Steamworks.SteamRemoteStorage.GetFileCount();
                var sb = new StringBuilder();
                for (int i = 0; i < count; i++)
                {
                    var name = Steamworks.SteamRemoteStorage.GetFileNameAndSize(i, out int size);
                    sb.AppendLine($"  {name} {size} bytes, ts={Steamworks.SteamRemoteStorage.GetFileTimestamp(name)}");
                }
                Plugin.L.LogInfo($"CLOUD {count} files\n{sb}");
                break;
            }
            case "cloudput":
            {
                // cloudput <local folder>: writes each file in the folder to the cloud file with the same name
                // (only names that already exist in the cloud). Use to restore a save backup.
                var dir = string.Join(" ", a, 1, a.Length - 1);
                int count = Steamworks.SteamRemoteStorage.GetFileCount();
                var cloudNames = new List<string>();
                for (int i = 0; i < count; i++) cloudNames.Add(Steamworks.SteamRemoteStorage.GetFileNameAndSize(i, out _));
                foreach (var file in Directory.GetFiles(dir))
                {
                    var name = Path.GetFileName(file);
                    var cloudName = cloudNames.FirstOrDefault(n => n == name || n.EndsWith("/" + name));
                    if (cloudName == null) { Plugin.L.LogInfo($"  skip {name} (not in cloud)"); continue; }
                    var bytes = File.ReadAllBytes(file);
                    bool ok = Steamworks.SteamRemoteStorage.FileWrite(cloudName, bytes, bytes.Length);
                    Plugin.L.LogInfo($"  {cloudName} <- {name} ({bytes.Length} bytes): {ok}");
                }
                break;
            }
            case "wtv":
                Plugin.L.LogInfo($"WorldToViewportPoint postfix calls: {WorldUiFix.DiagCalls}");
                break;
            case "bytes":
            {
                // bytes <rva hex> ... : first 16 bytes of GameAssembly code at that RVA
                var ga = System.Diagnostics.Process.GetCurrentProcess().Modules.Cast<System.Diagnostics.ProcessModule>().First(m => m.ModuleName == "GameAssembly.dll").BaseAddress;
                foreach (var hex in a.Skip(1))
                {
                    var p = new IntPtr(ga.ToInt64() + Convert.ToInt64(hex, 16));
                    var buf = new byte[16];
                    System.Runtime.InteropServices.Marshal.Copy(p, buf, 0, 16);
                    Plugin.L.LogInfo($"{hex}: {BitConverter.ToString(buf)}");
                }
                break;
            }
            case "cust":
            {
                // Customer bubbles: where the anchor's world point is on screen vs where the bubble is.
                var sb = new StringBuilder();
                foreach (var info in UnityEngine.Object.FindObjectsOfType<CustomerActionInfo>())
                {
                    var rt = info.m_RectTransform;
                    var wt = info.m_WorldTransform;
                    if (rt == null || wt == null) continue;
                    var cam = info.TargetCamera;
                    var canvas = rt.GetComponentInParent<Canvas>();
                    var uiCam = canvas != null && canvas.rootCanvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.rootCanvas.worldCamera : null;
                    var want = cam != null ? cam.WorldToScreenPoint(wt.position) : Vector3.zero;
                    var got = RectTransformUtility.WorldToScreenPoint(uiCam, rt.position);
                    var parent = rt.parent.TryCast<RectTransform>();
                    sb.AppendLine($"{Path2(rt)} act={rt.gameObject.activeInHierarchy} cam={(cam != null ? cam.name + " " + R(cam.rect) : "-")} " +
                                  $"anchor={rt.anchorMin} wantScreen=({want.x:F0},{want.y:F0}) bubbleScreen=({got.x:F0},{got.y:F0}) dx={got.x - want.x:F0} parentW={(parent != null ? parent.rect.width : 0):F0}");
                }
                Plugin.L.LogInfo("CUST\n" + sb);
                break;
            }
            case "cams":
            {
                var sb = new StringBuilder();
                foreach (var cam in UnityEngine.Object.FindObjectsOfType<Camera>(true))
                    sb.AppendLine($"{Path2(cam.transform)} en={cam.isActiveAndEnabled} rect={R(cam.rect)} aspect={cam.aspect:F3} depth={cam.depth}");
                Plugin.L.LogInfo("CAMS\n" + sb);
                break;
            }
            case "tree":
            {
                // tree <rootName> [maxDepth]
                var go = GameObject.Find(a[1]);
                if (go == null)
                {
                    foreach (var c in UnityEngine.Object.FindObjectsOfType<Canvas>())
                        if (c.name == a[1] || Path2(c.transform).EndsWith(a[1])) { go = c.gameObject; break; }
                }
                if (go == null) { Plugin.L.LogInfo("not found"); break; }
                var sb = new StringBuilder();
                Tree(go.transform, 0, a.Length > 2 ? int.Parse(a[2]) : 4, sb);
                Plugin.L.LogInfo("TREE\n" + sb);
                break;
            }
        }
    }

    static void Tree(Transform t, int depth, int max, StringBuilder sb)
    {
        var rt = t.TryCast<RectTransform>();
        var comps = t.GetComponents<Component>();
        var cs = new StringBuilder();
        foreach (var c in comps)
        {
            var n = c.GetIl2CppType().Name;
            if (n is "Transform" or "RectTransform" or "CanvasRenderer") continue;
            cs.Append(n).Append(' ');
        }
        sb.Append(new string(' ', depth * 2)).Append(t.name).Append(t.gameObject.activeSelf ? "" : " [off]");
        if (rt != null) sb.Append($" amin={rt.anchorMin} amax={rt.anchorMax} pos={rt.anchoredPosition} size={rt.sizeDelta} rect={rt.rect.width:F0}x{rt.rect.height:F0} scale={rt.localScale.x:F2}");
        sb.Append(" {").Append(cs).Append('}').AppendLine();
        if (depth >= max) return;
        for (int i = 0; i < t.childCount; i++) Tree(t.GetChild(i), depth + 1, max, sb);
    }


    static string R(UnityEngine.Rect r) => $"({r.x:F3},{r.y:F3},{r.width:F3},{r.height:F3})";

    static string Path2(Transform t)
    {
        var s = t.name;
        while (t.parent != null) { t = t.parent; s = t.name + "/" + s; }
        return s;
    }

    void Dump()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Screen {Screen.width}x{Screen.height} mode={Screen.fullScreenMode} cur={Screen.currentResolution.width}x{Screen.currentResolution.height}");
        var cr = Singleton<CameraResolution>._instance;
        if (cr != null)
        {
            sb.AppendLine($"CR def={cr.m_DefaultScreenWidth}x{cr.m_DefaultScreenHeight} scr={cr.m_ScreenWidth}x{cr.m_ScreenHeight} mode={cr.m_ScrrenWindowModeType} viewRect={R(cr.m_CameraViewRect)} updated={cr.m_UpdatedViewRect} target={CameraResolution.k_TargetRatio}");
            foreach (var lb in new[] { cr.m_LeftLetterBoxModifier, cr.m_RightLetterBoxModifier, cr.m_TopLetterBoxModifier, cr.m_DownLetterBoxModifier })
                if (lb != null) sb.AppendLine($"  LB {Path2(lb.transform)} active={lb.gameObject.activeInHierarchy} min={lb.anchorMin} max={lb.anchorMax}");
        }
        foreach (var cam in Camera.allCameras)
            sb.AppendLine($"Cam {Path2(cam.transform)} rect={R(cam.rect)} pix={R(cam.pixelRect)} ortho={cam.orthographic} size={cam.orthographicSize:F3} fov={cam.fieldOfView:F2} aspect={cam.aspect:F3} depth={cam.depth} tex={(cam.targetTexture != null ? cam.targetTexture.width + "x" + cam.targetTexture.height : "-")}");
        foreach (var c in UnityEngine.Object.FindObjectsOfType<Canvas>())
        {
            if (!c.isRootCanvas) continue;
            var cs = c.GetComponent<CanvasScaler>();
            var rt = c.GetComponent<RectTransform>();
            sb.AppendLine($"Canvas {Path2(c.transform)} en={c.isActiveAndEnabled} mode={c.renderMode} cam={(c.worldCamera != null ? c.worldCamera.name : "-")} size={rt.sizeDelta} scale={c.scaleFactor:F3}" +
                (cs != null ? $" scaler={cs.uiScaleMode} ref={cs.referenceResolution} match={cs.matchWidthOrHeight} sm={cs.screenMatchMode}" : ""));
        }
        Plugin.L.LogInfo("DUMP\n" + sb);
    }
}

[HarmonyPatch]
public static class DiagHooks
{
    [HarmonyPostfix, HarmonyPatch(typeof(CameraResolution), nameof(CameraResolution.SetResolution))]
    static void SetRes(float width, float height) => Plugin.L.LogInfo($"SetResolution {width}x{height}");

    [HarmonyPostfix, HarmonyPatch(typeof(CameraResolution), nameof(CameraResolution.UpdateCameraRect))]
    static void UCR(CameraResolution __instance, Camera changeCamera) => Plugin.L.LogInfo($"UpdateCameraRect {(changeCamera != null ? changeCamera.name : "null")} -> {(changeCamera != null ? changeCamera.rect.ToString() : "")} viewRect={__instance.m_CameraViewRect}");

    [HarmonyPostfix, HarmonyPatch(typeof(CameraResolution), nameof(CameraResolution.IsWideResolution))]
    static void IWR(int width, int height, bool __result) => Plugin.L.LogInfo($"IsWideResolution {width}x{height} = {__result}");


    [HarmonyPostfix, HarmonyPatch(typeof(CameraResolution), nameof(CameraResolution.UpdateCanvasScale))]
    static void UCS() => Plugin.L.LogInfo("UpdateCanvasScale");



}
#endif

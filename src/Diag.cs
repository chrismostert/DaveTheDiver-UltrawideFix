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
                    sb.AppendLine($"{Path2(g.transform)} {g.GetIl2CppType().Name} col={g.color} sprite={(img != null && img.sprite != null ? img.sprite.name : "-")} effA={g.color.a * g.canvasRenderer.GetInheritedAlpha():F2} canvas={g.canvas.name}:{g.canvas.sortingOrder}");
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
            case "show":
            {
                // show <0|1> <root/child/path> : SetActive, also for inactive objects below an active root
                var path = string.Join(" ", a, 2, a.Length - 2).Replace('|', ' ');
                var t = FindAny(path);
                if (t == null) { Plugin.L.LogInfo("not found: " + path); break; }
                t.gameObject.SetActive(a[1] == "1");
                Plugin.L.LogInfo($"{path} active={t.gameObject.activeInHierarchy}");
                break;
            }
            case "tsl":
            {
                // tsl : shows the Time Skip loading screen (visual only, does not skip time); tsl off hides it
                var loader = UnityEngine.Object.FindObjectsOfType<TimeSkipLoader>(true).FirstOrDefault();
                if (loader == null) { Plugin.L.LogInfo("no TimeSkipLoader"); break; }
                var fade = loader.transform.parent;
                if (a.Length > 1 && a[1] == "off")
                {
                    loader.Close();
                    loader.gameObject.SetActive(false);
                    fade.gameObject.SetActive(false);
                    break;
                }
                fade.gameObject.SetActive(true);
                var cg = fade.GetComponent<CanvasGroup>();
                if (cg != null) cg.alpha = 1f;
                loader.gameObject.SetActive(true);
                loader.StartCoroutine(loader.DoDaySkipLoading(default));
                Plugin.L.LogInfo("time skip loader started");
                break;
            }
            case "goto":
            {
                // goto <sceneName> : loads a scene like the game's travel does (no story checks; may change the save)
                var gl = Singleton<GlobalSceneLoader>._instance;
                if (gl == null || gl.sceneLoader == null) { Plugin.L.LogInfo("no scene loader"); break; }
                gl.sceneLoader.ChangeSceneAsync(a[1], SceneTransitionType.None, true, false, true, true, false, null, null, true, false, false, false);
                Plugin.L.LogInfo($"loading {a[1]}");
                break;
            }
            case "ui":
            {
                // ui <dmg|emoji|talk|guide|sub> <vx> [vy] : spawns world UI on a dummy target at viewport (vx, vy)
                // of the main camera (full screen, 0..1), at the depth of the player
                var cam = Camera.main;
                if (cam == null) { Plugin.L.LogInfo("no main camera"); break; }
                float vx = float.Parse(a[2], System.Globalization.CultureInfo.InvariantCulture);
                float vy = a.Length > 3 ? float.Parse(a[3], System.Globalization.CultureInfo.InvariantCulture) : 0.5f;
                float depth = 10f;
                var player = GameObject.FindWithTag("Player");
                if (player != null) depth = Vector3.Dot(player.transform.position - cam.transform.position, cam.transform.forward);
                var world = cam.ViewportToWorldPoint(new Vector3(vx, vy, depth));
                var target = new GameObject($"DiagTarget_{a[1]}_{vx:F2}").transform;
                target.position = world;
                UnityEngine.Object.Destroy(target.gameObject, 8f);
                Component panel = null;
                switch (a[1])
                {
                    case "dmg": { var p = FindActiveFirst<DamageTextPoolPanel>(); panel = p; p?.ShowDamageText(123, target, world); break; }
                    case "emoji": { var p = FindActiveFirst<EmojiPanel>(); panel = p; p?.ShowEmoji(target, EmojiType.Question, 6f); break; }
                    case "talk": { var p = FindActiveFirst<TalkBoxPanel>(); panel = p; p?.ShowMessage(target, "Test", 6f, 0f, null); break; }
                    case "guide": { var p = FindActiveFirst<GuideHelperPanel>(); panel = p; p?.ShowGuidePointerUI(target, true, GuideHelperPanel.GuidePointType.Normal); break; }
                    case "sub": { var p = FindActiveFirst<SubHelperUtilPanel>(); panel = p; p?.ShowSubHelperEffect(SubHelperEffectUIType.Reload, target, Vector3.zero); break; }
                    default: Plugin.L.LogInfo("unknown kind"); break;
                }
                Plugin.L.LogInfo($"  panel: {(panel != null ? Path2(panel.transform) + (panel.gameObject.activeInHierarchy ? "" : " [inactive]") : "not found")}");
                Plugin.L.LogInfo($"ui {a[1]} at viewport ({vx}, {vy}) world {world} depth {depth:F1}");
                break;
            }
            case "gfx":
            {
                // gfx <name> : clipping state of every graphic under a GameObject (found like "tree")
                var go = GameObject.Find(a[1]);
                if (go == null) { Plugin.L.LogInfo("not found"); break; }
                var sb = new StringBuilder();
                foreach (var g in go.GetComponentsInChildren<MaskableGraphic>(true))
                {
                    var cr = g.canvasRenderer;
                    var rm = g.GetComponentInParent<RectMask2D>();
                    var mk = g.GetComponentInParent<Mask>();
                    sb.AppendLine($"  {Path2(g.transform)} act={g.isActiveAndEnabled} maskable={g.maskable} cull={cr.cull} rectClip={cr.hasRectClipping} alpha={g.color.a * cr.GetInheritedAlpha():F2} rectMask2D={(rm != null ? rm.name : "-")} mask={(mk != null ? mk.name + (mk.enabled ? "" : "(off)") : "-")}");
                }
                Plugin.L.LogInfo("GFX\n" + sb);
                break;
            }
            case "wui":
                Plugin.L.LogInfo("WUI\n" + WuiTrack.Report());
                break;
            case "wuiclear":
                WuiTrack.Entries.Clear();
                break;
            case "findtest":
            {
                // findtest <TypeName> : cost of the different ways to find all objects of a type
                var t = AppDomain.CurrentDomain.GetAssemblies()
                    .Where(x => !x.IsDynamic && x.Location.Replace('\\', '/').Contains("/BepInEx/interop/"))
                    .Select(x => x.GetType(a[1])).FirstOrDefault(x => x != null);
                if (t == null) { Plugin.L.LogInfo("type not found"); break; }
                var it = Il2CppType.From(t);
                var sw = new System.Diagnostics.Stopwatch();
                int n;
                sw.Restart(); n = UnityEngine.Object.FindObjectsOfType(it, true).Length; sw.Stop();
                Plugin.L.LogInfo($"FindObjectsOfType(incl inactive): {n} in {sw.Elapsed.TotalMilliseconds:F3} ms");
                sw.Restart(); n = UnityEngine.Object.FindObjectsOfType(it, false).Length; sw.Stop();
                Plugin.L.LogInfo($"FindObjectsOfType(active): {n} in {sw.Elapsed.TotalMilliseconds:F3} ms");
                sw.Restart(); n = UnityEngine.Object.FindObjectsByType(it, FindObjectsInactive.Include, FindObjectsSortMode.None).Length; sw.Stop();
                Plugin.L.LogInfo($"FindObjectsByType(incl inactive, unsorted): {n} in {sw.Elapsed.TotalMilliseconds:F3} ms");
                sw.Restart(); n = UnityEngine.Object.FindObjectsByType(it, FindObjectsInactive.Exclude, FindObjectsSortMode.None).Length; sw.Stop();
                Plugin.L.LogInfo($"FindObjectsByType(active, unsorted): {n} in {sw.Elapsed.TotalMilliseconds:F3} ms");
                break;
            }
            case "perf":
                // perf : time per module since the last "perf" (the first call just starts measuring)
                Plugin.L.LogInfo("PERF\n" + Perf.Report());
                break;
            case "wtv":
                Plugin.L.LogInfo($"WorldToViewportPoint postfix calls: {WorldUiFix.DiagCalls}, remapped: {WorldUiFix.DiagRemapped}");
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
            case "edges":
            {
                // edges [minPx] : world renderers that reach into the side areas outside the 16:9 strip,
                // with their screen-space extent (WorldToScreenPoint is not remapped by WorldUiFix)
                var cam = Camera.main;
                if (cam == null) { Plugin.L.LogInfo("no main camera"); break; }
                float minPx = a.Length > 1 ? float.Parse(a[1]) : 40f;
                float w = Screen.width, h = Screen.height, strip = h * 16f / 9f;
                float l = (w - strip) / 2, r = w - l;
                var sb = new StringBuilder($"cam {Path2(cam.transform)} ortho={cam.orthographic} size={cam.orthographicSize:F2} pos={cam.transform.position} strip {l:F0}..{r:F0}\n");
                foreach (var rend in UnityEngine.Object.FindObjectsOfType<Renderer>())
                {
                    if (!rend.isVisible || !rend.enabled) continue;
                    var b = rend.bounds;
                    var p0 = cam.WorldToScreenPoint(b.min);
                    var p1 = cam.WorldToScreenPoint(b.max);
                    float x0 = Mathf.Min(p0.x, p1.x), x1 = Mathf.Max(p0.x, p1.x), y0 = Mathf.Min(p0.y, p1.y), y1 = Mathf.Max(p0.y, p1.y);
                    if (x1 - x0 < minPx && y1 - y0 < minPx) continue;
                    if (x0 > l && x1 < r) continue;
                    if (x1 < 0 || x0 > w || y1 < 0 || y0 > h) continue;
                    string flags = (x0 > 0 && x0 < l ? "Ledge " : "") + (x1 < w && x1 > r ? "Redge " : "");
                    var sr = rend.TryCast<SpriteRenderer>();
                    string extra = sr != null ? $"sprite={(sr.sprite != null ? sr.sprite.name : "-")} draw={sr.drawMode} col={sr.color}" : rend.GetIl2CppType().Name;
                    var comps = string.Join(",", rend.GetComponents<Component>().Select(c => c.GetIl2CppType().Name).Where(n => n != "Transform"));
                    sb.AppendLine($"{flags}x={x0:F0}..{x1:F0} y={y0:F0}..{y1:F0} z={b.center.z:F1} L={rend.sortingLayerName}/{rend.sortingOrder} {Path2(rend.transform)} {extra} [{comps}]");
                }
                Plugin.L.LogInfo("EDGES\n" + sb);
                break;
            }
            case "blur":
            {
                // blur : how the game's blur is wired (KawaseBlur features, RendererFeatureCameras, blur back panels)
                var sb = new StringBuilder();
                foreach (var kb in Resources.FindObjectsOfTypeAll<KawaseBlur>())
                {
                    var s = kb.settings;
                    sb.AppendLine($"KawaseBlur '{kb.name}' active={kb.isActive} en={s.IsEnabled} evt={s.renderPassEvent} passes={s.blurPasses} down={s.downsample} toFB={s.copyToFramebuffer} target='{s.targetName}' mat={MatInfo(s.blurMaterial)}");
                }
                foreach (var rfc in UnityEngine.Object.FindObjectsOfType<RendererFeatureCamera>(true))
                {
                    var c = rfc.Cam;
                    sb.AppendLine($"RFC {Path2(rfc.transform)} act={rfc.isActiveAndEnabled} blurOn={rfc.IsBlurEnabled} cam={(c != null ? $"{Path2(c.transform)} en={c.isActiveAndEnabled} depth={c.depth} rect={R(c.rect)} tex={(c.targetTexture != null ? c.targetTexture.name : "-")} mask={c.cullingMask:X}" : "-")} blurF={(rfc.BlurFeature != null ? rfc.BlurFeature.name : "-")} photoF={(rfc.BlurPhotoFeature != null ? rfc.BlurPhotoFeature.name : "-")}");
                }
                foreach (var p in UnityEngine.Object.FindObjectsOfType<LobbyblurBackPanel>(true))
                {
                    var img = p.BackImage;
                    sb.AppendLine($"BlurPanel {Path2(p.transform)} act={p.gameObject.activeInHierarchy} blurred={p.IsBlurred} camRef={p.hasblurCamRef} img={(img != null ? $"en={img.enabled} sprite={(img.sprite != null ? img.sprite.name : "-")} col={img.color} mat={MatInfo(img.material)}" : "-")}");
                }
                var cm = Singleton<CameraManager>._instance;
                if (cm != null) sb.AppendLine($"CameraManager blurCam={(cm.blurCamera != null ? Path2(cm.blurCamera.transform) : "-")} rfc={(cm.RendererFeatureCam != null ? Path2(cm.RendererFeatureCam.transform) : "-")}");
                Plugin.L.LogInfo("BLUR\n" + sb);
                break;
            }
            case "texdump":
            {
                // texdump <textureName> : saves the texture (GPU copy) as BepInEx/<name>.png
                var tex = Resources.FindObjectsOfTypeAll<Texture>().FirstOrDefault(t => t.name == a[1]);
                if (tex == null) { Plugin.L.LogInfo("no texture " + a[1]); break; }
                var rt = RenderTexture.GetTemporary(tex.width, tex.height, 0, RenderTextureFormat.ARGB32);
                Graphics.Blit(tex, rt);
                var prev = RenderTexture.active; RenderTexture.active = rt;
                var t2 = new Texture2D(tex.width, tex.height, TextureFormat.RGBA32, false);
                t2.ReadPixels(new UnityEngine.Rect(0, 0, tex.width, tex.height), 0, 0); t2.Apply();
                RenderTexture.active = prev; RenderTexture.ReleaseTemporary(rt);
                var file = Path.Combine(Paths.BepInExRootPath, a[1] + ".png");
                File.WriteAllBytes(file, ImageConversion.EncodeToPNG(t2));
                Plugin.L.LogInfo($"wrote {file} ({tex.width}x{tex.height})");
                break;
            }
            case "blurtest":
            {
                // blurtest <on|off> [mask <edgeFrac>|nomask] [passes <n>] : toggles the blur camera of the first
                // active blur back panel, optionally with a generated side mask (white outside the 16:9 strip,
                // ramping over edgeFrac of the strip width) on its KawaseBlur material
                var rfc = UnityEngine.Object.FindObjectsOfType<RendererFeatureCamera>().FirstOrDefault(r => r.isActiveAndEnabled);
                if (rfc == null) { Plugin.L.LogInfo("no active RendererFeatureCamera"); break; }
                var mat = rfc.BlurFeature?.settings.blurMaterial;
                for (int i = 2; i < a.Length; i++)
                {
                    if (a[i] == "mask" && mat != null)
                    {
                        float ramp = float.Parse(a[++i], System.Globalization.CultureInfo.InvariantCulture);
                        const int N = 1024;
                        var m = new Texture2D(N, 4, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "UltrawideSideMask" };
                        float strip = Screen.height * 16f / 9f / Screen.width, l = (1 - strip) / 2;
                        for (int x = 0; x < N; x++)
                        {
                            float u = (x + 0.5f) / N;
                            float d = Mathf.Max(l - u, u - (1 - l)); // >0 outside the strip
                            float v = Mathf.SmoothStep(0, 1, Mathf.Clamp01((d + ramp * strip) / (ramp * strip)));
                            for (int y = 0; y < 4; y++) m.SetPixel(x, y, new Color(v, v, v, v));
                        }
                        m.Apply();
                        mat.SetTexture("_MaskTex", m);
                    }
                    else if (a[i] == "nomask" && mat != null) mat.SetTexture("_MaskTex", null);
                    else if (a[i] == "passes" || a[i] == "down")
                    {
                        // applies to every KawaseBlur instance (rfc.BlurFeature may not be the one that renders)
                        int v = int.Parse(a[++i]);
                        foreach (var kb in Resources.FindObjectsOfTypeAll<KawaseBlur>())
                        {
                            if (a[i - 1] == "passes") { kb.settings.blurPasses = v; kb.UpdatePasses(v); }
                            else { kb.settings.downsample = v; kb.Create(); }
                        }
                    }
                }
                if (a[1] == "on") rfc.EnableBlur(false); else rfc.DisableBlur(false);
                Plugin.L.LogInfo($"blurtest {Path2(rfc.transform)} on={rfc.IsBlurEnabled} mat={MatInfo(mat)}");
                break;
            }
            case "findperf":
            {
                // findperf [name] : cost of one GameObject.Find (what SushiBarScenery does every 0.5 s while not found)
                var name = a.Length > 1 ? a[1] : "Lobby_Env_SushiBar";
                var sw = System.Diagnostics.Stopwatch.StartNew();
                GameObject go = null;
                for (int i = 0; i < 100; i++) go = GameObject.Find(name);
                sw.Stop();
                int count = UnityEngine.Object.FindObjectsOfType<Transform>().Length;
                Plugin.L.LogInfo($"findperf '{name}' found={(go != null ? Path2(go.transform) : "-")} {sw.Elapsed.TotalMilliseconds * 10:F1} us per call, {count} active transforms");
                break;
            }
            case "boats":
                Plugin.L.LogInfo("BOATS\n" + NpcBoats.Describe());
                break;
            case "boatin":
            {
                // boatin <CharacterType number> : sails that NPC boat in like a story event (locks input until it arrives)
                var dock = NpcBoats.Dock;
                if (dock == null) { Plugin.L.LogInfo("no dock"); break; }
                var type = (CharacterType)int.Parse(a[1]);
                dock.EnterNPCBoat(type, (Il2CppSystem.Action)(System.Action)(() => Plugin.L.LogInfo($"boat {type} arrived")));
                break;
            }
            case "boatout":
            {
                // boatout : sends the current NPC boat away again
                var dock = NpcBoats.Dock;
                if (dock == null) { Plugin.L.LogInfo("no dock"); break; }
                dock.ExitNPCBoat((Il2CppSystem.Action)(System.Action)(() => Plugin.L.LogInfo("boat left")));
                break;
            }
            case "mesh":
            {
                // mesh <path> : transform, mesh bounds, material/texture and the screen position of the
                // mesh's local bounds corners (to see where a scenery card ends on screen)
                var path = string.Join(" ", a, 1, a.Length - 1).Replace('|', ' ');
                var t = FindAny(path);
                if (t == null) { Plugin.L.LogInfo("not found: " + path); break; }
                var sb = new StringBuilder($"MESH {Path2(t)} pos={t.position} lpos={t.localPosition} rot={t.eulerAngles} lscale={t.localScale} lossy={t.lossyScale}\n");
                var mf = t.GetComponent<MeshFilter>();
                var mr = t.GetComponent<Renderer>();
                if (mf != null && mf.sharedMesh != null)
                {
                    var m = mf.sharedMesh;
                    var lb = m.bounds;
                    sb.AppendLine($"  mesh={m.name} verts={m.vertexCount} lbounds c={lb.center} ext={lb.extents}");
                    var cam = Camera.main;
                    for (int i = 0; i < 8 && cam != null; i++)
                    {
                        var c = lb.center + Vector3.Scale(lb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                        var w = t.TransformPoint(c);
                        var s = cam.WorldToScreenPoint(w);
                        sb.AppendLine($"  corner {c} -> world {w} -> screen ({s.x:F0}, {s.y:F0}, {s.z:F1})");
                    }
                }
                if (mr != null)
                    foreach (var mat in mr.sharedMaterials)
                    {
                        if (mat == null) continue;
                        var tex = mat.mainTexture;
                        sb.AppendLine($"  mat={mat.name} shader={mat.shader.name} queue={mat.renderQueue} tex={(tex != null ? $"{tex.name} {tex.width}x{tex.height} wrap={tex.wrapMode}" : "-")} st={mat.mainTextureScale}/{mat.mainTextureOffset}");
                    }
                Plugin.L.LogInfo(sb.ToString());
                break;
            }
            case "xfset":
            {
                // xfset <px> <py> <pz> <sx> <sy> <sz> <path> : set localPosition and localScale
                var path = string.Join(" ", a, 7, a.Length - 7).Replace('|', ' ');
                var t = FindAny(path);
                if (t == null) { Plugin.L.LogInfo("not found: " + path); break; }
                t.localPosition = new Vector3(F(a[1]), F(a[2]), F(a[3]));
                t.localScale = new Vector3(F(a[4]), F(a[5]), F(a[6]));
                Plugin.L.LogInfo($"{path} lpos={t.localPosition} lscale={t.localScale}");
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

    /// <summary>Finds "root/child/..." where children may be inactive (GameObject.Find only sees active ones).</summary>
    static float F(string s) => float.Parse(s, System.Globalization.CultureInfo.InvariantCulture);

    static Transform FindAny(string path)
    {
        var go = GameObject.Find(path);
        if (go != null) return go.transform;
        int slash = path.IndexOf('/');
        var root = GameObject.Find(slash < 0 ? path : path.Substring(0, slash));
        if (root == null) return null;
        return slash < 0 ? root.transform : root.transform.Find(path.Substring(slash + 1));
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


    static string MatInfo(Material m)
    {
        if (m == null) return "-";
        var sh = m.shader;
        var sb = new StringBuilder($"{m.name} shader={(sh != null ? sh.name : "-")}");
        if (sh != null)
            for (int i = 0; i < sh.GetPropertyCount(); i++)
            {
                var n = sh.GetPropertyName(i);
                var t = sh.GetPropertyType(i);
                sb.Append($" {n}:{t}");
                if (t == UnityEngine.Rendering.ShaderPropertyType.Texture) { var tx = m.GetTexture(n); sb.Append($"={(tx != null ? tx.name + " " + tx.width + "x" + tx.height : "-")}"); }
                else if (t is UnityEngine.Rendering.ShaderPropertyType.Float or UnityEngine.Rendering.ShaderPropertyType.Range) sb.Append($"={m.GetFloat(n):F2}");
            }
        return sb.ToString();
    }

    static string R(UnityEngine.Rect r) => $"({r.x:F3},{r.y:F3},{r.width:F3},{r.height:F3})";

    internal static string PathOf(Transform t) => Path2(t);

    /// <summary>An active instance if there is one (scenes keep inactive copies of HUD panels), else any.</summary>
    static T FindActiveFirst<T>() where T : Component
    {
        T any = null;
        foreach (var c in UnityEngine.Object.FindObjectsOfType<T>(true))
        {
            if (c.gameObject.activeInHierarchy) return c;
            any ??= c;
        }
        return any;
    }

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

/// <summary>
/// Alignment check for world-following UI (DIAG command "wui"): records which RectTransform a hooked
/// world-UI method positions and which world point it projected just before, then reports where the
/// element is on screen relative to that point. A correctly placed element keeps the same offset
/// wherever its target is; a wrong remap makes the offset grow with the distance from the center.
/// </summary>
[HarmonyPatch]
public static class WuiTrack
{
    public class Entry
    {
        public RectTransform Rt;
        public Vector3 World;
        public Camera Cam;
        public string Type;
        public int Frame;
    }

    public static readonly Dictionary<IntPtr, Entry> Entries = new();

    [HarmonyPostfix, HarmonyPatch(typeof(RectTransform), nameof(RectTransform.anchorMin), MethodType.Setter)]
    static void AnchorMin(RectTransform __instance) => Record(__instance);

    [HarmonyPostfix, HarmonyPatch(typeof(RectTransform), nameof(RectTransform.anchoredPosition), MethodType.Setter)]
    static void AnchoredPosition(RectTransform __instance) => Record(__instance);

    static void Record(RectTransform rt)
    {
        if (WorldUiFix.DiagScope <= 0 || WorldUiFix.DiagLastCam == null || rt == null) return;
        if (!Entries.TryGetValue(rt.Pointer, out var e)) Entries[rt.Pointer] = e = new Entry { Rt = rt };
        e.World = WorldUiFix.DiagLastWorld;
        e.Cam = WorldUiFix.DiagLastCam;
        e.Type = WorldUiFix.DiagScopeType;
        e.Frame = Time.frameCount;
    }

    public static string Report()
    {
        var sb = new StringBuilder($"screen {Screen.width}x{Screen.height}; offset = element center - projected target (px)\n");
        foreach (var e in Entries.Values.OrderBy(x => x.Type).ThenBy(x => x.Cam != null ? x.Cam.WorldToScreenPoint(x.World).x : 0))
        {
            if (e.Rt == null || e.Cam == null || !e.Rt.gameObject.activeInHierarchy) continue;
            if (Time.frameCount - e.Frame > 600) continue; // not positioned for ~5 s
            var target = e.Cam.WorldToScreenPoint(e.World);
            var canvas = e.Rt.GetComponentInParent<Canvas>();
            if (canvas == null) continue;
            var root = canvas.rootCanvas;
            var uiCam = root.renderMode == RenderMode.ScreenSpaceOverlay ? null : root.worldCamera;
            var center = RectTransformUtility.WorldToScreenPoint(uiCam, e.Rt.TransformPoint(e.Rt.rect.center));
            sb.AppendLine($"  {e.Type,-40} targetX={target.x,6:F0} ({target.x / Screen.width:F2}) offset=({center.x - target.x,6:F0},{center.y - target.y,6:F0}) {DiagBehaviour.PathOf(e.Rt)}");
        }
        return sb.ToString();
    }
}

/// <summary>Per-module time spent in the plugin's LateUpdate and hooks (DIAG command "perf").</summary>
public static class Perf
{
    public static readonly string[] Names = { "Apply", "OverlayExtender", "WorldUiFix", "PixelCutscenes", "SushiBarScenery", "NpcBoats", "LobbyClouds", "Remap hook (own time)" };
    public const int Remap = 7;
    static readonly long[] Total = new long[Names.Length];
    static readonly long[] Max = new long[Names.Length];
    static readonly long[] Frame = new long[Names.Length];
    static readonly int[] Calls = new int[Names.Length];
    static int _frames;

    /// <summary>Adds the time since <paramref name="t"/> to a slot and restarts <paramref name="t"/>.</summary>
    public static void Mark(int slot, ref long t)
    {
        long now = System.Diagnostics.Stopwatch.GetTimestamp();
        Frame[slot] += now - t;
        Calls[slot]++;
        t = now;
    }

    public static void Add(int slot, long ticks) { Frame[slot] += ticks; Calls[slot]++; }

    /// <summary>Closes the current frame (frame maxima are per frame, so hook calls add up).</summary>
    public static void EndFrame()
    {
        _frames++;
        for (int i = 0; i < Names.Length; i++)
        {
            Total[i] += Frame[i];
            if (Frame[i] > Max[i]) Max[i] = Frame[i];
            Frame[i] = 0;
        }
    }

    public static string Report()
    {
        double us = 1e6 / System.Diagnostics.Stopwatch.Frequency;
        var sb = new StringBuilder($"{_frames} frames\n");
        long sum = 0;
        for (int i = 0; i < Names.Length; i++)
        {
            sum += Total[i];
            sb.AppendLine($"  {Names[i],-24} avg {Total[i] * us / Math.Max(1, _frames),8:F1} us/frame   max {Max[i] * us,8:F1} us   calls/frame {(double)Calls[i] / Math.Max(1, _frames):F1}");
        }
        sb.AppendLine($"  {"total",-24} avg {sum * us / Math.Max(1, _frames),8:F1} us/frame");
        Array.Clear(Total); Array.Clear(Max); Array.Clear(Calls); _frames = 0;
        return sb.ToString();
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

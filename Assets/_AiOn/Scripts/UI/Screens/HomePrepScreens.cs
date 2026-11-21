// 우리 집 안전 준비 (competition upgrade): the hub ("homeprep", from the title and the learning report) and its three
// screens: 우리 가족 비상연락카드 (a form beside a live A6 preview, saved only as a PNG), 우리 집 안전 점검표 (checked by
// hand, saved as a PNG too) and 안전체험관 안내 (agency sites as text; no network). Nothing typed or checked here is
// kept: no game save, no player prefs, no file but the PNG the player asks for, in their Pictures folder (or the game's
// own data folder when Pictures is synced to a cloud drive).
// Rules and wording: Game/HomePrep.cs (FamilyCard, HomeChecklist, TrainingGuide, PrintLayout).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using AiOn.Game;
using AiOn.Story;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace AiOn.UI
{
    // =====================================================================================================
    // print art: the A6 card and checklist, drawn the same way on screen (preview) and into the PNG
    // =====================================================================================================
    public static class HomePrepArt
    {
        public static readonly Color Paper = Color.white;
        public static readonly Color Ink = Terminal.Hex("#1C2326");
        public static readonly Color Dim = Terminal.Hex("#5B666B");
        public static readonly Color Rule = Terminal.Hex("#C5CED1");
        public static readonly Color Band = Terminal.Hex("#0F5D57");
        public static readonly Color Head = Terminal.Hex("#0F5D57");
        public static readonly Color Hot = Terminal.Hex("#B45309");
        public static readonly Color Done = Terminal.Hex("#047857");

        /// <summary>A print text: exact size (no 글자 크기 scaling: the paper is the paper), no rich text from what was typed.</summary>
        static Text T(Transform parent, string text, int size, Color c, float x, float y, float w, float h,
            TextAnchor a = TextAnchor.UpperLeft, FontStyle st = FontStyle.Normal, bool fit = false)
        {
            var rt = UiKit.Rect("T", parent);
            var t = rt.gameObject.AddComponent<Text>();
            t.font = UiKit.Font; t.fontSize = size; t.color = c; t.alignment = a; t.fontStyle = st;
            t.supportRichText = false; t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = fit ? VerticalWrapMode.Truncate : VerticalWrapMode.Overflow;
            if (fit) { t.resizeTextForBestFit = true; t.resizeTextMinSize = Mathf.Max(10, size * 3 / 5); t.resizeTextMaxSize = size; }
            t.text = text ?? "";
            UiKit.At(rt, x, y, w, h);
            return t;
        }

        static Image B(Transform parent, Color c, float x, float y, float w, float h)
        {
            var img = UiKit.Box("B", parent, c);
            UiKit.At(img.rectTransform, x, y, w, h);
            return img;
        }

        /// <summary>
        /// The family card into root (w x h, top-left origin). value(id) gives each field's text (already cleaned). Returns
        /// the value texts by field id, for the live preview. hotNote: the fact printed under the hotlines (comms_112).
        /// </summary>
        public static Dictionary<string, Text> Card(RectTransform root, int w, int h, Func<string, string> value, string footLine, string hotNote)
        {
            float s = w / 1240f;
            int Px(float v) => Mathf.Max(8, Mathf.RoundToInt(v * s));
            var values = new Dictionary<string, Text>();
            B(root, Paper, 0, 0, w, h);
            foreach (var c in PrintLayout.Card(w, h))
            {
                switch (c.Kind)
                {
                    case "band":
                        B(root, Band, c.X, c.Y, c.W, c.H);
                        T(root, c.Text, Px(62), Color.white, PrintLayout.Margin * s, c.Y + 34 * s, c.W - PrintLayout.Margin * 2 * s, 80 * s, st: FontStyle.Bold);
                        T(root, FamilyCard.CardLead, Px(28), new Color(1, 1, 1, 0.86f), PrintLayout.Margin * s, c.Y + 122 * s, c.W - PrintLayout.Margin * 2 * s, 40 * s);
                        break;
                    case "section":
                        T(root, c.Text, Px(30), Head, c.X, c.Y + 4 * s, c.W, c.H - 8 * s, TextAnchor.MiddleLeft, FontStyle.Bold);
                        B(root, Head, c.X, c.Bottom - 3 * s, c.W, Mathf.Max(1, 3 * s));
                        break;
                    case "field":
                        T(root, c.Text, Px(26), Dim, c.X + 4 * s, c.Y + 2 * s, c.W - 8 * s, 32 * s);
                        values[c.Id] = T(root, value?.Invoke(c.Id) ?? "", Px(36), Ink, c.X + 4 * s, c.Y + 34 * s, c.W - 8 * s, 40 * s, TextAnchor.MiddleLeft, fit: true);
                        B(root, Rule, c.X, c.Bottom - 2 * s, c.W, Mathf.Max(1, 2 * s)); // a line to write on by hand
                        break;
                    case "hotline":
                        T(root, c.Id, Px(38), Hot, c.X + 4 * s, c.Y, 150 * s, c.H, TextAnchor.MiddleLeft, FontStyle.Bold);
                        T(root, c.Text, Px(34), Ink, c.X + 160 * s, c.Y, c.W - 164 * s, c.H, TextAnchor.MiddleLeft);
                        break;
                    case "hotnote":
                        T(root, hotNote ?? "", Px(27), Ink, c.X + 4 * s, c.Y, c.W - 8 * s, c.H, TextAnchor.MiddleLeft, fit: true);
                        break;
                    case "note":
                        B(root, Rule, c.X, c.Y, c.W, Mathf.Max(1, 2 * s));
                        T(root, c.Text + (string.IsNullOrEmpty(footLine) ? "" : "\n" + footLine), Px(25), Dim, c.X, c.Y + 10 * s, c.W, c.H - 10 * s);
                        break;
                }
            }
            return values;
        }

        /// <summary>The checklist print: each item's mark (✓ / – / blank box to tick by hand), title, and where or how.</summary>
        public static void Checklist(RectTransform root, int w, int h, IReadOnlyDictionary<string, HomeChecklist.Mark> marks, string date, string sources)
        {
            float s = w / 1240f;
            int Px(float v) => Mathf.Max(8, Mathf.RoundToInt(v * s));
            B(root, Paper, 0, 0, w, h);
            foreach (var c in PrintLayout.Checklist(w, h))
            {
                switch (c.Kind)
                {
                    case "band":
                        B(root, Band, c.X, c.Y, c.W, c.H);
                        T(root, c.Text, Px(62), Color.white, PrintLayout.Margin * s, c.Y + 34 * s, c.W - PrintLayout.Margin * 2 * s, 80 * s, st: FontStyle.Bold);
                        T(root, HomeChecklist.Subtitle + " · 직접 보고 확인", Px(28), new Color(1, 1, 1, 0.86f), PrintLayout.Margin * s, c.Y + 122 * s, c.W - PrintLayout.Margin * 2 * s, 40 * s);
                        break;
                    case "date":
                        T(root, $"{c.Text}  {date}", Px(28), Ink, c.X, c.Y, c.W * 0.45f, c.H, TextAnchor.MiddleLeft, FontStyle.Bold);
                        T(root, HomeChecklist.Summary(marks), Px(26), Dim, c.X + c.W * 0.45f, c.Y, c.W * 0.55f, c.H, TextAnchor.MiddleRight, fit: true);
                        break;
                    case "item":
                    {
                        var item = HomeChecklist.Get(c.Id);
                        var m = HomeChecklist.MarkOf(marks, c.Id);
                        float box = 58 * s, bx = c.X + 2 * s, by = c.Y + 18 * s;
                        Color tone = MarkInk(m);
                        B(root, tone, bx, by, box, box);
                        B(root, Paper, bx + 4 * s, by + 4 * s, box - 8 * s, box - 8 * s);
                        string glyph = m == HomeChecklist.Mark.Done ? "✓" : m == HomeChecklist.Mark.Fix ? "!" : m == HomeChecklist.Mark.Absent ? "–" : null;
                        if (glyph != null) T(root, glyph, Px(46), tone, bx, by, box, box, TextAnchor.MiddleCenter, FontStyle.Bold);
                        float tx = c.X + 90 * s, tw = c.W - 90 * s;
                        string state = m == HomeChecklist.Mark.Todo ? "" : HomeChecklist.MarkText(m);
                        T(root, item.Title, Px(34), Ink, tx, c.Y + 12 * s, tw * 0.7f, 46 * s, TextAnchor.MiddleLeft, FontStyle.Bold);
                        T(root, state, Px(24), tone, tx + tw * 0.7f, c.Y + 12 * s, tw * 0.3f, 46 * s, TextAnchor.MiddleRight, FontStyle.Bold);
                        T(root, item.Where ?? item.How, Px(22), Dim, tx, c.Y + 60 * s, tw, 58 * s, fit: true);
                        B(root, Rule, c.X, c.Bottom - 2 * s, c.W, Mathf.Max(1, 2 * s));
                        break;
                    }
                    case "note":
                        T(root, HomeChecklist.PrintNote + (string.IsNullOrEmpty(sources) ? "" : "\n근거 · " + sources), Px(22), Dim, c.X, c.Y + 8 * s, c.W, c.H - 8 * s);
                        break;
                }
            }
        }

        /// <summary>A mark's print colour: green fine, amber to fix, grey otherwise.</summary>
        static Color MarkInk(HomeChecklist.Mark m) => m == HomeChecklist.Mark.Done ? Done : m == HomeChecklist.Mark.Fix ? Hot : Dim;

        /// <summary>The publishers behind a list of fact ids, once each, in order.</summary>
        public static string Publishers(Facts facts, IEnumerable<string> ids) =>
            string.Join(", ", ids.Select(id => facts.Fact(id)).Where(f => f != null).Select(f => facts.Source(f.SourceId)?.Publisher)
                .Where(p => !string.IsNullOrEmpty(p)).Distinct());
    }

    // =====================================================================================================
    // the PNG: the print art on its own canvas, rendered once by its own camera into a texture, then encoded
    // =====================================================================================================
    public static class OffscreenPng
    {
        const int UiLayer = 5;

        public static byte[] Capture(int w, int h, Color clear, Action<RectTransform> build)
        {
            var camGo = new GameObject("HomePrepPrintCamera");
            camGo.transform.position = new Vector3(0, -20000, 0); // far from every world-space canvas on the UI layer
            GameObject canvasGo = null;
            RenderTexture rt = null;
            Texture2D tex = null;
            var prev = RenderTexture.active;
            try
            {
                var cam = camGo.AddComponent<Camera>();
                cam.enabled = false;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = clear;
                cam.cullingMask = 1 << UiLayer;
                cam.orthographic = true;
                cam.nearClipPlane = 0.1f; cam.farClipPlane = 10f;
                cam.allowHDR = false; cam.allowMSAA = false;
                rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { name = "HomePrepPrint" };
                rt.Create();
                cam.targetTexture = rt;

                canvasGo = new GameObject("HomePrepPrintCanvas", typeof(RectTransform));
                var canvas = canvasGo.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = cam;
                canvas.planeDistance = 1f;
                var scaler = canvasGo.AddComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize; scaler.scaleFactor = 1;
                var root = UiKit.Rect("Art", canvasGo.transform);
                UiKit.At(root, 0, 0, w, h);
                build(root);
                foreach (var t in canvasGo.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = UiLayer;

                Canvas.ForceUpdateCanvases();
                AiOn.Rendering.ScreenFxFeature.Excluded = cam;
                cam.Render();
                RenderTexture.active = rt;
                tex = new Texture2D(w, h, TextureFormat.RGB24, false, false); // no alpha: UI blending must not make the paper see-through
                tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                tex.Apply(false);
                return tex.EncodeToPNG();
            }
            finally
            {
                AiOn.Rendering.ScreenFxFeature.Excluded = null;
                RenderTexture.active = prev;
                if (tex) UnityEngine.Object.Destroy(tex);
                if (rt) { rt.Release(); UnityEngine.Object.Destroy(rt); }
                if (canvasGo) UnityEngine.Object.Destroy(canvasGo);
                UnityEngine.Object.Destroy(camGo);
            }
        }
    }

    /// <summary>
    /// Writes a PNG to the player's Pictures folder ("경보 이전"), never anywhere online. A Pictures folder a cloud drive
    /// syncs, or one the write is refused in (Controlled Folder Access, a locked file), falls back to the game's own data
    /// folder (persistentDataPath/cards: LocalLow on Windows, not synced).
    /// </summary>
    public static class HomePrepFiles
    {
        /// <summary>Another folder for checks (null: the Pictures folder).</summary>
        public static string FolderOverride;
        public static string LastFolder { get; private set; }

        static readonly string[] SyncVars = { "OneDrive", "OneDriveConsumer", "OneDriveCommercial" };

        static List<string> SyncRoots()
        {
            var list = new List<string>();
            foreach (var v in SyncVars)
            {
                try { var r = Environment.GetEnvironmentVariable(v); if (!string.IsNullOrWhiteSpace(r)) list.Add(r); } catch (Exception) { }
            }
            return list;
        }

        public readonly struct Saved
        {
            public readonly string Path, Line;
            public readonly bool Pictures;
            public Saved(string path, string line, bool pictures) { Path = path; Line = line; Pictures = pictures; }
        }

        public static Saved SavePng(byte[] png, string stem)
        {
            string pictures = null;
            try { pictures = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures); } catch (Exception) { }
            var (dir, inPictures, synced) = string.IsNullOrEmpty(FolderOverride)
                ? FamilyCard.SaveFolder(pictures, Application.persistentDataPath, SyncRoots())
                : (FolderOverride, true, false);
            string path;
            try { path = Write(dir, png, stem); }
            catch (Exception e) when (inPictures)
            {
                Debug.LogWarning("HomePrepFiles: the Pictures folder refused the image, using the game's data folder (" + e.GetType().Name + ")");
                (dir, inPictures, _) = FamilyCard.SaveFolder(null, Application.persistentDataPath);
                path = Write(dir, png, stem);
            }
            LastFolder = dir;
            return new Saved(path, FamilyCard.SavedLine(path, inPictures, synced), inPictures);
        }

        /// <summary>Into dir under a free name: written to a temporary name first, then moved, so no half PNG is left.</summary>
        static string Write(string dir, byte[] png, string stem)
        {
            Directory.CreateDirectory(dir);
            string path = FamilyCard.UniquePath(dir, FamilyCard.FileName(stem, DateTime.Now), File.Exists);
            string part = path + ".part";
            try
            {
                File.WriteAllBytes(part, png);
                File.Move(part, path);
            }
            catch (Exception)
            {
                try { if (File.Exists(part)) File.Delete(part); } catch (Exception) { }
                throw;
            }
            return path;
        }

        /// <summary>Shows the folder in the system's file browser (a local folder, not a web page).</summary>
        public static void OpenFolder()
        {
            if (string.IsNullOrEmpty(LastFolder) || !Directory.Exists(LastFolder)) return;
            try
            {
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
                // explorer takes the path as is (a file:// URL with a Korean account name, percent-escaped, may not open)
                // and only with backslashes (persistentDataPath uses '/'; explorer opens 문서 for a path it cannot read)
                string dir = Path.GetFullPath(LastFolder).Replace('/', '\\');
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", "\"" + dir + "\"") { UseShellExecute = false });
#else
                Application.OpenURL("file://" + LastFolder.Replace('\\', '/'));
#endif
            }
            catch (Exception e) { Debug.LogException(e); }
        }
    }

    /// <summary>Speech for the low-vision profile (HostSettings speaks only under it).</summary>
    sealed class HomePrepVoice
    {
        AiOn.Settings.HostSettings _host;
        bool _looked;
        public void Say(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            if (!_looked) { _looked = true; _host = UnityEngine.Object.FindFirstObjectByType<AiOn.Settings.HostSettings>(); }
            if (_host) _host.Speak(text);
        }
    }

    // =====================================================================================================
    // hub: homeprep -> null
    // =====================================================================================================
    public class HomePrepScreen : UiScreen
    {
        public const float W = 1500, H = 760;
        public const string Heading = "우리 집 안전 준비";

        public static void Register(UnityStoryHost host) => host.Screens[FamilyCard.Screen] = Open;

        public static async Task<JToken> Open(JObject _)
        {
            await Show<HomePrepScreen>(null, W, H);
            return null;
        }

        static readonly (string id, string icon, string title, string line)[] Tiles =
        {
            ("card", "card", "우리 가족 비상연락카드", "가족 연락처와 만남의 장소를 적어 A6 카드 이미지로 내려받습니다."),
            ("checklist", "shelter", HomeChecklist.Title, "경량칸막이, 하향식 피난구, 소화기 압력계, 가까운 대피소를 우리 집에서 직접 확인합니다."),
            ("training", "heart", "안전체험관 안내", "심폐소생술과 소화기를 실제로 배울 수 있는 곳을 기관 누리집으로 안내합니다."),
        };

        bool _child;
        int _cursor;
        readonly List<(Image img, Color c)> _tiles = new();
        readonly HomePrepVoice _voice = new();

        protected override object EscapeResult() => _child ? null : "close";

        protected override void Build()
        {
            var body = Terminal.Frame(Window, Heading, "게임 밖 · 실제 우리 집", Terminal.Emerald);
            float bw = W - Terminal.Inset * 2, bh = H - Terminal.Inset * 2 - Terminal.HeaderH - 2;
            const float pad = 36, gap = 20;
            var intro = UiKit.Label("Intro", body, "게임 속 준비를 실제 우리 집에서 해 봅니다. 모두 이 컴퓨터 안에서만 쓰고, 인터넷에 접속하지 않습니다.", 21, Terminal.Ink, TextAnchor.MiddleLeft);
            UiKit.At(intro.rectTransform, pad, 22, bw - pad * 2, 56);
            float tw = (bw - pad * 2 - gap * 2) / 3, th = 360, ty = 96;
            for (int i = 0; i < Tiles.Length; i++)
            {
                int k = i;
                var rt = Tile(body, Tiles[i], tw, th, () => OpenChild(k));
                UiKit.At(rt, pad + i * (tw + gap), ty, tw, th);
                UiAppear.On(rt.gameObject, 0.06f + i * 0.05f);
            }
            var promise = UiKit.Label("Promise", body, "적거나 체크한 내용은 게임에 저장되지 않습니다. '이미지로 저장'을 누를 때만 이 컴퓨터에 이미지 파일이 생깁니다.", 17, Terminal.Muted, TextAnchor.MiddleLeft);
            UiKit.At(promise.rectTransform, pad, ty + th + 24, bw - pad * 2 - 280, 50);
            var keys = UiKit.Label("Keys", body, "1~3 또는 ←→ + Enter · 클릭 · 패드 A 고르기   Esc·B 닫기", 16, Terminal.Muted, TextAnchor.MiddleLeft);
            UiKit.At(keys.rectTransform, pad, bh - 56, 700, 30);
            var close = Terminal.Button(body, "닫기  ·  Esc", Terminal.Emerald, false);
            UiKit.At((RectTransform)close.transform, bw - pad - 240, bh - 70, 240, 50);
            close.onClick.AddListener(() => { if (!_child) Finish("close"); });
            SetCursor(0);
            _voice.Say(Heading + ". " + string.Join(". ", Tiles.Select((t, i) => $"{i + 1}번, {t.title}. {t.line}")) + " 숫자로 고릅니다.");
        }

        RectTransform Tile(Transform parent, (string id, string icon, string title, string line) t, float w, float h, Action open)
        {
            var img = UiKit.Box(t.id, parent, Terminal.Plate, raycast: true);
            HudTheme.Round(img);
            var rt = img.rectTransform;
            UiKit.At(UiKit.Box("Tone", rt, Terminal.Emerald).rectTransform, 14, 0, w - 28, 3);
            var badge = UiKit.Box("Badge", rt, Terminal.Tint(Terminal.Emerald, 0.16f)); badge.sprite = HudDraw.DiscSprite;
            UiKit.At(badge.rectTransform, 28, 30, 76, 76);
            var ic = Terminal.Icon(badge.transform, t.icon, Terminal.Emerald, 40);
            var irt = ic.rectTransform; irt.anchorMin = irt.anchorMax = irt.pivot = new Vector2(0.5f, 0.5f); irt.anchoredPosition = Vector2.zero;
            UiKit.At(UiKit.Label("No", rt, (_tiles.Count + 1).ToString(), 18, Terminal.Muted, TextAnchor.MiddleRight, FontStyle.Bold).rectTransform, w - 70, 30, 44, 28);
            UiKit.At(UiKit.Label("Title", rt, t.title, 27, Terminal.Ink, TextAnchor.UpperLeft, FontStyle.Bold).rectTransform, 28, 128, w - 56, 76);
            var line = UiKit.Label("Line", rt, t.line, 19, Terminal.Muted);
            line.lineSpacing = 1.1f;
            UiKit.At(line.rectTransform, 28, 208, w - 56, 90);
            UiKit.At(UiKit.Label("Open", rt, "열기 ▶", 19, Terminal.Emerald, TextAnchor.MiddleLeft, FontStyle.Bold).rectTransform, 28, h - 52, 200, 30);
            var b = img.gameObject.AddComponent<Button>();
            b.targetGraphic = img;
            b.navigation = new Navigation { mode = Navigation.Mode.None };
            b.onClick.AddListener(() => { UiSound.Play("click"); open(); });
            var trig = img.gameObject.AddComponent<EventTrigger>();
            var enter = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
            int idx = _tiles.Count;
            enter.callback.AddListener(_ => { UiSound.Play("tick"); SetCursor(idx); });
            trig.triggers.Add(enter);
            _tiles.Add((img, img.color));
            return rt;
        }

        void SetCursor(int i)
        {
            if (_tiles.Count == 0) return;
            i = Mathf.Clamp(i, 0, _tiles.Count - 1);
            for (int k = 0; k < _tiles.Count; k++) _tiles[k].img.color = k == i ? Terminal.PlateHi : _tiles[k].c;
            _cursor = i;
        }

        async void OpenChild(int i)
        {
            if (_child) return;
            _child = true;
            try
            {
                switch (Tiles[i].id)
                {
                    case "card": await FamilyCardScreen.Open(null); break;
                    case "checklist": await ChecklistScreen.Open(null); break;
                    default: await TrainingScreen.Open(null); break;
                }
            }
            catch (Exception e) { Debug.LogException(e); }
            finally { _child = false; }
        }

        protected override void Update()
        {
            base.Update();
            if (_child || !IsTop || Time.unscaledTime - OpenedAt < 0.25f) return;
            var es = EventSystem.current;
            if (es && es.currentSelectedGameObject) es.SetSelectedGameObject(null);
            var kb = Keyboard.current;
            var gp = Gamepad.current;
            int move = 0; bool press = false;
            if (kb != null)
            {
                for (int k = 0; k < Tiles.Length; k++)
                    if (kb[Key.Digit1 + k].wasPressedThisFrame || kb[Key.Numpad1 + k].wasPressedThisFrame) { SetCursor(k); OpenChild(k); return; }
                move += (kb.rightArrowKey.wasPressedThisFrame || kb.downArrowKey.wasPressedThisFrame ? 1 : 0) - (kb.leftArrowKey.wasPressedThisFrame || kb.upArrowKey.wasPressedThisFrame ? 1 : 0);
                press |= kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame;
            }
            if (gp != null)
            {
                move += (gp.dpad.right.wasPressedThisFrame ? 1 : 0) - (gp.dpad.left.wasPressedThisFrame ? 1 : 0);
                press |= gp.buttonSouth.wasPressedThisFrame;
                if (gp.buttonEast.wasPressedThisFrame) { Finish("close"); return; }
            }
            if (move != 0) { SetCursor(_cursor + move); _voice.Say(Tiles[_cursor].title); }
            else if (press) OpenChild(_cursor);
        }
    }

    // =====================================================================================================
    // 우리 가족 비상연락카드
    // =====================================================================================================
    public class FamilyCardScreen : UiScreen
    {
        public const float W = 1760, H = 1000;

        public static async Task<JToken> Open(JObject _)
        {
            await Show<FamilyCardScreen>(null, W, H);
            return null;
        }

        // what the player typed: this screen only (never stored)
        readonly Dictionary<string, string> _values = new();
        // what is in each field as the player typed it (a uGUI Esc puts back the text from before the edit; this undoes that)
        readonly Dictionary<string, string> _typed = new();
        readonly List<(FamilyCard.Field f, InputField input, Outline ring)> _inputs = new();
        Dictionary<string, Text> _preview;
        RectTransform _body;
        Text _status, _saved;
        Button _folder;
        bool _dirty, _warned, _wasEditing, _escInField, _armSave;
        int _focus = -1, _ringed = -1, _next = -1;
        const string KeysHint = "Tab·Enter 다음 칸 · Shift+Tab 이전 칸 · Esc 닫기 · 패드 Y 저장, B 닫기";
        readonly HomePrepVoice _voice = new();
        string _date;

        float BW => W - Terminal.Inset * 2;
        float BH => H - Terminal.Inset * 2 - Terminal.HeaderH - 2;
        const float Pad = 32;

        protected override object EscapeResult() => null; // Esc is handled in Update (a field's Esc only ends its edit)

        string Clean(string id) => FamilyCard.Clean(_values.TryGetValue(id, out var v) ? v : "", FamilyCard.Get(id)?.Max ?? 0);

        IReadOnlyDictionary<string, string> Cleaned() => FamilyCard.Fields.ToDictionary(f => f.Id, f => Clean(f.Id));

        protected override void Build()
        {
            _date = FamilyCard.DateLine(DateTime.Now);
            var body = _body = Terminal.Frame(Window, "우리 가족 비상연락카드", "통신이 끊길 때를 위해 · 종이로", Terminal.Amber);

            // ---- right: the live A6 preview (the same drawing the PNG gets) ----
            var (pw, ph) = PrintLayout.A6Pixels();
            float scale = (BH - 64 - 24) / ph, viewW = pw * scale;
            float rx = BW - Pad - viewW;
            UiKit.At(UiKit.Label("PreviewHead", body, "미리 보기 · A6 (105×148mm)", 16, Terminal.Muted, TextAnchor.MiddleLeft).rectTransform, rx, 18, viewW, 28);
            var frame = UiKit.Box("PreviewFrame", body, Terminal.Tint(Color.black, 0.5f));
            UiKit.At(frame.rectTransform, rx - 4, 56, viewW + 8, ph * scale + 8);
            var art = UiKit.Rect("Preview", frame.transform);
            UiKit.At(art, 4, 4, pw, ph);
            art.localScale = new Vector3(scale, scale, 1);
            _preview = HomePrepArt.Card(art, pw, ph, Clean, FootLine(), HotNote());

            // ---- left: the promise, the form, the facts, the buttons ----
            float lw = rx - Pad * 2 - 16;
            var notice = UiKit.Box("Notice", body, Terminal.Tint(Terminal.Amber, 0.14f));
            HudTheme.Round(notice);
            UiKit.At(notice.rectTransform, Pad, 16, lw, 70);
            var nic = Terminal.Icon(notice.transform, "warn", Terminal.Amber, 26);
            UiKit.At(nic.rectTransform, 18, 22, 26, 26);
            UiKit.At(UiKit.Label("Promise", notice.transform, FamilyCard.NotSaved, 21, Terminal.Ink, TextAnchor.UpperLeft, FontStyle.Bold).rectTransform, 58, 9, lw - 72, 28);
            UiKit.At(UiKit.Label("PromiseMore", notice.transform, FamilyCard.NotSavedMore, 16, Terminal.Muted).rectTransform, 58, 40, lw - 72, 24);

            float y = 104, g = 14, rowStep = 80;
            float Col(float share, int n) => (lw - g * (n - 1)) * share;
            // row 1: 이름 · 혈액형 · 복용약
            Field("name", Pad, y, Col(0.26f, 3)); Field("blood", Pad + Col(0.26f, 3) + g, y, Col(0.2f, 3));
            Field("meds", Pad + Col(0.46f, 3) + g * 2, y, Col(0.54f, 3));
            y += rowStep;
            // rows 2-3: 가족 셋 (two per row), then 집·직장 전화 beside 가족 3
            float nameW = Col(0.29f, 4), phoneW = Col(0.21f, 4), quarter = Col(0.25f, 4);
            Field("fam1", Pad, y, nameW); Field("fam1Phone", Pad + nameW + g, y, phoneW);
            Field("fam2", Pad + nameW + phoneW + g * 2, y, nameW); Field("fam2Phone", Pad + nameW * 2 + phoneW + g * 3, y, phoneW);
            y += rowStep;
            Field("fam3", Pad, y, nameW); Field("fam3Phone", Pad + nameW + g, y, phoneW);
            float px0 = Pad + nameW + phoneW + g * 2, placeW = (lw - (px0 - Pad) - g) / 2;
            Field("placeHome", px0, y, placeW); Field("placeWork", px0 + placeW + g, y, placeW);
            y += rowStep;
            // row 4: 학교 전화, then 만남의 장소 1·2·3차; row 5: 대피소
            Field("placeSchool", Pad, y, quarter);
            for (int i = 0; i < 3; i++) Field($"meet{i + 1}", Pad + (i + 1) * (quarter + g), y, quarter);
            y += rowStep;
            Field("shelter", Pad, y, lw);
            y += rowStep + 4;

            _status = UiKit.Label("Status", body, "", 18, Terminal.Muted, TextAnchor.MiddleLeft, FontStyle.Bold);
            UiKit.At(_status.rectTransform, Pad, y, lw, 30);
            y += 40;

            // the guidance the fields come from, word for word, with who published it
            var facts = TerminalData.Facts;
            var plate = UiKit.Box("Why", body, Terminal.Plate);
            HudTheme.Round(plate);
            float plateH = BH - 92 - 52 - y;
            UiKit.At(plate.rectTransform, Pad, y, lw, plateH);
            var col = UiKit.Rect("Col", plate.transform);
            UiKit.Fill(col, 18, 12);
            var v = col.gameObject.AddComponent<VerticalLayoutGroup>();
            v.spacing = 6; v.childControlWidth = true; v.childForceExpandWidth = true; v.childControlHeight = true; v.childForceExpandHeight = false;
            UiKit.Label("Head", col, "이 카드에 적는 것 · 국민안전24 「비상사태 행동요령」", 16, Terminal.Amber, style: FontStyle.Bold);
            foreach (var id in FamilyCard.Facts)
            {
                var f = facts.Fact(id);
                if (f == null) continue;
                UiKit.Label("Fact", col, $"<color={Terminal.Rgb(Terminal.Emerald)}>✓</color>  {f.Text}", 16, Terminal.Ink);
            }
            UiKit.Label("Official", col, FamilyCard.NotOfficial, 14, Terminal.Muted);

            // buttons
            var save = Terminal.Button(body, "이미지로 저장 (PNG)", Terminal.Amber, true);
            UiKit.At((RectTransform)save.transform, Pad, BH - 78, 300, 54);
            save.onClick.AddListener(Save);
            _folder = Terminal.Button(body, "폴더 열기", Terminal.Amber, false);
            UiKit.At((RectTransform)_folder.transform, Pad + 316, BH - 78, 170, 54);
            _folder.onClick.AddListener(HomePrepFiles.OpenFolder);
            _folder.gameObject.SetActive(false);
            var close = Terminal.Button(body, "닫기", Terminal.Amber, false);
            UiKit.At((RectTransform)close.transform, Pad + lw - 170, BH - 78, 170, 54);
            close.onClick.AddListener(TryClose);
            _saved = UiKit.Label("Saved", body, KeysHint, 15, Terminal.Muted, TextAnchor.MiddleLeft);
            UiKit.At(_saved.rectTransform, Pad, BH - 92 - 46, lw, 44);

            UpdateStatus();
            _voice.Say("우리 가족 비상연락카드. " + FamilyCard.NotSaved + " " + FamilyCard.NotSavedMore + " Tab 키로 칸을 옮깁니다.");
            Focus(0);
        }

        string FootLine() => "적은 날 " + _date;

        static string HotNote() => TerminalData.Facts.Text(FamilyCard.HotlineNoteFact);

        void Field(string id, float x, float y, float w)
        {
            var f = FamilyCard.Get(id);
            var parent = _body;
            var label = UiKit.Label("Label_" + id, parent, f.Section == "meet" ? "만남의 장소 · " + f.Label : f.Label, 16, Terminal.Muted, TextAnchor.MiddleLeft);
            UiKit.At(label.rectTransform, x, y, w, 22);
            var bg = UiKit.Box("Field_" + id, parent, Terminal.PlateHi, raycast: true);
            HudTheme.Round(bg);
            UiKit.At(bg.rectTransform, x, y + 24, w, 46);
            var ring = bg.gameObject.AddComponent<Outline>();
            ring.effectColor = Terminal.Tint(Terminal.Amber, 0f); ring.effectDistance = new Vector2(2, -2);
            var text = UiKit.Label("Text", bg.transform, "", 20, Terminal.Ink, TextAnchor.MiddleLeft);
            text.supportRichText = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            UiKit.Fill(text.rectTransform, 12, 4);
            var hint = UiKit.Label("Hint", bg.transform, f.Hint, 17, Terminal.Tint(Terminal.Muted, 0.7f), TextAnchor.MiddleLeft);
            hint.supportRichText = false;
            hint.horizontalOverflow = HorizontalWrapMode.Overflow;
            UiKit.Fill(hint.rectTransform, 12, 4);
            var input = bg.gameObject.AddComponent<InputField>();
            input.textComponent = text;
            input.placeholder = hint;
            input.targetGraphic = bg;
            input.lineType = InputField.LineType.SingleLine;
            input.characterLimit = f.Max;
            input.caretColor = Terminal.Ink;
            input.caretWidth = 2;
            input.selectionColor = new Color(0.96f, 0.62f, 0.04f, 0.35f);
            input.navigation = new Navigation { mode = Navigation.Mode.None };
            bool phone = f.Kind == FamilyCard.Kind.Phone;
            input.onValidateInput = (s, i, c) => char.IsControl(c) || phone && !FamilyCard.PhoneChar(c) ? '\0' : c;
            int index = _inputs.Count;
            input.onValueChanged.AddListener(val =>
            {
                if (input.isFocused) _typed[id] = val; // not the text uGUI puts back on Esc (the field is no longer focused then)
                Changed(id, val);
            });
            input.onEndEdit.AddListener(_ => EndEdit(index));
            _inputs.Add((f, input, ring));
        }

        void Changed(string id, string val)
        {
            if (_values.TryGetValue(id, out var was) && was == val) return;
            _values[id] = val;
            _dirty = true; _warned = false; _armSave = false;
            if (_preview != null && _preview.TryGetValue(id, out var t) && t) t.text = Clean(id);
            int f = FocusedIndex();
            UpdateStatus(f >= 0 ? _inputs[f].f.Id : null);
        }

        void EndEdit(int index)
        {
            var (f, input, _) = _inputs[index];
            if (input && input.wasCanceled)
            {
                // Esc: keep what was typed (uGUI just put back the text from before the edit), then ask to close
                if (_typed.TryGetValue(f.Id, out var typed) && input.text != typed) input.text = typed;
                _escInField = true;
            }
            var kb = Keyboard.current;
            if (kb != null && (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame))
                _next = index + 1 < _inputs.Count ? index + 1 : -2; // after the last field: ask, do not save yet
            UpdateStatus();
        }

        /// <summary>skip: the field being typed in (its half-typed number is not shown as an error yet).</summary>
        void UpdateStatus(string skip = null)
        {
            if (!_status) return;
            var v = Cleaned();
            _status.text = FamilyCard.Status(v, skip);
            bool bad = FamilyCard.Problems(v, skip).Count > 0;
            _status.color = bad ? Terminal.Red : FamilyCard.Missing(v).Count == 0 && FamilyCard.PhonesWithoutName(v).Count == 0 ? Terminal.Emerald : Terminal.Amber;
        }

        int FocusedIndex()
        {
            for (int i = 0; i < _inputs.Count; i++)
                if (_inputs[i].input && _inputs[i].input.isFocused) return i;
            return -1;
        }

        void Focus(int i)
        {
            if (i < 0 || i >= _inputs.Count) return;
            var input = _inputs[i].input;
            EventSystem.current?.SetSelectedGameObject(input.gameObject);
            input.Select();
            input.ActivateInputField();
        }

        void Save()
        {
            var v = Cleaned();
            if (!FamilyCard.CanSave(v))
            {
                UpdateStatus();
                _voice.Say("저장하지 못했습니다. " + _status.text);
                return;
            }
            try
            {
                var (w, h) = PrintLayout.A6Pixels();
                string foot = FootLine();
                string hot = HotNote();
                var png = OffscreenPng.Capture(w, h, HomePrepArt.Paper, root => HomePrepArt.Card(root, w, h, id => v.TryGetValue(id, out var s) ? s : "", foot, hot));
                var saved = HomePrepFiles.SavePng(png, FamilyCard.CardStem);
                _saved.text = saved.Line + (saved.Line.Contains("\n") ? "" : "\n인쇄해 지갑이나 생존배낭에 넣고, 가족에게도 나눠 주세요.");
                _saved.color = Terminal.Emerald;
                _folder.gameObject.SetActive(true);
                _dirty = false; _warned = false; _armSave = false;
                _voice.Say(FamilyCard.SavedSpoken("카드", saved.Pictures));
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                _saved.text = "저장하지 못했습니다: " + e.GetType().Name;
                _saved.color = Terminal.Red;
            }
        }

        void TryClose()
        {
            if (_dirty && !_warned)
            {
                _warned = true;
                _saved.text = "닫으면 적은 내용이 지워집니다(저장되지 않음). 한 번 더 누르면 닫습니다.";
                _saved.color = Terminal.Amber;
                _voice.Say(_saved.text);
                return;
            }
            Finish("close");
        }

        /// <summary>Enter after the last field: says what the next Enter does instead of saving straight away.</summary>
        void ArmSave()
        {
            _armSave = true;
            _saved.text = "마지막 칸입니다. Enter를 한 번 더 누르거나 '이미지로 저장'을 누르면 저장합니다.";
            _saved.color = Terminal.Amber;
            _voice.Say(_saved.text);
        }

        protected override void Update()
        {
            // no base.Update: Esc inside a field ends that edit (keeping the text) and then asks to close like Esc outside
            // (one warning when something is written)
            int now = FocusedIndex();
            bool editing = now >= 0;
            if (Time.unscaledTime - OpenedAt < 0.2f) { _wasEditing = editing; _escInField = false; return; }
            var kb = Keyboard.current;
            if (_escInField)
            {
                _escInField = false;
                TakeEscape(); // this Esc is spent here (no screen under this one sees it)
                if (IsTop) { TryClose(); _wasEditing = false; return; }
            }
            else if (kb != null && kb.escapeKey.wasPressedThisFrame && !_wasEditing && !editing && TakeEscape()) { TryClose(); return; }

            if (IsTop)
            {
                var gp = Gamepad.current;
                if (gp != null && gp.buttonEast.wasPressedThisFrame)
                {
                    if (editing) _inputs[now].input.DeactivateInputField(); // not a cancel: the text stays
                    TryClose();
                    return;
                }
                if (gp != null && gp.buttonNorth.wasPressedThisFrame) { Save(); return; }
                if (kb != null && _armSave && !editing && !_wasEditing && (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame))
                {
                    _armSave = false;
                    Save();
                }
                if (kb != null && kb.tabKey.wasPressedThisFrame)
                {
                    bool back = kb.shiftKey.isPressed;
                    int cur = now >= 0 ? now : _focus;
                    _next = Mathf.Clamp(cur + (back ? -1 : 1), 0, _inputs.Count - 1);
                }
            }
            if (_next == -2) { _next = -1; ArmSave(); }
            else if (_next >= 0) { int n = _next; _next = -1; _armSave = false; Focus(n); now = FocusedIndex(); editing = now >= 0; }

            // focus ring (repainted only when it moves) and the spoken label of the field just entered
            if (now != _focus && now >= 0)
            {
                var f = _inputs[now].f;
                _voice.Say(f.Label + (string.IsNullOrEmpty(f.Hint) ? "" : ". " + f.Hint));
            }
            if (now >= 0) _focus = now;
            if (now != _ringed)
            {
                if (_ringed >= 0 && _ringed < _inputs.Count && _inputs[_ringed].ring) _inputs[_ringed].ring.effectColor = Terminal.Tint(Terminal.Amber, 0f);
                if (now >= 0 && _inputs[now].ring) _inputs[now].ring.effectColor = Terminal.Tint(Terminal.Amber, 0.9f);
                _ringed = now;
            }
            _wasEditing = editing;
        }
    }

    // =====================================================================================================
    // 우리 집 안전 점검표
    // =====================================================================================================
    public class ChecklistScreen : UiScreen
    {
        public const float W = 1760, H = 1000;

        public static async Task<JToken> Open(JObject _)
        {
            await Show<ChecklistScreen>(null, W, H);
            return null;
        }

        readonly Dictionary<string, HomeChecklist.Mark> _marks = new();
        readonly List<(Image img, Text head)> _rows = new();
        ScrollRect _scroll;
        Text _summary, _summaryRest, _saved;
        Button _folder;
        int _cursor = -1;
        readonly HomePrepVoice _voice = new();

        float BW => W - Terminal.Inset * 2;
        float BH => H - Terminal.Inset * 2 - Terminal.HeaderH - 2;
        const float Pad = 32;

        protected override object EscapeResult() => "close";

        protected override void Build()
        {
            var body = Terminal.Frame(Window, HomeChecklist.Title, HomeChecklist.Subtitle, Terminal.Emerald);
            float rightW = 420, listW = BW - Pad * 3 - rightW;
            UiKit.At(UiKit.Label("Lead", body, "하나씩 직접 보고 눌러 표시하세요. 누를 때마다 아직 → 확인함 → (고칠 것) → (우리 집엔 없음) 으로 바뀝니다. 괄호는 해당하는 항목만.", 18, Terminal.Muted, TextAnchor.MiddleLeft).rectTransform, Pad, 12, listW, 34);
            var content = DebriefUi.Scroll(body, Pad - 6, 52, listW + 12, BH - 52 - 56, out _scroll, 10);
            var facts = TerminalData.Facts;
            int n = 0;
            foreach (var item in HomeChecklist.Items)
            {
                int idx = n++;
                var card = DebriefUi.Card(content, Terminal.Plate, 22, 14, 5);
                var img = card.GetComponent<Image>();
                HudTheme.Round(img);
                img.raycastTarget = true;
                var head = UiKit.Label("Head", card, "", 22, Terminal.Ink);
                UiKit.Label("How", card, item.How, 18, Terminal.Ink).lineSpacing = 1.06f;
                if (!string.IsNullOrEmpty(item.Where)) UiKit.Label("Where", card, $"찾는 곳 · {item.Where}", 17, Terminal.Sky);
                foreach (var id in item.Facts)
                {
                    var f = facts.Fact(id);
                    if (f == null) continue;
                    string pub = facts.Source(f.SourceId)?.Publisher;
                    UiKit.Label("Fact", card, $"<color={Terminal.Rgb(Terminal.Amber)}>▸</color> {f.Text}" + (string.IsNullOrEmpty(pub) ? "" : $"  <color={Terminal.Rgb(Terminal.Muted)}>· {pub}</color>"), 15, Terminal.Muted);
                }
                var b = card.gameObject.AddComponent<Button>();
                b.targetGraphic = img;
                b.navigation = new Navigation { mode = Navigation.Mode.None };
                b.onClick.AddListener(() => { SetCursor(idx); Toggle(idx); });
                _rows.Add((img, head));
            }
            UiKit.At(UiKit.Label("Keys", body, "↑↓ 고르기 · Space/Enter 표시 · 1~9 바로 표시 · 클릭   Esc 닫기", 16, Terminal.Muted, TextAnchor.MiddleLeft).rectTransform, Pad, BH - 44, listW, 30);

            // right: the tally, the note, save
            float rx = BW - Pad - rightW;
            var plate = UiKit.Box("Side", body, Terminal.Plate); HudTheme.Round(plate);
            UiKit.At(plate.rectTransform, rx, 52, rightW, BH - 52 - 24);
            _summary = UiKit.Label("Summary", plate.transform, "", 34, Terminal.Ink, TextAnchor.MiddleLeft, FontStyle.Bold);
            UiKit.At(_summary.rectTransform, 24, 20, rightW - 48, 46);
            _summaryRest = UiKit.Label("SummaryRest", plate.transform, "", 19, Terminal.Muted, TextAnchor.MiddleLeft);
            UiKit.At(_summaryRest.rectTransform, 24, 68, rightW - 48, 28);
            var note = UiKit.Label("Note", plate.transform, HomeChecklist.Note, 17, Terminal.Muted);
            note.lineSpacing = 1.1f;
            UiKit.At(note.rectTransform, 24, 116, rightW - 48, 110);
            var save = Terminal.Button(plate.transform, "점검표 이미지로 저장", Terminal.Emerald, true);
            UiKit.At((RectTransform)save.transform, 24, 244, rightW - 48, 56);
            save.onClick.AddListener(Save);
            _folder = Terminal.Button(plate.transform, "폴더 열기", Terminal.Emerald, false);
            UiKit.At((RectTransform)_folder.transform, 24, 312, rightW - 48, 48);
            _folder.onClick.AddListener(HomePrepFiles.OpenFolder);
            _folder.gameObject.SetActive(false);
            _saved = UiKit.Label("Saved", plate.transform, "A6 한 장(105×148mm)으로 인쇄됩니다. 표시하지 않은 칸은 손으로 체크할 수 있게 비워 둡니다. '고칠 것'은 ! 로 인쇄됩니다.", 16, Terminal.Muted);
            UiKit.At(_saved.rectTransform, 24, 374, rightW - 48, 120);
            var close = Terminal.Button(plate.transform, "닫기  ·  Esc", Terminal.Emerald, false);
            UiKit.At((RectTransform)close.transform, 24, BH - 52 - 24 - 80, rightW - 48, 54);
            close.onClick.AddListener(() => Finish("close"));

            Render();
            _voice.Say(HomeChecklist.Title + ". 아홉 가지를 직접 보고 표시합니다. " + HomeChecklist.Note);
        }

        void Render()
        {
            for (int i = 0; i < _rows.Count; i++)
            {
                var item = HomeChecklist.Items[i];
                var m = HomeChecklist.MarkOf(_marks, item.Id);
                string glyph = m == HomeChecklist.Mark.Done ? "✓" : m == HomeChecklist.Mark.Fix ? "!" : m == HomeChecklist.Mark.Absent ? "–" : "○";
                Color c = m == HomeChecklist.Mark.Done ? Terminal.Emerald : m == HomeChecklist.Mark.Fix ? Terminal.Amber : m == HomeChecklist.Mark.Absent ? Terminal.Muted : Terminal.Sky;
                _rows[i].head.text = $"<color={Terminal.Rgb(Terminal.Muted)}>{i + 1}</color>   <b><color={Terminal.Rgb(c)}>{glyph} {HomeChecklist.MarkText(m)}</color></b>   <b>{item.Title}</b>";
                _rows[i].img.color = i == _cursor ? Terminal.PlateHi : Terminal.Plate;
            }
            if (_summary) _summary.text = HomeChecklist.SummaryHead(_marks);
            if (_summaryRest) _summaryRest.text = HomeChecklist.SummaryRest(_marks);
        }

        void SetCursor(int i)
        {
            _cursor = Mathf.Clamp(i, 0, _rows.Count - 1);
            Render();
            if (_scroll && _rows.Count > 1) _scroll.verticalNormalizedPosition = 1f - _cursor / (float)(_rows.Count - 1);
        }

        void Toggle(int i)
        {
            var item = HomeChecklist.Items[i];
            var m = HomeChecklist.Next(item, HomeChecklist.MarkOf(_marks, item.Id));
            _marks[item.Id] = m;
            UiSound.Play("click");
            Render();
            _voice.Say($"{item.Title}, {HomeChecklist.MarkText(m)}");
        }

        void Save()
        {
            try
            {
                var (w, h) = PrintLayout.A6Pixels();
                var marks = new Dictionary<string, HomeChecklist.Mark>(_marks);
                string date = FamilyCard.DateLine(DateTime.Now);
                string sources = HomePrepArt.Publishers(TerminalData.Facts, HomeChecklist.AllFacts());
                var png = OffscreenPng.Capture(w, h, HomePrepArt.Paper, root => HomePrepArt.Checklist(root, w, h, marks, date, sources));
                var saved = HomePrepFiles.SavePng(png, FamilyCard.ChecklistStem);
                _saved.text = saved.Line;
                _saved.color = Terminal.Emerald;
                _folder.gameObject.SetActive(true);
                _voice.Say(FamilyCard.SavedSpoken("점검표", saved.Pictures));
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                _saved.text = "저장하지 못했습니다: " + e.GetType().Name;
                _saved.color = Terminal.Red;
            }
        }

        protected override void Update()
        {
            base.Update();
            if (!IsTop || Time.unscaledTime - OpenedAt < 0.25f) return;
            var es = EventSystem.current;
            if (es && es.currentSelectedGameObject) es.SetSelectedGameObject(null);
            var kb = Keyboard.current;
            var gp = Gamepad.current;
            int move = 0; bool press = false;
            if (kb != null)
            {
                for (int k = 0; k < 9 && k < _rows.Count; k++)
                    if (kb[Key.Digit1 + k].wasPressedThisFrame || kb[Key.Numpad1 + k].wasPressedThisFrame) { SetCursor(k); Toggle(k); return; }
                move += (kb.downArrowKey.wasPressedThisFrame ? 1 : 0) - (kb.upArrowKey.wasPressedThisFrame ? 1 : 0);
                press |= kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame;
            }
            if (gp != null)
            {
                move += (gp.dpad.down.wasPressedThisFrame || gp.leftStick.down.wasPressedThisFrame ? 1 : 0) - (gp.dpad.up.wasPressedThisFrame || gp.leftStick.up.wasPressedThisFrame ? 1 : 0);
                press |= gp.buttonSouth.wasPressedThisFrame;
                if (gp.buttonEast.wasPressedThisFrame) { Finish("close"); return; }
            }
            if (move != 0)
            {
                SetCursor(_cursor < 0 ? 0 : _cursor + move);
                var item = HomeChecklist.Items[_cursor];
                _voice.Say($"{_cursor + 1}번, {item.Title}, {HomeChecklist.MarkText(HomeChecklist.MarkOf(_marks, item.Id))}. {item.How} 누르면 {HomeChecklist.CycleText(item)} 순서로 바뀝니다.");
            }
            else if (press)
            {
                if (_cursor < 0) SetCursor(0);
                else Toggle(_cursor);
            }
        }
    }

    // =====================================================================================================
    // 안전체험관 안내
    // =====================================================================================================
    public class TrainingScreen : UiScreen
    {
        public const float W = 1560, H = 800;

        public static async Task<JToken> Open(JObject _)
        {
            await Show<TrainingScreen>(null, W, H);
            return null;
        }

        protected override object EscapeResult() => "close";

        protected override void Build()
        {
            var body = Terminal.Frame(Window, "안전체험관 안내", "실제로 배우기 · 기관 누리집", Terminal.Emerald);
            float bw = W - Terminal.Inset * 2, bh = H - Terminal.Inset * 2 - Terminal.HeaderH - 2;
            const float pad = 32, gap = 18;
            UiKit.At(UiKit.Label("Lead", body, "게임 속 심폐소생술과 소화기는 연습일 뿐입니다. 실제 마네킹과 소화기로 한 번 배워 보세요.", 20, Terminal.Ink, TextAnchor.MiddleLeft).rectTransform, pad, 14, bw - pad * 2, 40);
            var facts = TerminalData.Facts;
            var spoken = new StringBuilder("안전체험관 안내. ");
            float cw = (bw - pad * 2 - gap) / 2, ch = (bh - 64 - 140 - gap) / 2;
            for (int i = 0; i < TrainingGuide.Entries.Length; i++)
            {
                var e = TrainingGuide.Entries[i];
                var plate = UiKit.Box(e.Id, body, Terminal.Plate); HudTheme.Round(plate);
                var rt = plate.rectTransform;
                UiKit.At(rt, pad + (i % 2) * (cw + gap), 64 + (i / 2) * (ch + gap), cw, ch);
                UiKit.At(UiKit.Box("Tone", rt, Terminal.Emerald).rectTransform, 14, 0, cw - 28, 3);
                var col = UiKit.Rect("Col", rt); UiKit.Fill(col, 24, 16);
                var v = col.gameObject.AddComponent<VerticalLayoutGroup>();
                v.spacing = 6; v.childControlWidth = true; v.childForceExpandWidth = true; v.childControlHeight = true; v.childForceExpandHeight = false;
                UiKit.Label("Agency", col, e.Agency, 17, Terminal.Amber, style: FontStyle.Bold);
                UiKit.Label("Title", col, e.Title, 25, Terminal.Ink, style: FontStyle.Bold);
                UiKit.Label("What", col, e.What, 18, Terminal.Ink);
                foreach (var w in e.Where) UiKit.Label("Where", col, "▸ " + w, 18, Terminal.Sky);
                foreach (var id in e.Facts)
                {
                    var f = facts.Fact(id);
                    if (f == null) continue;
                    string pub = facts.Source(f.SourceId)?.Publisher;
                    UiKit.Label("Fact", col, f.Text + (string.IsNullOrEmpty(pub) ? "" : $"  · {pub}"), 15, Terminal.Muted);
                }
                spoken.Append($"{e.Title}. {e.Agency}. {e.What} ");
                UiAppear.On(rt.gameObject, 0.06f + i * 0.05f);
            }
            var note = UiKit.Label("Note", body, TrainingGuide.Note + "\n" + TrainingGuide.CheckedLine, 16, Terminal.Muted, TextAnchor.UpperLeft);
            UiKit.At(note.rectTransform, pad, bh - 128, bw - pad * 2 - 280, 60);
            var close = Terminal.Button(body, "닫기  ·  Esc", Terminal.Emerald, false);
            UiKit.At((RectTransform)close.transform, bw - pad - 240, bh - 78, 240, 50);
            close.onClick.AddListener(() => Finish("close"));
            spoken.Append(TrainingGuide.Note);
            new HomePrepVoice().Say(spoken.ToString());
        }

        protected override void Update()
        {
            base.Update();
            var gp = Gamepad.current;
            if (gp != null && IsTop && gp.buttonEast.wasPressedThisFrame && Time.unscaledTime - OpenedAt > 0.25f) Finish("close");
        }
    }
}

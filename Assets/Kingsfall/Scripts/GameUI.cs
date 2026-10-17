using System.Collections.Generic;
using UnityEngine;
using Kingsfall.Chess;

namespace Kingsfall
{
    /// <summary>IMGUI front end: title menu, HUD, move list, promotion picker, game over.</summary>
    public class GameUI : MonoBehaviour
    {
        public static GameUI I { get; private set; }
        public bool ShowMenu = true;
        public bool PointerOverUI { get; private set; }

        /// <summary>
        /// Mouse position in screen pixels (y up), taken from IMGUI events. Input.mousePosition is
        /// unreliable on multi-monitor XWayland (it reports desktop-space coordinates), whereas
        /// IMGUI events are always window-relative — the same source the menu buttons use.
        /// </summary>
        public Vector2 MouseScreen { get; private set; } = new Vector2(-1f, -1f);
        bool clickPending;

        /// <summary>True once per left click on the 3D view (clicks used by GUI controls are dropped).</summary>
        public bool ConsumeClick()
        {
            bool c = clickPending;
            clickPending = false;
            return c;
        }

        GUIStyle title, subtitle, heading, body, small, button, buttonBig, panel, banner, symbolStyle, symbolBig, toggle, moveText, caption;
        Texture2D panelTex, buttonTex, buttonHoverTex, buttonActiveTex, lineTex, vignetteTex;
        readonly List<Rect> uiRects = new List<Rect>();
        Vector2 moveScroll;
        float menuFade = 1f;
        float scale = 1f;
        bool showHelp;

        static readonly Color GoldC = new Color(0.96f, 0.78f, 0.45f);
        static readonly Color Parchment = new Color(0.93f, 0.89f, 0.8f);

        static readonly string[] WhiteSymbols = { "", "♙", "♘", "♗", "♖", "♕", "♔" };
        static readonly string[] BlackSymbols = { "", "♟", "♞", "♝", "♜", "♛", "♚" };

        void Awake() { I = this; }

        GameController G => GameController.I;

        void BuildStyles()
        {
            if (title != null) return;
            var lib = KingsfallLibrary.I;
            panelTex = Gradient(new Color(0.05f, 0.04f, 0.05f, 0.86f), new Color(0.02f, 0.02f, 0.03f, 0.92f), new Color(0.75f, 0.55f, 0.28f, 0.7f));
            buttonTex = Gradient(new Color(0.13f, 0.1f, 0.09f, 0.92f), new Color(0.06f, 0.05f, 0.05f, 0.95f), new Color(0.65f, 0.48f, 0.25f, 0.8f));
            buttonHoverTex = Gradient(new Color(0.32f, 0.22f, 0.12f, 0.95f), new Color(0.14f, 0.09f, 0.06f, 0.97f), new Color(1f, 0.8f, 0.45f, 1f));
            buttonActiveTex = Gradient(new Color(0.5f, 0.33f, 0.14f, 1f), new Color(0.25f, 0.15f, 0.07f, 1f), new Color(1f, 0.9f, 0.6f, 1f));
            lineTex = new Texture2D(1, 1); lineTex.SetPixel(0, 0, Color.white); lineTex.Apply();
            vignetteTex = RadialVignette();

            title = new GUIStyle { font = lib.titleFont, fontSize = 96, alignment = TextAnchor.MiddleCenter };
            title.normal.textColor = GoldC;
            subtitle = new GUIStyle { font = lib.headFont, fontSize = 30, alignment = TextAnchor.MiddleCenter };
            subtitle.normal.textColor = new Color(0.85f, 0.8f, 0.72f, 0.85f);
            heading = new GUIStyle { font = lib.headFont, fontSize = 30, alignment = TextAnchor.MiddleCenter };
            heading.normal.textColor = GoldC;
            body = new GUIStyle { font = lib.bodyFont, fontSize = 22, alignment = TextAnchor.MiddleLeft, wordWrap = true };
            body.normal.textColor = Parchment;
            small = new GUIStyle(body) { fontSize = 17, alignment = TextAnchor.MiddleCenter };
            small.normal.textColor = new Color(0.8f, 0.76f, 0.68f, 0.8f);
            moveText = new GUIStyle(body) { fontSize = 20, wordWrap = false };
            caption = new GUIStyle { font = lib.headFont, fontSize = 40, alignment = TextAnchor.MiddleCenter };
            caption.normal.textColor = GoldC;

            button = new GUIStyle { font = lib.headFont, fontSize = 24, alignment = TextAnchor.MiddleCenter, border = new RectOffset(6, 6, 6, 6), padding = new RectOffset(12, 12, 6, 6) };
            button.normal.background = buttonTex; button.normal.textColor = Parchment;
            button.hover.background = buttonHoverTex; button.hover.textColor = Color.white;
            button.active.background = buttonActiveTex; button.active.textColor = Color.white;
            buttonBig = new GUIStyle(button) { fontSize = 30 };
            toggle = new GUIStyle(button) { fontSize = 20 };
            toggle.onNormal.background = buttonActiveTex; toggle.onNormal.textColor = Color.white;
            toggle.onHover.background = buttonActiveTex; toggle.onHover.textColor = Color.white;
            panel = new GUIStyle { border = new RectOffset(6, 6, 6, 6), padding = new RectOffset(16, 16, 12, 12) };
            panel.normal.background = panelTex;
            banner = new GUIStyle { font = lib.headFont, fontSize = 32, alignment = TextAnchor.MiddleCenter };
            banner.normal.textColor = Parchment;
            symbolStyle = new GUIStyle { font = lib.symbolFont, fontSize = 34, alignment = TextAnchor.MiddleCenter };
            symbolStyle.normal.textColor = Parchment;
            symbolBig = new GUIStyle(symbolStyle) { fontSize = 64 };
        }

        static Texture2D Gradient(Color top, Color bottom, Color border)
        {
            const int w = 16, h = 32;
            var t = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                Color c = Color.Lerp(bottom, top, y / (float)(h - 1));
                bool edge = x == 0 || y == 0 || x == w - 1 || y == h - 1;
                bool inner = x == 2 || y == 2 || x == w - 3 || y == h - 3;
                if (edge) c = border;
                else if (inner) c = Color.Lerp(c, border, 0.25f);
                t.SetPixel(x, y, c);
            }
            t.Apply();
            return t;
        }

        static Texture2D RadialVignette()
        {
            const int s = 128;
            var t = new Texture2D(s, s, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < s; y++)
            for (int x = 0; x < s; x++)
            {
                float dx = (x / (float)(s - 1) - 0.5f) * 2f, dy = (y / (float)(s - 1) - 0.5f) * 2f;
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                t.SetPixel(x, y, new Color(0, 0, 0, Mathf.SmoothStep(0.15f, 0.85f, d * 0.75f) * 0.85f));
            }
            t.Apply();
            return t;
        }

        void Update()
        {
            if (Input.GetKeyDown(KeyCode.Escape) && Leaderboard.I && Leaderboard.I.Visible) Leaderboard.I.Visible = false;
            else if (Input.GetKeyDown(KeyCode.Escape) && G && G.Reviewing && !ShowMenu) G.EndReview();
            else if (Input.GetKeyDown(KeyCode.Escape) && G && !G.Busy)
            {
                if (G.AwaitingPromotion) G.CancelPromotion();
                else if (G.Mode != GameMode.Attract) ShowMenu = !ShowMenu;
            }
            UpdateNameTyping();
            if (Input.GetKeyDown(KeyCode.F1) && !Typing) showHelp = !showHelp;
            menuFade = Mathf.MoveTowards(menuFade, ShowMenu ? 1f : 0f, Time.unscaledDeltaTime * 3f);

            // pointer test against last frame's rects (GUI space, y down)
            Vector2 m = MouseScreen;
            m.y = Screen.height - m.y;
            m /= scale;
            PointerOverUI = false;
            foreach (var r in uiRects) if (r.Contains(m)) { PointerOverUI = true; break; }
            if (ShowMenu || Leaderboard.I && Leaderboard.I.Visible) PointerOverUI = true;
        }

        void OnGUI()
        {
            GUI.matrix = Matrix4x4.identity;
            var e = Event.current;
            bool leftDown = e.type == EventType.MouseDown && e.button == 0;
            if (e.isMouse || e.type == EventType.Repaint || e.type == EventType.ScrollWheel)
                MouseScreen = new Vector2(e.mousePosition.x, Screen.height - e.mousePosition.y);
            if (leftDown) clickPending = true;
            DrawAll();
            // a click that a button/toggle consumed must not also act on the board
            if (leftDown && e.type == EventType.Used) clickPending = false;
        }

        void DrawAll()
        {
            if (!G || G.Board == null) return;
            BuildStyles();
            scale = Screen.height / 1080f;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1));
            float W = Screen.width / scale, H = 1080f;
            if (Event.current.type == EventType.Layout) uiRects.Clear();

            if (Leaderboard.I && Leaderboard.I.Visible)
            {
                // drawn alone so clicks can't fall through to the menu or board underneath
                GUI.DrawTexture(new Rect(0, 0, W, H), vignetteTex, ScaleMode.StretchToFill);
                DrawLeaderboard(W, H);
                return;
            }
            if (menuFade > 0.01f) DrawMenu(W, H);
            if (menuFade < 0.99f && G.Mode != GameMode.Attract) DrawHud(W, H);
            if (G.Mode == GameMode.Showcase && !string.IsNullOrEmpty(G.ShowcaseCaption)) DrawCaption(W, H);
        }

        Rect Track(Rect r)
        {
            if (Event.current.type == EventType.Repaint || Event.current.type == EventType.Layout) uiRects.Add(r);
            return r;
        }

        // ------------------------------------------------------------ menu

        void DrawMenu(float W, float H)
        {
            var old = GUI.color;
            GUI.color = new Color(1, 1, 1, menuFade);
            GUI.DrawTexture(new Rect(0, 0, W, H), vignetteTex, ScaleMode.StretchToFill);

            float glow = 0.85f + 0.15f * Mathf.Sin(Time.unscaledTime * 1.3f);
            var shadow = new GUIStyle(title); shadow.normal.textColor = new Color(0, 0, 0, 0.7f * menuFade);
            GUI.Label(new Rect(0, 104, W, 180), "JOSH'S CHESS TRAINER", shadow);
            title.normal.textColor = new Color(GoldC.r * glow, GoldC.g * glow, GoldC.b * glow, menuFade);
            GUI.Label(new Rect(0, 96, W, 180), "JOSH'S CHESS TRAINER", title);
            GUI.Label(new Rect(0, 262, W, 50), "CHESS  ·  A  WAR  OF  STONE  AND  FIRE", subtitle);
            GUI.color = new Color(GoldC.r, GoldC.g, GoldC.b, 0.6f * menuFade);
            GUI.DrawTexture(new Rect(W * 0.5f - 260, 318, 520, 2), lineTex);
            GUI.color = new Color(1, 1, 1, menuFade);

            float pw = 520, ph = 692;
            var pr = Track(new Rect(W * 0.5f - pw * 0.5f, 332, pw, ph));
            GUI.Box(pr, GUIContent.none, panel);
            GUILayout.BeginArea(new Rect(pr.x + 30, pr.y + 26, pw - 60, ph - 40));
            bool inGame = G.Mode == GameMode.VsAI || G.Mode == GameMode.TwoPlayer;
            if (inGame && !G.GameOver)
            {
                if (GUILayout.Button("Resume", buttonBig, GUILayout.Height(62))) ShowMenu = false;
                GUILayout.Space(10);
            }
            if (GUILayout.Button("Play as White", buttonBig, GUILayout.Height(62))) { G.StartVsAI(Board.White); ShowMenu = false; }
            GUILayout.Space(10);
            if (GUILayout.Button("Play as Black", buttonBig, GUILayout.Height(62))) { G.StartVsAI(Board.Black); ShowMenu = false; }
            GUILayout.Space(10);
            if (GUILayout.Button("Two Players", buttonBig, GUILayout.Height(62))) { G.StartTwoPlayer(); ShowMenu = false; }
            GUILayout.Space(10);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Showcase", buttonBig, GUILayout.Height(62))) { G.StartShowcase(); ShowMenu = false; }
            GUILayout.Space(10);
            if (GUILayout.Button("Leaderboard", buttonBig, GUILayout.Height(62))) Leaderboard.I.Show();
            GUILayout.EndHorizontal();
            GUILayout.Space(18);

            GUILayout.Label("OPPONENT", heading, GUILayout.Height(34));
            GUILayout.BeginHorizontal();
            string[] names = { "Squire", "Knight", "Warlord" };
            for (int i = 0; i < 3; i++)
            {
                bool on = (int)G.Difficulty == i;
                if (GUILayout.Toggle(on, names[i], toggle, GUILayout.Height(44)) && !on) G.Difficulty = (Difficulty)i;
            }
            GUILayout.EndHorizontal();
            GUILayout.Space(10);
            GUILayout.BeginHorizontal();
            var ch = Choreographer.I;
            ch.Cinematics = GUILayout.Toggle(ch.Cinematics, ch.Cinematics ? "Cinematic battles: ON" : "Cinematic battles: OFF", toggle, GUILayout.Height(44));
            GUILayout.EndHorizontal();
            GUILayout.Space(8);
            GUILayout.BeginHorizontal();
            var pvoices = PieceVoices.I;
            if (pvoices) pvoices.Enabled = GUILayout.Toggle(pvoices.Enabled, pvoices.Enabled ? "Piece voices: ON" : "Piece voices: OFF", toggle, GUILayout.Height(44));
            G.ReviewEnabled = GUILayout.Toggle(G.ReviewEnabled, G.ReviewEnabled ? "Grandmaster: ON" : "Grandmaster: OFF", toggle, GUILayout.Height(44));
            GUILayout.EndHorizontal();
            GUILayout.Space(8);
            GUILayout.BeginHorizontal();
            GUILayout.Label("Music", body, GUILayout.Width(80), GUILayout.Height(30));
            SoundBank.I.MusicVolume = GUILayout.HorizontalSlider(SoundBank.I.MusicVolume, 0f, 1f, GUILayout.Height(30));
            GUILayout.EndHorizontal();
            GUILayout.FlexibleSpace();
            if (Application.platform != RuntimePlatform.WebGLPlayer && GUILayout.Button("Quit", button, GUILayout.Height(46))) Application.Quit();
            GUILayout.EndArea();

            GUI.Label(new Rect(0, H - 46, W, 30), "Left-click to move  ·  Right-drag or A/D to orbit  ·  Scroll to zoom  ·  Space skips a battle  ·  Esc for menu", small);
            GUI.color = old;
        }

        // ------------------------------------------------------------ HUD

        void DrawHud(float W, float H)
        {
            var b = G.Board;
            float a = 1f - menuFade;
            var old = GUI.color;
            GUI.color = new Color(1, 1, 1, a);

            // turn banner
            string side = b.Side == Board.White ? "WHITE" : "BLACK";
            string text;
            if (G.Reviewing) text = "THE GRANDMASTER'S REVIEW";
            else if (G.GameOver) text = G.GameOverTitle;
            else if (G.Mode == GameMode.Showcase) text = "BATTLE SHOWCASE";
            else if (G.Thinking) text = $"{side} IS PLOTTING…";
            else if (G.Busy) text = "BATTLE!";
            else if (G.Mode == GameMode.VsAI) text = b.Side == G.HumanColour ? "YOUR MOVE" : $"{side} TO MOVE";
            else text = $"{side} TO MOVE";
            bool inCheck = !G.GameOver && !G.Busy && b.InCheck();
            var br = Track(new Rect(W * 0.5f - 260, 22, 520, 64));
            GUI.Box(br, GUIContent.none, panel);
            banner.normal.textColor = inCheck ? new Color(1f, 0.35f + 0.2f * Mathf.Sin(Time.unscaledTime * 6f), 0.25f) : Parchment;
            GUI.Label(br, inCheck ? text + "  —  CHECK!" : text, banner);
            // side swatch
            if (G.Reviewing) GUI.color = new Color(0, 0, 0, 0);
            else GUI.color = new Color(b.Side == Board.White ? 0.95f : 0.12f, b.Side == Board.White ? 0.92f : 0.08f, b.Side == Board.White ? 0.86f : 0.07f, a);
            GUI.DrawTexture(new Rect(br.x + 22, br.y + 22, 20, 20), lineTex);
            GUI.color = new Color(1, 1, 1, a);

            if (G.Reviewing && G.LastReview != null) DrawReport(G.LastReview);
            else if (G.Mode != GameMode.Showcase)
            {
                // captured pieces (left)
                var cr = Track(new Rect(24, 120, 300, 230));
                GUI.Box(cr, GUIContent.none, panel);
                GUI.Label(new Rect(cr.x, cr.y + 10, cr.width, 34), "FALLEN", heading);
                DrawCaptured(new Rect(cr.x + 16, cr.y + 56, cr.width - 32, 80), G.Captured[Board.Black], false, "White has slain");
                DrawCaptured(new Rect(cr.x + 16, cr.y + 140, cr.width - 32, 80), G.Captured[Board.White], true, "Black has slain");

                // move list (right)
                var mr = Track(new Rect(W - 324, 120, 300, 560));
                GUI.Box(mr, GUIContent.none, panel);
                GUI.Label(new Rect(mr.x, mr.y + 10, mr.width, 34), "CHRONICLE", heading);
                GUILayout.BeginArea(new Rect(mr.x + 18, mr.y + 54, mr.width - 30, mr.height - 70));
                moveScroll = GUILayout.BeginScrollView(moveScroll, GUIStyle.none, GUIStyle.none);
                for (int i = 0; i < G.Sans.Count; i += 2)
                {
                    GUILayout.BeginHorizontal();
                    GUILayout.Label($"{i / 2 + 1}.", moveText, GUILayout.Width(48));
                    GUILayout.Label(G.Sans[i], moveText, GUILayout.Width(105));
                    if (i + 1 < G.Sans.Count) GUILayout.Label(G.Sans[i + 1], moveText, GUILayout.Width(105));
                    GUILayout.EndHorizontal();
                }
                GUILayout.EndScrollView();
                GUILayout.EndArea();
                if (Event.current.type == EventType.Repaint) moveScroll.y = Mathf.Max(moveScroll.y, (G.Sans.Count + 1) / 2 * 30f);
            }

            DrawPieceBubble(W, H, a);
            if (G.Reviewing)
            {
                DrawReview(W, H);
                GUI.color = old;
                return;
            }

            // bottom bar
            float bw = 170, gap = 12;
            int count = G.Mode == GameMode.Showcase ? 2 : 6;
            float total = count * bw + (count - 1) * gap;
            float x = W * 0.5f - total * 0.5f, y = H - 84;
            if (GUI.Button(Track(new Rect(x, y, bw, 54)), "Menu", button)) ShowMenu = true;
            x += bw + gap;
            if (G.Mode != GameMode.Showcase)
            {
                GUI.enabled = !G.Busy && G.Played.Count > 0;
                if (GUI.Button(Track(new Rect(x, y, bw, 54)), "Undo", button)) G.Undo();
                GUI.enabled = true;
                x += bw + gap;
                GUI.enabled = G.IsHumanTurn && !G.HintPending;
                if (GUI.Button(Track(new Rect(x, y, bw, 54)), "Hint", button)) G.RequestHint();
                GUI.enabled = true;
                x += bw + gap;
                if (GUI.Button(Track(new Rect(x, y, bw, 54)), "Flip View", button))
                    CameraRig.I.SetSide(CameraRig.I.Yaw > 90f && CameraRig.I.Yaw < 270f ? Board.White : Board.Black);
                x += bw + gap;
                var ch = Choreographer.I;
                ch.Cinematics = GUI.Toggle(Track(new Rect(x, y, bw, 54)), ch.Cinematics, "Cinematic", toggle);
                x += bw + gap;
            }
            GUI.enabled = G.Busy;
            if (GUI.Button(Track(new Rect(x, y, bw, 54)), "Skip ▶▶", button)) G.Skip();
            GUI.enabled = true;

            if (!string.IsNullOrEmpty(G.HintText) && G.Mode != GameMode.Showcase)
            {
                var hr = new Rect(W * 0.5f - 200, 96, 400, 44);
                GUI.Box(hr, GUIContent.none, panel);
                GUI.Label(hr, G.HintText, new GUIStyle(heading) { fontSize = 24 });
            }
            if (G.AwaitingPromotion) DrawPromotion(W, H);
            if (G.GameOver && !G.Busy) DrawGameOver(W, H);
            GUI.color = old;
        }

        void DrawCaptured(Rect r, List<PieceType> list, bool whitePieces, string label)
        {
            GUI.Label(new Rect(r.x, r.y, r.width, 24), label, new GUIStyle(small) { alignment = TextAnchor.MiddleLeft });
            var sorted = new List<PieceType>(list);
            sorted.Sort((p, q) => q.CompareTo(p));
            float x = r.x, y = r.y + 26;
            var st = new GUIStyle(symbolStyle) { fontSize = 30 };
            st.normal.textColor = whitePieces ? new Color(0.96f, 0.93f, 0.86f) : new Color(0.95f, 0.45f, 0.18f);
            foreach (var t in sorted)
            {
                GUI.Label(new Rect(x, y, 30, 40), whitePieces ? WhiteSymbols[(int)t] : BlackSymbols[(int)t], st);
                x += 22;
                if (x > r.xMax - 24) { x = r.x; y += 30; }
            }
            int material = 0;
            int[] values = { 0, 1, 3, 3, 5, 9, 0 };
            foreach (var t in list) material += values[(int)t];
            if (material > 0) GUI.Label(new Rect(r.xMax - 60, r.y, 60, 24), "+" + material, new GUIStyle(small) { alignment = TextAnchor.MiddleRight });
        }

        void DrawPromotion(float W, float H)
        {
            var r = Track(new Rect(W * 0.5f - 300, H * 0.5f - 140, 600, 260));
            GUI.Box(r, GUIContent.none, panel);
            GUI.Label(new Rect(r.x, r.y + 16, r.width, 44), "Raise your pawn to glory", heading);
            PieceType[] opts = { PieceType.Queen, PieceType.Rook, PieceType.Bishop, PieceType.Knight };
            bool white = G.PromotionColour == Board.White;
            for (int i = 0; i < 4; i++)
            {
                var br = new Rect(r.x + 30 + i * 137, r.y + 80, 125, 150);
                if (GUI.Button(br, GUIContent.none, button)) G.ChoosePromotion(opts[i]);
                var st = new GUIStyle(symbolBig);
                st.normal.textColor = white ? new Color(0.96f, 0.93f, 0.86f) : new Color(0.95f, 0.45f, 0.18f);
                GUI.Label(new Rect(br.x, br.y + 10, br.width, 90), (white ? WhiteSymbols : BlackSymbols)[(int)opts[i]], st);
                GUI.Label(new Rect(br.x, br.y + 100, br.width, 36), opts[i].ToString(), new GUIStyle(small) { fontSize = 20 });
            }
        }

        void DrawGameOver(float W, float H)
        {
            bool scored = G.Mode == GameMode.VsAI;
            float extra = scored ? 130f : 0f;
            var r = Track(new Rect(W * 0.5f - 400, H * 0.5f - 170 - extra * 0.5f, 800, 300 + extra));
            GUI.Box(r, GUIContent.none, panel);
            var big = new GUIStyle(title) { fontSize = 88 };
            big.normal.textColor = GoldC;
            GUI.Label(new Rect(r.x, r.y + 20, r.width, 120), G.GameOverTitle, big);
            GUI.Label(new Rect(r.x, r.y + 140, r.width, 40), G.GameOverDetail, subtitle);
            if (scored) DrawScore(new Rect(r.x + 30, r.y + 190, r.width - 60, extra));
            bool review = G.Mode == GameMode.VsAI || G.Mode == GameMode.TwoPlayer;
            float bx = review ? r.x + 40 : r.x + 140, step = review ? 240 : 280, by = r.y + 210 + extra;
            if (GUI.Button(new Rect(bx, by, 220, 60), "Rematch", buttonBig)) G.Rematch();
            if (review)
            {
                GUI.enabled = G.CanReview;
                if (GUI.Button(new Rect(bx + step, by, 220, 60), "Grandmaster", buttonBig)) G.StartReview();
                GUI.enabled = true;
            }
            if (GUI.Button(new Rect(bx + step * (review ? 2 : 1), by, 220, 60), "Main Menu", buttonBig)) { ShowMenu = true; G.StartAttract(); }
        }

        // ------------------------------------------------------------ score and leaderboard

        string nameField;
        bool submitTyped;
        GameScore typingFor;

        /// <summary>The name box has the keyboard (camera keys and shortcuts are ignored meanwhile).</summary>
        public static bool Typing { get; private set; }

        void UpdateNameTyping()
        {
            if (!Typing || G == null || G.Score == null || G.ScoreSubmitted || !G.GameOver || G.Reviewing) { Typing = false; return; }
            foreach (char c in Input.inputString)
            {
                if (c == '\b') { if (nameField.Length > 0) nameField = nameField.Substring(0, nameField.Length - 1); }
                else if (c == '\n' || c == '\r') submitTyped = true;
                else if (nameField.Length < 16 && (char.IsLetterOrDigit(c) || " '._-".IndexOf(c) >= 0)) nameField += c;
            }
        }

        void DrawScore(Rect r)
        {
            var line = new GUIStyle(small) { fontSize = 19 };
            if (G.ScorePending || G.Score == null) { GUI.Label(new Rect(r.x, r.y, r.width, 40), "Counting your score…", line); return; }
            int score = Leaderboard.Compute(G.Score);
            var head = new GUIStyle(heading) { fontSize = 34 };
            GUI.Label(new Rect(r.x, r.y, r.width, 44), score > 0 ? $"SCORE  {score}" : "NO SCORE THIS TIME", head);
            GUI.Label(new Rect(r.x, r.y + 42, r.width, 30), Leaderboard.Breakdown(G.Score), line);
            if (score <= 0) return;
            float y = r.y + 80;
            if (!G.ScoreSubmitted)
            {
                if (nameField == null) nameField = PlayerPrefs.GetString("playerName", "");
                if (typingFor != G.Score) { typingFor = G.Score; Typing = true; } // ready to type after each scored game
                GUI.Label(new Rect(r.x + 60, y, 110, 40), "Your name", new GUIStyle(body) { fontSize = 20 });
                // a home-made text box: Unity's IMGUI TextField mishandles typing in browsers
                var box = new Rect(r.x + 175, y, 260, 40);
                var field = new GUIStyle(button) { font = KingsfallLibrary.I.bodyFont, fontSize = 22, alignment = TextAnchor.MiddleLeft, padding = new RectOffset(12, 12, 4, 4) };
                if (Typing) { field.normal.background = buttonHoverTex; field.hover.background = buttonHoverTex; }
                bool caret = Typing && (Time.unscaledTime * 2f) % 2f < 1.2f;
                if (GUI.Button(box, nameField + (caret ? "|" : ""), field)) Typing = true;
                GUI.enabled = nameField.Trim().Length >= 2;
                if (GUI.Button(new Rect(r.x + 450, y, 240, 40), "Add to leaderboard", button) || submitTyped && GUI.enabled) { Typing = false; G.SubmitScore(nameField.Trim()); }
                submitTyped = false;
                GUI.enabled = true;
                if (!string.IsNullOrEmpty(G.SubmitStatus)) GUI.Label(new Rect(r.x, y + 40, r.width, 30), G.SubmitStatus, line);
            }
            else GUI.Label(new Rect(r.x, y, r.width, 40), G.SubmitStatus, new GUIStyle(heading) { fontSize = 24 });
        }

        void DrawLeaderboard(float W, float H)
        {
            var lb = Leaderboard.I;
            GUI.color = Color.white;
            var r = Track(new Rect(W * 0.5f - 460, 90, 920, 900));
            GUI.Box(r, GUIContent.none, panel);
            GUI.Box(r, GUIContent.none, panel); // doubled for a more solid backdrop over the board
            GUI.Label(new Rect(r.x, r.y + 18, r.width, 50), "LEADERBOARD", new GUIStyle(heading) { fontSize = 40 });
            GUI.Label(new Rect(r.x, r.y + 66, r.width, 28), "Best games against the computer, from players everywhere", small);
            var cell = new GUIStyle(body) { fontSize = 21, wordWrap = false };
            var dim = new GUIStyle(cell); dim.normal.textColor = new Color(0.75f, 0.7f, 0.6f);
            float[] cx = { 30, 90, 360, 480, 600, 700, 790 };
            string[] heads = { "#", "Name", "Score", "Level", "Result", "Moves", "Date" };
            float y = r.y + 110;
            for (int i = 0; i < heads.Length; i++) GUI.Label(new Rect(r.x + cx[i], y, 140, 30), heads[i], new GUIStyle(dim) { fontSize = 18 });
            y += 36;
            if (lb.Loading) GUI.Label(new Rect(r.x, y + 40, r.width, 40), "Fetching the scores…", small);
            else if (!string.IsNullOrEmpty(lb.Error)) GUI.Label(new Rect(r.x, y + 40, r.width, 40), lb.Error, small);
            else if (lb.Top.Count == 0) GUI.Label(new Rect(r.x, y + 40, r.width, 40), "No scores yet. Win a game to be the first!", small);
            else
            {
                bool marked = false;
                for (int i = 0; i < lb.Top.Count && i < 20; i++)
                {
                    var e = lb.Top[i];
                    bool mine = !marked && !string.IsNullOrEmpty(lb.Highlight) && e.name == lb.Highlight;
                    if (mine)
                    {
                        marked = true;
                        var old = GUI.color;
                        GUI.color = new Color(GoldC.r, GoldC.g, GoldC.b, 0.18f);
                        GUI.DrawTexture(new Rect(r.x + 16, y - 2, r.width - 32, 32), lineTex);
                        GUI.color = old;
                    }
                    var st = i < 3 ? new GUIStyle(cell) { normal = { textColor = GoldC } } : cell;
                    GUI.Label(new Rect(r.x + cx[0], y, 60, 30), (i + 1).ToString(), st);
                    GUI.Label(new Rect(r.x + cx[1], y, 260, 30), e.name, st);
                    GUI.Label(new Rect(r.x + cx[2], y, 110, 30), e.score.ToString(), st);
                    GUI.Label(new Rect(r.x + cx[3], y, 110, 30), e.difficulty, cell);
                    GUI.Label(new Rect(r.x + cx[4], y, 100, 30), e.result == "win" ? "Win" : "Draw", cell);
                    GUI.Label(new Rect(r.x + cx[5], y, 80, 30), e.moves.ToString(), cell);
                    GUI.Label(new Rect(r.x + cx[6], y, 130, 30), e.date, dim);
                    y += 34;
                }
            }
            GUI.Label(new Rect(r.x + 30, r.yMax - 90, r.width - 60, 30), "Score: win 100 / 300 / 800 by level (a draw scores a third) + accuracy + quick win + captures. Hints and undos cost a little.", new GUIStyle(small) { fontSize = 16 });
            if (GUI.Button(new Rect(r.x + r.width * 0.5f - 110, r.yMax - 60, 220, 46), "Close", button)) lb.Visible = false;
        }

        // ------------------------------------------------------------ piece voices and review

        void DrawPieceBubble(float W, float H, float alpha)
        {
            var pv = PieceVoices.I;
            if (!pv || pv.BubbleAlpha < 0.01f || string.IsNullOrEmpty(pv.BubbleText)) return;
            var cam = CameraRig.I.Cam;
            Vector3 sp = cam.WorldToScreenPoint(pv.BubblePosition);
            if (sp.z < 0.1f) return;
            // screen pixels (y up) -> GUI space (y down, scaled)
            Vector2 p = new Vector2(sp.x / scale, (Screen.height - sp.y) / scale);
            var style = new GUIStyle(body) { fontSize = 22, alignment = TextAnchor.MiddleCenter, wordWrap = true };
            style.normal.textColor = new Color(0.12f, 0.08f, 0.05f);
            float w = Mathf.Min(380f, style.CalcSize(new GUIContent(pv.BubbleText)).x + 40f);
            float h = style.CalcHeight(new GUIContent(pv.BubbleText), w - 28f) + 22f;
            var r = new Rect(Mathf.Clamp(p.x - w * 0.5f, 10f, W - w - 10f), Mathf.Clamp(p.y - h - 26f, 100f, H - h - 10f), w, h);
            var old = GUI.color;
            float k = alpha * Mathf.SmoothStep(0f, 1f, pv.BubbleAlpha);
            GUI.color = new Color(0.97f, 0.93f, 0.84f, 0.95f * k);
            GUI.DrawTexture(r, lineTex);
            // little tail pointing at the piece
            for (int i = 0; i < 12; i++)
                GUI.DrawTexture(new Rect(Mathf.Clamp(p.x, r.x + 20, r.xMax - 20) - (12 - i), r.yMax + i * 1.5f, (12 - i) * 2, 1.6f), lineTex);
            GUI.color = new Color(GoldC.r, GoldC.g, GoldC.b, k);
            GUI.DrawTexture(new Rect(r.x, r.y, r.width, 3), lineTex);
            GUI.color = new Color(1, 1, 1, k);
            GUI.Label(new Rect(r.x + 14, r.y + 8, r.width - 28, r.height - 16), pv.BubbleText, style);
            GUI.color = old;
        }

        void DrawReport(GameReviewResult rv)
        {
            var cr = Track(new Rect(24, 120, 300, 300));
            GUI.Box(cr, GUIContent.none, panel);
            GUI.Label(new Rect(cr.x, cr.y + 10, cr.width, 34), "REPORT", heading);
            var row = new GUIStyle(body) { fontSize = 21 };
            float y = cr.y + 58;
            void Row(string label, int n, Color c)
            {
                var old = GUI.color;
                GUI.color = c;
                GUI.DrawTexture(new Rect(cr.x + 20, y + 9, 14, 14), lineTex);
                GUI.color = old;
                GUI.Label(new Rect(cr.x + 46, y, 190, 32), label, row);
                GUI.Label(new Rect(cr.x + 220, y, 60, 32), n.ToString(), new GUIStyle(row) { alignment = TextAnchor.MiddleRight });
                y += 38;
            }
            Row("Excellent", rv.Excellent, new Color(0.45f, 0.9f, 0.5f));
            Row("Inaccuracies", rv.Inaccuracies, new Color(0.95f, 0.85f, 0.4f));
            Row("Mistakes", rv.Mistakes, new Color(1f, 0.6f, 0.25f));
            Row("Blunders", rv.Blunders, new Color(1f, 0.3f, 0.25f));
            GUI.Label(new Rect(cr.x + 20, y + 4, cr.width - 40, 60), $"{rv.Moves} moves reviewed", small);
        }

        void DrawReview(float W, float H)
        {
            var beat = G.CurrentBeat;
            float pw = Mathf.Min(1180f, W - 80f), ph = 250f;
            var r = Track(new Rect(W * 0.5f - pw * 0.5f, H - ph - 24f, pw, ph));
            GUI.Box(r, GUIContent.none, panel);

            // speaker name with a pulse while he talks
            float lvl = Speech.I ? Speech.I.Level : 0f;
            var name = new GUIStyle(heading) { fontSize = 26, alignment = TextAnchor.MiddleLeft };
            name.normal.textColor = Color.Lerp(GoldC, Color.white, lvl * 0.6f);
            GUI.Label(new Rect(r.x + 28, r.y + 12, 400, 34), GameReviewBuilder.MasterName.ToUpperInvariant(), name);
            if (beat != null)
            {
                var t = new GUIStyle(small) { alignment = TextAnchor.MiddleRight, fontSize = 19 };
                GUI.Label(new Rect(r.xMax - 528, r.y + 12, 500, 34), $"{beat.Title}   ·   {G.BeatIndex + 1} / {G.LastReview.Beats.Count}", t);
            }

            string text = beat != null ? beat.Text : G.ReviewStatus;
            var words = new GUIStyle(body) { fontSize = 25, alignment = TextAnchor.UpperLeft, wordWrap = true };
            GUI.Label(new Rect(r.x + 28, r.y + 56, r.width - 56, 120), text, words);

            // arrow legend for this beat
            float lx = r.x + 28, ly = r.yMax - 58;
            if (beat != null)
            {
                var seen = new HashSet<ArrowKind>();
                foreach (var a in beat.Arrows)
                {
                    if (!seen.Add(a.Kind)) continue;
                    string label = a.Kind switch
                    {
                        ArrowKind.Played => "Move played",
                        ArrowKind.Threat => "Their reply",
                        ArrowKind.Better => "Better move",
                        _ => "Great move",
                    };
                    var c = Grandmaster.ArrowColour(a.Kind);
                    float m = Mathf.Max(c.r, Mathf.Max(c.g, c.b));
                    var old = GUI.color;
                    GUI.color = new Color(c.r / m, c.g / m, c.b / m, 1f);
                    GUI.DrawTexture(new Rect(lx, ly + 19, 34, 8), lineTex);
                    GUI.color = old;
                    GUI.Label(new Rect(lx + 44, ly, 160, 46), label, new GUIStyle(small) { alignment = TextAnchor.MiddleLeft, fontSize = 19 });
                    lx += 200;
                }
            }

            // controls
            float bw = 150, bh = 46, gap = 10;
            float bx = r.xMax - 28 - (bw * 4 + gap * 3), by = r.yMax - 60;
            GUI.enabled = beat != null && G.BeatIndex > 0;
            if (GUI.Button(Track(new Rect(bx, by, bw, bh)), "◀ Back", button)) G.ReviewBack();
            GUI.enabled = beat != null;
            if (GUI.Button(Track(new Rect(bx + (bw + gap), by, bw, bh)), "Repeat", button)) G.ReviewReplay();
            if (GUI.Button(Track(new Rect(bx + (bw + gap) * 2, by, bw, bh)), "Next ▶", button)) G.ReviewNext();
            GUI.enabled = true;
            if (GUI.Button(Track(new Rect(bx + (bw + gap) * 3, by, bw, bh)), "Finish", button)) G.EndReview();
        }

        void DrawCaption(float W, float H)
        {
            var sh = new GUIStyle(caption); sh.normal.textColor = new Color(0, 0, 0, 0.8f);
            GUI.Label(new Rect(2, H - 178, W, 60), G.ShowcaseCaption, sh);
            GUI.Label(new Rect(0, H - 180, W, 60), G.ShowcaseCaption, caption);
        }
    }
}

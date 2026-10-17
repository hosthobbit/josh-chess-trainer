using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using Kingsfall.Chess;

namespace Kingsfall
{
    public enum GameMode { Attract, VsAI, TwoPlayer, Showcase }

    public class GameController : MonoBehaviour
    {
        public static GameController I { get; private set; }

        public GameMode Mode { get; private set; } = GameMode.Attract;
        public int HumanColour { get; private set; } = Board.White;
        public Difficulty Difficulty = Difficulty.Knight;
        public bool AutoFlipTwoPlayer = true;

        public Board Board { get; private set; }
        public readonly List<string> Sans = new List<string>();
        public readonly List<Move> Played = new List<Move>();
        public readonly List<PieceType>[] Captured = { new List<PieceType>(), new List<PieceType>() }; // by colour of captured piece
        public GameStatus Status { get; private set; }
        public bool Busy { get; private set; }
        public bool Thinking => aiTask != null;
        public bool GameOver { get; private set; }
        public string GameOverTitle { get; private set; } = "";
        public string GameOverDetail { get; private set; } = "";
        public bool AwaitingPromotion => promoChoices != null;
        public int PromotionColour => Board.Side;
        public bool Skipping { get; private set; }
        public string ShowcaseCaption { get; private set; } = "";

        // ---- grandmaster review
        public bool ReviewEnabled = true;
        public bool Reviewing { get; private set; }
        public string ReviewStatus { get; private set; } = "";
        public ReviewBeat CurrentBeat { get; private set; }
        public int BeatIndex { get; private set; }
        public GameReviewResult LastReview { get; private set; }
        public bool CanReview => GameOver && !Busy && !Reviewing && (Mode == GameMode.VsAI || Mode == GameMode.TwoPlayer) && Played.Count > 0;
        readonly ReviewAnalyser analyser = new ReviewAnalyser();
        Task<GameReviewResult> reviewTask;

        // ---- leaderboard score (games against the computer)
        public GameScore Score { get; private set; }
        public bool ScorePending => reviewTask != null && Score == null && Mode == GameMode.VsAI;
        public bool ScoreSubmitted;
        public string SubmitStatus = "";
        int hintsUsed, undosUsed;
        int requestedBeat = -1;
        bool endReview;
        float musicBeforeReview = -1f;

        BoardView view;
        Choreographer choreo;
        Task<Move> aiTask;
        float aiStarted;
        int selected = -1;
        List<Move> selectedMoves = new List<Move>();
        List<Move> promoChoices;
        Coroutine flow;
        int generation;

        // automation
        string autoshotDir;
        bool autoshot, reviewOnly;

        void Awake()
        {
            I = this;
            Application.targetFrameRate = 144;
            QualitySettings.vSyncCount = 1;
        }

        void Start()
        {
            if (Application.isMobilePlatform) MobileQuality();
            view = BoardView.I;
            choreo = Choreographer.I;
            var args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--autoshot" && i + 1 < args.Length) { autoshot = true; autoshotDir = args[i + 1]; }
                if (args[i] == "--review-only") reviewOnly = true;
            }
            if (autoshot) { StartCoroutine(AutoShot()); return; }
            StartAttract();
        }

        /// <summary>Phones and tablets: lighter rendering so the battles stay smooth and the battery lasts.</summary>
        static void MobileQuality()
        {
            Application.targetFrameRate = 30;
            var urp = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline as UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset;
            if (urp)
            {
                urp.renderScale = 0.8f;
                urp.shadowDistance = Mathf.Min(urp.shadowDistance, 20f);
                urp.msaaSampleCount = 1;
            }
            if (PostFx.I) PostFx.I.Lite = true;
        }

        // ------------------------------------------------------------ public API (UI)

        public void StartAttract()
        {
            ResetWorld();
            Mode = GameMode.Attract;
            NewBoard();
            CameraRig.I.AutoOrbit = true;
            CameraRig.I.UserControl = false;
            CameraRig.I.Distance = 13f;
        }

        public void StartVsAI(int humanColour)
        {
            ResetWorld();
            Mode = GameMode.VsAI;
            HumanColour = humanColour;
            hintsUsed = undosUsed = 0;
            NewBoard();
            CameraRig.I.AutoOrbit = false;
            CameraRig.I.UserControl = true;
            CameraRig.I.SetSide(humanColour);
        }

        public void StartTwoPlayer()
        {
            ResetWorld();
            Mode = GameMode.TwoPlayer;
            hintsUsed = undosUsed = 0;
            HumanColour = Board.White;
            NewBoard();
            CameraRig.I.AutoOrbit = false;
            CameraRig.I.UserControl = true;
            CameraRig.I.SetSide(Board.White);
        }

        public void StartShowcase()
        {
            ResetWorld();
            Mode = GameMode.Showcase;
            CameraRig.I.AutoOrbit = false;
            CameraRig.I.UserControl = true;
            CameraRig.I.SetSide(Board.White);
            flow = StartCoroutine(Showcase(false));
        }

        public void Rematch()
        {
            if (Mode == GameMode.TwoPlayer) StartTwoPlayer();
            else StartVsAI(HumanColour);
        }

        public void Undo()
        {
            if (Busy || Mode == GameMode.Attract || Mode == GameMode.Showcase) return;
            int count = Mode == GameMode.VsAI ? (Board.Side == HumanColour ? 2 : 1) : 1;
            if (Thinking) count = 1;
            count = Mathf.Min(count, Played.Count);
            if (count == 0) return;
            var keep = Played.GetRange(0, Played.Count - count);
            undosUsed++;
            ResetWorld();
            NewBoard();
            foreach (var m in keep) ApplyInstant(m);
            analyser.Enqueue(Board);
            view.Spawn(Board);
            if (keep.Count > 0) view.SetLastMove(keep[keep.Count - 1].From, keep[keep.Count - 1].To);
            if (Board.InCheck()) view.SetCheck(Board.KingSq[Board.Side]);
            CameraRig.I.UserControl = true;
            if (Mode == GameMode.TwoPlayer && AutoFlipTwoPlayer) CameraRig.I.SetSide(Board.Side);
        }

        public void ChoosePromotion(PieceType t)
        {
            if (promoChoices == null) return;
            foreach (var m in promoChoices)
            {
                if (m.Promo != t) continue;
                promoChoices = null;
                flow = StartCoroutine(PlayMove(m));
                return;
            }
        }

        public void CancelPromotion() { promoChoices = null; }

        public void Skip() { if (Busy) Skipping = true; }

        // ---- trainer hint: the engine suggests the human's best move
        Task<Move> hintTask;
        public string HintText { get; private set; } = "";
        public bool HintPending => hintTask != null;

        public void RequestHint()
        {
            if (!IsHumanTurn || hintTask != null) return;
            var snapshot = Board.Clone();
            hintsUsed++;
            HintText = "Thinking…";
            hintTask = EngineJob.Start(() => new ChessAI().FindBestMove(snapshot, Difficulty.Warlord));
        }

        void PollHint()
        {
            if (hintTask == null || !hintTask.IsCompleted) return;
            var t = hintTask;
            hintTask = null;
            if (t.IsFaulted || t.Result.IsNull || !IsHumanTurn) { HintText = ""; return; }
            var m = t.Result;
            HintText = "Try " + Board.ToSan(m);
            Select(m.From);
            view.SetMarker(m.To, MarkerKind.Selected);
        }

        public bool IsHumanTurn => !GameOver && !Busy && (Mode == GameMode.TwoPlayer || (Mode == GameMode.VsAI && Board.Side == HumanColour));

        // ------------------------------------------------------------ world management

        void ResetWorld()
        {
            generation++;
            StopAllCoroutines();
            choreo.StopAllCoroutines();
            Fx.I.StopAllCoroutines();
            Fx.I.ClearAll();
            foreach (var junk in GameObject.FindObjectsByType<ParticleSystem>())
                if (junk && junk.transform.parent == null && junk.GetComponentInParent<Ambience>() == null) Destroy(junk.gameObject);
            foreach (var n in new[] { "Bolt", "BoltLight", "LightPillar", "Orb", "Flash", "Shockwave" })
                foreach (var go in GameObject.FindObjectsByType<Transform>())
                    if (go && go.parent == null && go.name == n) Destroy(go.gameObject);
            aiTask = null;
            hintTask = null;
            EngineJob.Clear();
            HintText = "";
            Busy = false;
            Skipping = false;
            GameOver = false;
            promoChoices = null;
            selected = -1;
            selectedMoves.Clear();
            ShowcaseCaption = "";
            PostFx.I.BaseTimeScale = 1f;
            Time.timeScale = 1f;
            CameraRig.I.EndCinematic();
            PostFx.I.CinematicFocus(false);
            RestoreMusic();
            Reviewing = false;
            CurrentBeat = null;
            ReviewStatus = "";
            endReview = false;
            Grandmaster.I?.Hide();
            Speech.I?.Stop();
            PieceVoices.I?.Silence();
        }

        void NewBoard()
        {
            Board = Board.StartPosition();
            Sans.Clear();
            Played.Clear();
            Captured[0].Clear(); Captured[1].Clear();
            Status = GameStatus.Ongoing;
            GameOver = false;
            view.Spawn(Board);
            LastReview = null;
            reviewTask = null;
            Score = null;
            ScoreSubmitted = false;
            SubmitStatus = "";
            if (Mode == GameMode.VsAI || Mode == GameMode.TwoPlayer) analyser.Enqueue(Board);
        }

        void ApplyInstant(Move m)
        {
            Sans.Add(Board.ToSan(m));
            int victimSq = m.IsEnPassant ? m.To + (Board.Side == Board.White ? -8 : 8) : m.To;
            int victim = Board.Sq[victimSq];
            if (victim != 0) Captured[Board.ColourOf(victim)].Add(Board.TypeOf(victim));
            Board.MakeMove(m);
            Played.Add(m);
            Status = Board.Status();
        }

        // ------------------------------------------------------------ frame loop

        void Update()
        {
            if (Reviewing && Grandmaster.I && Grandmaster.I.Present && !reviewCloseUp) ReviewCamera(reviewSide, false);
            if (!EngineJob.Threaded)
            {
                // browsers: one engine job per frame; otherwise chip away at the review analysis while it's quiet
                if (!EngineJob.Pump() && !Busy && !Reviewing && (Time.frameCount & 1) == 0) analyser.PumpOne();
            }
            if (autoshot) return;

            if (Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.Escape) && Busy) Skip();
            if (Reviewing && GameUI.I && !GameUI.I.ShowMenu)
            {
                if (Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.RightArrow)) ReviewNext();
                if (Input.GetKeyDown(KeyCode.LeftArrow)) ReviewBack();
            }
            if (Skipping) PostFx.I.BaseTimeScale = 3.5f;

            if (Mode == GameMode.Attract || Mode == GameMode.VsAI)
            {
                bool aiTurn = Mode == GameMode.Attract || Board.Side != HumanColour;
                if (!Busy && !GameOver && aiTurn && aiTask == null && promoChoices == null)
                {
                    var snapshot = Board.Clone();
                    var diff = Mode == GameMode.Attract ? Difficulty.Squire : Difficulty;
                    aiStarted = Time.unscaledTime;
                    aiTask = EngineJob.Start(() => new ChessAI().FindBestMove(snapshot, diff));
                }
                if (aiTask != null && aiTask.IsCompleted && Time.unscaledTime - aiStarted > (Mode == GameMode.Attract ? 0.9f : 0.5f))
                {
                    var t = aiTask;
                    aiTask = null;
                    if (!t.IsFaulted && !t.Result.IsNull) flow = StartCoroutine(PlayMove(t.Result));
                    else if (t.IsFaulted) Debug.LogException(t.Exception);
                }
                if (Mode == GameMode.Attract && GameOver && !Busy) { StartAttract(); return; }
            }

            PollHint();
            UpdateScore();
            if (IsHumanTurn && promoChoices == null) HandleInput();
            else GameUI.I?.ConsumeClick(); // drop clicks made while it isn't the player's turn
        }

        void HandleInput()
        {
            var ui = GameUI.I;
            bool overUI = ui && ui.PointerOverUI;
            Vector2 mouse = ui ? ui.MouseScreen : (Vector2)Input.mousePosition;
            bool insideWindow = mouse.x >= 0 && mouse.y >= 0 && mouse.x < Screen.width && mouse.y < Screen.height;
            int hover = overUI || !insideWindow ? -1 : view.PickSquare(CameraRig.I.Cam, mouse);
            view.SetHover(hover);

            bool clicked = ui ? ui.ConsumeClick() : Input.GetMouseButtonDown(0);
            if (!clicked || overUI) return;
            if (hover < 0) { Deselect(); return; }

            if (selected >= 0)
            {
                var options = selectedMoves.FindAll(mv => mv.To == hover);
                if (options.Count == 1) { var m = options[0]; Deselect(); flow = StartCoroutine(PlayMove(m)); return; }
                if (options.Count > 1) { promoChoices = options; Deselect(); return; }
            }

            int code = Board.Sq[hover];
            if (code != 0 && Board.ColourOf(code) == Board.Side)
            {
                if (selected == hover) { Deselect(); return; }
                Select(hover);
            }
            else Deselect();
        }

        void Select(int sq)
        {
            Deselect();
            selected = sq;
            selectedMoves = Board.LegalMoves().FindAll(m => m.From == sq);
            var pv = view.Pieces[sq];
            if (pv) { pv.SetHover(true); pv.Glow = 0.8f; }
            view.SetMarker(sq, MarkerKind.Selected);
            foreach (var m in selectedMoves)
                view.SetMarker(m.To, m.IsCapture ? MarkerKind.Capture : MarkerKind.Move);
            SoundBank.I.Play(Sfx.Select, 0.5f);
        }

        void Deselect()
        {
            if (selected >= 0 && view.Pieces[selected])
            {
                view.Pieces[selected].SetHover(false);
                if (!Board.InCheck() || Board.KingSq[Board.Side] != selected) view.Pieces[selected].Glow = 0f;
            }
            selected = -1;
            selectedMoves.Clear();
            view.ClearMarkers();
        }

        // ------------------------------------------------------------ move playback

        IEnumerator PlayMove(Move m)
        {
            int gen = generation;
            Busy = true;
            Skipping = false;
            HintText = "";
            hintTask = null;
            view.ClearMarkers();
            view.SetHover(-1);
            view.SetCheck(-1);

            string san = Board.ToSan(m);
            var mover = view.Pieces[m.From];
            int victimSq = m.IsEnPassant ? m.To + (Board.Side == Board.White ? -8 : 8) : m.To;
            var victim = view.Pieces[victimSq];
            view.Pieces[m.From] = null;
            if (victim) view.Pieces[victimSq] = null;

            if (mover)
            {
                mover.SetHover(false);
                mover.Glow = 0f;
                if (m.IsCastle)
                {
                    int home = Board.Side == Board.White ? 0 : 56;
                    bool kingSide = Board.File(m.To) == 6;
                    int rf = kingSide ? home + 7 : home, rt = kingSide ? home + 5 : home + 3;
                    var rook = view.Pieces[rf];
                    view.Pieces[rf] = null;
                    yield return choreo.Castle(mover, rook, m.To, rt);
                    view.Pieces[rt] = rook;
                }
                else if (victim) yield return choreo.Capture(mover, victim, m.To);
                else yield return choreo.MovePiece(mover, m.To);
            }
            if (gen != generation) yield break;

            view.Pieces[m.To] = mover;
            if (victim)
            {
                Captured[victim.Colour].Add(victim.Type);
                Destroy(victim.gameObject);
            }
            Board.MakeMove(m);
            Played.Add(m);
            Sans.Add(san);
            if (Mode == GameMode.VsAI || Mode == GameMode.TwoPlayer) analyser.Enqueue(Board);

            if (m.IsPromotion && mover)
                yield return choreo.Promote(mover, m.Promo, np => view.Pieces[m.To] = np);
            if (gen != generation) yield break;

            view.SetLastMove(m.From, m.To);
            Status = Board.Status();
            bool check = Board.InCheck();

            if (Status == GameStatus.Checkmate)
            {
                var king = view.Pieces[Board.KingSq[Board.Side]];
                if (king) yield return choreo.Checkmate(king, view.Pieces[m.To]);
                GameOver = true;
                GameOverTitle = "CHECKMATE";
                GameOverDetail = (Board.Side == Board.White ? "Black" : "White") + " conquers the realm";
            }
            else if (Status != GameStatus.Ongoing)
            {
                SoundBank.I.Play(Sfx.Gong, 0.7f, 1.3f, 0f);
                GameOver = true;
                GameOverTitle = Status == GameStatus.Stalemate ? "STALEMATE" : "DRAW";
                GameOverDetail = Status switch
                {
                    GameStatus.Stalemate => "No legal move remains — an uneasy peace",
                    GameStatus.DrawFiftyMove => "Fifty moves without blood — the armies withdraw",
                    GameStatus.DrawRepetition => "Threefold repetition — the war stalls",
                    _ => "Neither side can force a victory",
                };
            }
            else if (check)
            {
                view.SetCheck(Board.KingSq[Board.Side]);
                SoundBank.I.Play(Sfx.Check, 0.7f, 1f, 0f);
                CameraRig.I.AddTrauma(0.15f);
            }

            PostFx.I.BaseTimeScale = 1f;
            Skipping = false;
            Busy = false;
            if (Mode == GameMode.TwoPlayer && AutoFlipTwoPlayer && !GameOver) CameraRig.I.SetSide(Board.Side);
            if (GameOver && (Mode == GameMode.VsAI || Mode == GameMode.TwoPlayer) && Played.Count > 0) StartReviewBuild();
            if (GameOver && ReviewEnabled && CanReview) StartCoroutine(ReviewAfterDelay(gen));
        }

        // ------------------------------------------------------------ grandmaster review

        void StartReviewBuild()
        {
            var moves = new List<Move>(Played);
            var status = Status;
            bool vsAI = Mode == GameMode.VsAI;
            int human = HumanColour;
            reviewTask = LastReview != null ? Task.FromResult(LastReview)
                : EngineJob.Start(() => new GameReviewBuilder(moves, status, vsAI, human, analyser).Build());
        }

        void UpdateScore()
        {
            if (!ScorePending || !reviewTask.IsCompleted) return;
            if (reviewTask.IsFaulted) { Debug.LogException(reviewTask.Exception); reviewTask = null; return; }
            var r = reviewTask.Result;
            LastReview = r;
            bool mated = Status == GameStatus.Checkmate;
            int enemy = HumanColour ^ 1;
            int[] values = { 0, 1, 3, 3, 5, 9, 0 };
            int captured = 0;
            foreach (var t in Captured[enemy]) captured += values[(int)t];
            Score = new GameScore
            {
                Won = mated && Board.Side == enemy,
                Drew = !mated && Status != GameStatus.Ongoing,
                Difficulty = (int)Difficulty,
                Moves = Mathf.Max(2, r.Moves),
                Accuracy = r.Moves > 0 ? Mathf.RoundToInt(100f * r.Excellent / r.Moves) : 0,
                Captured = Mathf.Min(39, captured),
                Hints = hintsUsed,
                Undos = undosUsed,
            };
        }

        public void SubmitScore(string name)
        {
            if (Score == null || ScoreSubmitted || Leaderboard.Compute(Score) <= 0) return;
            ScoreSubmitted = true;
            SubmitStatus = "Sending…";
            PlayerPrefs.SetString("playerName", name);
            PlayerPrefs.Save();
            StartCoroutine(Leaderboard.I.Submit(name, Score, (ok, msg) =>
            {
                SubmitStatus = msg;
                if (!ok) ScoreSubmitted = false;
                else Leaderboard.I.Show();
            }));
        }

        IEnumerator ReviewAfterDelay(int gen)
        {
            yield return new WaitForSecondsRealtime(2.8f);
            if (gen == generation && CanReview) StartReview();
        }

        public void StartReview()
        {
            if (!CanReview) return;
            flow = StartCoroutine(Review());
        }

        public void ReviewNext() { if (CurrentBeat != null) requestedBeat = BeatIndex + 1; }
        public void ReviewBack() { if (CurrentBeat != null) requestedBeat = Mathf.Max(0, BeatIndex - 1); }
        public void ReviewReplay() { if (CurrentBeat != null) requestedBeat = BeatIndex; }
        public void EndReview() { if (Reviewing) endReview = true; }

        void ShowPosition(List<Move> moves, int ply)
        {
            var b = Board.StartPosition();
            for (int i = 0; i < ply && i < moves.Count; i++) b.MakeMove(moves[i]);
            view.Spawn(b);
            if (ply > 0) view.SetLastMove(moves[ply - 1].From, moves[ply - 1].To);
            else view.SetLastMove(-1, -1);
            view.SetCheck(b.InCheck() ? b.KingSq[b.Side] : -1);
        }

        int reviewSide;
        bool reviewCloseUp;

        /// <summary>Frames the board with the grandmaster on the right, keeping the same horizontal view for any window shape.</summary>
        static void ReviewCamera(int side, bool snap)
        {
            Vector3 pos = new Vector3(-1.2f, 11.2f, -10.6f), look = new Vector3(1.5f, 0.2f, 0.5f);
            if (side == Board.Black) { pos = new Vector3(-pos.x, pos.y, -pos.z); look = new Vector3(-look.x, look.y, -look.z); }
            float aspect = Mathf.Max(0.5f, Screen.width / (float)Mathf.Max(1, Screen.height));
            float vfov = 2f * Mathf.Atan(Mathf.Tan(36f * Mathf.Deg2Rad) / aspect) * Mathf.Rad2Deg;
            CameraRig.I.Cinematic(pos, look, Mathf.Clamp(vfov, 40f, 80f), 2.2f, snap);
        }

        IEnumerator Review()
        {
            int gen = generation;
            Reviewing = true;
            endReview = false;
            requestedBeat = -1;
            CurrentBeat = null;
            BeatIndex = 0;
            ReviewStatus = GameReviewBuilder.MasterName + " is studying your game…";
            PieceVoices.I?.Silence();
            if (musicBeforeReview < 0f) musicBeforeReview = SoundBank.I.MusicVolume;
            SoundBank.I.MusicVolume = musicBeforeReview * 0.4f;

            var moves = new List<Move>(Played);
            var status = Status;
            bool vsAI = Mode == GameMode.VsAI;
            int human = HumanColour;
            int side = vsAI ? human : Board.White;
            if (reviewTask == null) StartReviewBuild();
            var build = reviewTask;
            reviewSide = side;
            ReviewCamera(side, false);
            yield return Grandmaster.I.Appear(side);
            while (!build.IsCompleted) yield return null;
            if (gen != generation) yield break;
            if (build.IsFaulted)
            {
                Debug.LogException(build.Exception);
                endReview = true;
            }
            else
            {
                LastReview = build.Result;
                var beats = LastReview.Beats;
                var lines = new List<string>();
                foreach (var b in beats) lines.Add(b.Speech);
                Speech.I.Prepare(lines);
                ReviewStatus = "";
                while (BeatIndex < beats.Count && !endReview)
                {
                    var beat = beats[BeatIndex];
                    CurrentBeat = beat;
                    requestedBeat = -1;
                    ShowPosition(moves, beat.Ply);
                    Grandmaster.I.ShowArrows(beat.Arrows);
                    if (autoshot && autoshotDir != null)
                    {
                        yield return new WaitForSecondsRealtime(1.4f);
                        Shot($"review_{BeatIndex:00}");
                        if (BeatIndex == 0)
                        {
                            // QA close-up of the grandmaster himself
                            reviewCloseUp = true;
                            var head = Grandmaster.HeadPosition(side);
                            var front = Grandmaster.I.transform.forward;
                            CameraRig.I.Cinematic(head + new Vector3(side == Board.White ? -3.2f : 3.2f, -0.6f, side == Board.White ? -2.6f : 2.6f), head - Vector3.up * 1.2f, 40f, 50f, true);
                            yield return new WaitForSecondsRealtime(1.0f);
                            Shot("review_grandmaster");
                            reviewCloseUp = false;
                        }
                    }

                    bool spoken = false;
                    float started = Time.unscaledTime, spokenAt = 0f;
                    StartCoroutine(SayThen(beat.Speech, () => { spoken = true; spokenAt = Time.unscaledTime; }));
                    while (requestedBeat < 0 && !endReview)
                    {
                        if (spoken)
                        {
                            // no voice available: give time to read the subtitle instead
                            float linger = Speech.I.LastSayHadAudio ? 1.0f : Mathf.Max(0f, 2f + beat.Text.Length * 0.055f - (spokenAt - started));
                            if (Time.unscaledTime - spokenAt > linger) break;
                        }
                        yield return null;
                    }
                    if (gen != generation) yield break;
                    Debug.Log($"KINGSFALL_REVIEW_BEAT {BeatIndex} engine={Speech.I.Engine} audio={Speech.I.LastSayHadAudio} '{beat.Text}'");
                    Speech.I.Stop();
                    BeatIndex = requestedBeat >= 0 ? Mathf.Min(requestedBeat, beats.Count) : BeatIndex + 1;
                }
            }

            Speech.I.Stop();
            CurrentBeat = null;
            ReviewStatus = "";
            ShowPosition(moves, moves.Count);
            yield return Grandmaster.I.Vanish();
            if (gen != generation) yield break;
            CameraRig.I.EndCinematic();
            RestoreMusic();
            Reviewing = false;
            endReview = false;
        }

        void RestoreMusic()
        {
            if (musicBeforeReview >= 0f) SoundBank.I.MusicVolume = musicBeforeReview;
            musicBeforeReview = -1f;
        }

        IEnumerator SayThen(string text, System.Action done)
        {
            yield return Speech.I.Say(text);
            done();
        }

        // ------------------------------------------------------------ showcase / autoshot

        static readonly (PieceType attacker, int from, PieceType victim, int to, string caption)[] ShowcaseSteps =
        {
            (PieceType.Pawn, 28, PieceType.Knight, 35, "THE PAWN — a desperate headbutt"),
            (PieceType.Knight, 27, PieceType.Rook, 44, "THE KNIGHT — a crushing leap"),
            (PieceType.Bishop, 2, PieceType.Queen, 38, "THE BISHOP — divine judgement"),
            (PieceType.Rook, 24, PieceType.Bishop, 28, "THE ROOK — the battering ram"),
            (PieceType.Queen, 3, PieceType.Knight, 43, "THE QUEEN — arcane annihilation"),
            (PieceType.King, 28, PieceType.Pawn, 36, "THE KING — royal punishment"),
        };

        IEnumerator Showcase(bool shots)
        {
            int index = 0;
            foreach (var step in ShowcaseSteps)
            {
                var b = new Board();
                b.Sq[step.from] = Board.Make(step.attacker, Board.White);
                b.Sq[step.to] = Board.Make(step.victim, Board.Black);
                // dressing pieces so the board isn't empty
                int[] dress = { 8, 9, 13, 14, 15, 6, 0, 49, 50, 54, 55, 62, 63, 57 };
                foreach (int s in dress)
                {
                    if (s == step.from || s == step.to || b.Sq[s] != 0) continue;
                    var t = s < 16 ? (s >= 8 ? PieceType.Pawn : (s == 6 ? PieceType.Knight : PieceType.Rook)) : (s >= 56 ? (s == 57 ? PieceType.Knight : PieceType.Rook) : PieceType.Pawn);
                    b.Sq[s] = Board.Make(t, s < 32 ? Board.White : Board.Black);
                }
                Board = b;
                view.Spawn(b);
                ShowcaseCaption = step.caption;
                CameraRig.I.SetSide(Board.White);
                yield return new WaitForSecondsRealtime(shots ? 1.2f : 1.0f);
                if (shots) Shot($"{index:00}_a_setup");
                var att = view.Pieces[step.from];
                var vic = view.Pieces[step.to];
                view.Pieces[step.from] = null; view.Pieces[step.to] = null;
                var routine = StartCoroutine(choreo.Capture(att, vic, step.to));
                if (shots)
                {
                    float[] at = { 0.6f, 1.2f, 1.8f, 2.4f, 3.0f, 3.6f, 4.4f };
                    float t0 = Time.unscaledTime;
                    for (int i = 0; i < at.Length; i++)
                    {
                        while (Time.unscaledTime - t0 < at[i]) yield return null;
                        yield return new WaitForEndOfFrame();
                        Shot($"{index:00}_b{i}");
                    }
                }
                yield return routine;
                if (vic) Destroy(vic.gameObject);
                yield return new WaitForSecondsRealtime(1.2f);
                index++;
            }
            ShowcaseCaption = "";
            if (!shots) StartAttract();
        }

        void Shot(string name)
        {
            Directory.CreateDirectory(autoshotDir);
            ScreenCapture.CaptureScreenshot(Path.Combine(autoshotDir, name + ".png"));
        }

        /// <summary>Browser QA hook: unityInstance.SendMessage("Kingsfall", "WebTest", "review").</summary>
        public void WebTest(string what)
        {
            Debug.Log("KINGSFALL_WEBTEST " + what);
            if (what == "mute") SoundBank.I.MasterVolume = 0f;
            if (what == "review") { ResetWorld(); StartCoroutine(ReviewShots()); }
            if (what == "next") ReviewNext();
            if (what == "win")
            {
                // scholar's mate played by the human as white: a scored win for the game-over panel
                ResetWorld();
                GameUI.I.ShowMenu = false;
                Mode = GameMode.VsAI;
                HumanColour = Board.White;
                hintsUsed = undosUsed = 0;
                NewBoard();
                foreach (var u in new[] { "e2e4", "e7e5", "f1c4", "b8c6", "d1h5", "g8f6", "h5f7" })
                    ApplyInstant(Board.LegalMoves().Find(x => x.ToString() == u));
                view.Spawn(Board);
                GameOver = true;
                GameOverTitle = "CHECKMATE";
                GameOverDetail = "White conquers the realm";
                StartReviewBuild();
            }
        }

        // Scholar's mate with the human as black, then the grandmaster's review of it.
        IEnumerator ReviewShots()
        {
            ShowcaseCaption = "";
            GameUI.I.ShowMenu = false;
            Mode = GameMode.VsAI;
            HumanColour = Board.Black;
            NewBoard();
            foreach (var u in new[] { "e2e4", "e7e5", "f1c4", "b8c6", "d1h5", "g8f6", "h5f7" })
            {
                var m = Board.LegalMoves().Find(x => x.ToString() == u);
                ApplyInstant(m);
            }
            view.Spawn(Board);
            var last = Played[Played.Count - 1];
            view.SetLastMove(last.From, last.To);
            GameOver = true;
            GameOverTitle = "CHECKMATE";
            GameOverDetail = "White conquers the realm";
            CameraRig.I.SetSide(Board.Black, true);
            yield return new WaitForSecondsRealtime(1.0f);
            if (autoshotDir != null) Shot("review_gameover");
            yield return Review();
            yield return new WaitForSecondsRealtime(0.5f);
            if (autoshotDir != null) Shot("review_after");
            Debug.Log("KINGSFALL_REVIEW_DONE");
        }

        IEnumerator AutoShot()
        {
            Debug.Log("KINGSFALL_AUTOSHOT_BEGIN");
            SoundBank.I.MasterVolume = 0f; // automated runs must stay silent
            if (reviewOnly) { yield return ReviewShots(); Application.Quit(); yield break; }
            Mode = GameMode.VsAI;
            NewBoard();
            CameraRig.I.UserControl = false;
            CameraRig.I.SetSide(Board.White, true);
            yield return new WaitForSecondsRealtime(2.5f);
            Shot("start_overview");
            GameUI.I.ShowMenu = true;
            yield return new WaitForSecondsRealtime(0.6f);
            Shot("menu");
            GameUI.I.ShowMenu = false;
            // a short game opening with selection markers
            Select(12);
            yield return new WaitForSecondsRealtime(0.7f);
            Shot("select_pawn");
            Deselect();
            CameraRig.I.SetView(35f, 28f, 7.5f);
            yield return new WaitForSecondsRealtime(1.0f);
            Shot("low_angle");
            Mode = GameMode.Showcase;
            yield return Showcase(true);
            yield return ReviewShots();
            Debug.Log("KINGSFALL_AUTOSHOT_DONE");
            yield return new WaitForSecondsRealtime(0.5f);
            Application.Quit();
        }
    }
}

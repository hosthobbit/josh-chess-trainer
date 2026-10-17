using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;

namespace Kingsfall.Chess
{
    /// <summary>
    /// Background engine analysis for the post-game review. Positions are queued as the game is
    /// played so most of the work is done by the time it ends; results are cached by position hash,
    /// which keeps them valid across undo.
    /// </summary>
    public class ReviewAnalyser
    {
        public const long MsPerPosition = 450;

        readonly Dictionary<ulong, (Move best, int score)> cache = new Dictionary<ulong, (Move, int)>();
        readonly Queue<Board> queue = new Queue<Board>();
        readonly object gate = new object();
        readonly ChessAI engine = new ChessAI();
        Thread worker;

        public void Enqueue(Board b)
        {
            lock (gate)
            {
                if (cache.ContainsKey(b.Hash)) return;
                queue.Enqueue(b.Clone());
                if (worker == null)
                {
                    worker = new Thread(Run) { IsBackground = true, Name = "ReviewAnalyser", Priority = ThreadPriority.BelowNormal };
                    worker.Start();
                }
                Monitor.Pulse(gate);
            }
        }

        void Run()
        {
            while (true)
            {
                Board b;
                lock (gate)
                {
                    while (queue.Count == 0) Monitor.Wait(gate);
                    b = queue.Dequeue();
                    if (cache.ContainsKey(b.Hash)) continue;
                }
                Get(b);
            }
        }

        /// <summary>Analysis for a position, computing it now (on the calling thread) if needed.</summary>
        public (Move best, int score) Get(Board b)
        {
            lock (gate) if (cache.TryGetValue(b.Hash, out var hit)) return hit;
            (Move, int) r;
            // one search at a time: ChessAI's tables aren't thread-safe
            lock (engine)
            {
                lock (gate) if (cache.TryGetValue(b.Hash, out var hit2)) return hit2;
                r = engine.Analyse(b, MsPerPosition, 12);
            }
            lock (gate) cache[b.Hash] = r;
            return r;
        }
    }

    public enum MoveGrade { Best, Good, Inaccuracy, Mistake, Blunder }
    public enum ArrowKind { Played, Threat, Better, Great }

    public struct ReviewArrow
    {
        public int From, To;
        public ArrowKind Kind;
        public ReviewArrow(int from, int to, ArrowKind kind) { From = from; To = to; Kind = kind; }
    }

    /// <summary>One thing the grandmaster says, with the board position and arrows to show.</summary>
    public class ReviewBeat
    {
        public string Title = "";
        public int Ply;                // board shown = position after this many moves
        public readonly List<ReviewArrow> Arrows = new List<ReviewArrow>();
        public string Text = "";       // subtitle
        public string Speech = "";     // what is spoken (moves written out as words)
    }

    public class GameReviewResult
    {
        public readonly List<ReviewBeat> Beats = new List<ReviewBeat>();
        public int Moves, Excellent, Inaccuracies, Mistakes, Blunders;
        public readonly List<MoveGrade> Grades = new List<MoveGrade>(); // per ply (all plies)
    }

    /// <summary>Turns the analysed game into the grandmaster's narration.</summary>
    public class GameReviewBuilder
    {
        public const string MasterName = "Master Aldric";

        readonly List<Move> played;
        readonly bool[] reviewed = new bool[2];
        readonly bool secondPerson;          // vs AI: "you" / "the enemy"
        readonly int human;
        readonly GameStatus finalStatus;
        readonly ReviewAnalyser analyser;

        readonly List<Board> pos = new List<Board>();  // pos[i] = before move i
        readonly List<string> sans = new List<string>();
        Move[] bestMove;
        int[] score;   // side to move's view, for pos[i]

        static readonly string[] PieceNames = { "", "pawn", "knight", "bishop", "rook", "queen", "king" };
        static readonly int[] PieceValue = { 0, 1, 3, 3, 5, 9, 100 };

        enum Why { None, AllowedMate, MissedMate, HungMovedPiece, LeftAttacked, LeftUndefended, MissedCapture }

        class Moment
        {
            public int Ply;
            public MoveGrade Grade;
            public double Drop;
            public Why Why;
            public bool Good;
            public string GoodReason, GoodSpeech;
        }

        public GameReviewBuilder(List<Move> played, GameStatus finalStatus, bool vsAI, int humanColour, ReviewAnalyser analyser)
        {
            this.played = new List<Move>(played);
            this.finalStatus = finalStatus;
            this.analyser = analyser;
            secondPerson = vsAI;
            human = humanColour;
            if (vsAI) reviewed[humanColour] = true;
            else reviewed[0] = reviewed[1] = true;
        }

        // ------------------------------------------------------------ wording helpers

        string Subj(int c) => secondPerson ? (c == human ? "you" : "the enemy") : (c == Board.White ? "White" : "Black");
        string SubjCap(int c) => Cap(Subj(c));
        string Poss(int c) => secondPerson ? (c == human ? "your" : "their") : (c == Board.White ? "White's" : "Black's");
        string Opp(int c) => Subj(c ^ 1);
        string OppPoss(int c) => Poss(c ^ 1);
        static string Cap(string s) => string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);
        static string Sq(int s) => Board.SquareName(s);
        static string SpokenSq(int s) => char.ToUpperInvariant(Board.SquareName(s)[0]) + Board.SquareName(s).Substring(1);
        static string MoveNo(int ply) => (ply / 2 + 1).ToString();

        /// <summary>A move written out for speech, e.g. "knight takes bishop on F6, check".</summary>
        static string Spoken(Board b, Move m)
        {
            var t = Board.TypeOf(b.Sq[m.From]);
            string s;
            if (m.IsCastle) s = Board.File(m.To) == 6 ? "castling on the king side" : "castling on the queen side";
            else if (m.IsCapture)
            {
                var victim = m.IsEnPassant ? PieceType.Pawn : Board.TypeOf(b.Sq[m.To]);
                s = $"{PieceNames[(int)t]} takes {PieceNames[(int)victim]} on {SpokenSq(m.To)}";
            }
            else s = $"{PieceNames[(int)t]} to {SpokenSq(m.To)}";
            if (m.IsPromotion) s += $", becoming a {PieceNames[(int)m.Promo]}";
            b.MakeMove(m);
            if (b.InCheck()) s += b.HasLegalMove() ? ", check" : ", checkmate";
            b.UnmakeMove();
            return s;
        }

        static double WinChance(int cp)
        {
            if (ChessAI.IsMateScore(cp)) cp = cp > 0 ? 3000 : -3000;
            cp = Math.Max(-3000, Math.Min(3000, cp));
            return 1.0 / (1.0 + Math.Exp(-0.00368208 * cp));
        }

        static bool GivesMate(Board b, Move m)
        {
            b.MakeMove(m);
            bool mate = b.InCheck() && !b.HasLegalMove();
            b.UnmakeMove();
            return mate;
        }

        static int MovesToMate(int score) => (ChessAI.MateValue - Math.Abs(score) + 1) / 2;

        // ------------------------------------------------------------ build

        public GameReviewResult Build()
        {
            var b = Board.StartPosition();
            foreach (var m in played)
            {
                pos.Add(b.Clone());
                sans.Add(b.ToSan(m));
                b.MakeMove(m);
            }
            pos.Add(b.Clone());
            int n = played.Count;

            bestMove = new Move[n + 1];
            score = new int[n + 1];
            for (int i = 0; i <= n; i++)
            {
                if (i == n && finalStatus != GameStatus.Ongoing)
                {
                    score[i] = finalStatus == GameStatus.Checkmate ? -ChessAI.MateValue : 0;
                    continue;
                }
                var r = analyser.Get(pos[i]);
                bestMove[i] = r.best;
                score[i] = r.score;
            }

            var result = new GameReviewResult { };
            var moments = new List<Moment>();
            var grades = new MoveGrade[n];
            var why = new Why[n];
            for (int i = 0; i < n; i++)
            {
                int side = pos[i].Side;
                if (!reviewed[side]) { grades[i] = MoveGrade.Good; continue; }
                double drop = WinChance(score[i]) - WinChance(-score[i + 1]);
                MoveGrade g;
                if (played[i].Equals(bestMove[i]) || drop < 0.035) g = played[i].Equals(bestMove[i]) ? MoveGrade.Best : MoveGrade.Good;
                else if (drop < 0.09) g = MoveGrade.Inaccuracy;
                else if (drop < 0.18) g = MoveGrade.Mistake;
                else g = MoveGrade.Blunder;
                // a missed mate is always worth talking about
                if (g < MoveGrade.Mistake && ChessAI.IsMateScore(score[i]) && score[i] > 0 && !(score[i + 1] < 0 && ChessAI.IsMateScore(score[i + 1])))
                    g = MoveGrade.Mistake;
                grades[i] = g;
                why[i] = Explain(i);
                result.Moves++;
                switch (g)
                {
                    case MoveGrade.Best: case MoveGrade.Good: result.Excellent++; break;
                    case MoveGrade.Inaccuracy: result.Inaccuracies++; break;
                    case MoveGrade.Mistake: result.Mistakes++; break;
                    default: result.Blunders++; break;
                }
                if (g >= MoveGrade.Inaccuracy) moments.Add(new Moment { Ply = i, Grade = g, Drop = drop, Why = why[i] });
            }
            result.Grades.AddRange(grades);

            // pick the worst few errors, then a couple of highlights
            moments.Sort((a, c) => c.Drop.CompareTo(a.Drop));
            var chosen = new List<Moment>();
            foreach (var m in moments)
            {
                if (chosen.Count >= 3) break;
                if (m.Grade >= MoveGrade.Mistake || chosen.Count < 2) chosen.Add(m);
            }
            var turning = chosen.Count > 0 ? chosen[0] : null;
            var highlights = FindHighlights(grades);
            chosen.AddRange(highlights);
            chosen.Sort((a, c) => a.Ply.CompareTo(c.Ply));

            Intro(result, turning);
            foreach (var m in chosen)
            {
                if (m.Good) GoodBeat(result, m);
                else ErrorBeats(result, m, m == turning && LostBy(pos[m.Ply].Side));
            }
            Tips(result, why);
            return result;
        }

        bool LostBy(int colour) => finalStatus == GameStatus.Checkmate && pos[played.Count].Side == colour;

        // ------------------------------------------------------------ explanation of a bad move

        Why Explain(int i)
        {
            var before = pos[i];
            var after = pos[i + 1];
            var m = played[i];
            int side = before.Side;
            var reply = bestMove[i + 1];

            if (score[i + 1] > 0 && ChessAI.IsMateScore(score[i + 1])) return Why.AllowedMate;
            if (score[i] > 0 && ChessAI.IsMateScore(score[i]) && !bestMove[i].IsNull) return Why.MissedMate;
            if (!reply.IsNull && reply.IsCapture && !reply.IsEnPassant)
            {
                var lost = Board.TypeOf(after.Sq[reply.To]);
                if (PieceValue[(int)lost] >= 3 || lost == PieceType.Pawn && PieceValue[(int)Board.TypeOf(after.Sq[reply.From])] == 1)
                {
                    if (reply.To == m.To) return Why.HungMovedPiece;
                    if (before.Sq[reply.To] == after.Sq[reply.To] && before.IsAttacked(reply.To, side ^ 1)) return Why.LeftAttacked;
                    return Why.LeftUndefended;
                }
            }
            var best = bestMove[i];
            if (!best.IsNull && best.IsCapture && !m.IsCapture) return Why.MissedCapture;
            return Why.None;
        }

        void ErrorBeats(GameReviewResult r, Moment mo, bool turningPoint)
        {
            int i = mo.Ply;
            var before = pos[i];
            var after = pos[i + 1];
            var m = played[i];
            int side = before.Side;
            var best = bestMove[i];
            var reply = bestMove[i + 1];
            string grade = mo.Grade == MoveGrade.Blunder ? "Blunder" : mo.Grade == MoveGrade.Mistake ? "Mistake" : "Inaccuracy";
            string title = $"Move {MoveNo(i)} · {grade}";
            string movedName = PieceNames[(int)Board.TypeOf(before.Sq[m.From])];

            // 1: what was played
            var b1 = new ReviewBeat { Title = title, Ply = i };
            b1.Arrows.Add(new ReviewArrow(m.From, m.To, ArrowKind.Played));
            string opener = mo.Grade == MoveGrade.Blunder ? "Ah. Here is where it went wrong." :
                            mo.Grade == MoveGrade.Mistake ? "Now look closely at this moment." : "A small slip here.";
            string playedBy = secondPerson && side == human ? "you played" : $"{Subj(side)} played";
            b1.Text = $"{opener} On move {MoveNo(i)}, {playedBy} {sans[i]}.";
            b1.Speech = $"{opener} On move {MoveNo(i)}, {playedBy} {Spoken(before.Clone(), m)}.";
            if (turningPoint) { b1.Text += " This was the turning point of the game."; b1.Speech += " This was the turning point of the game."; }
            r.Beats.Add(b1);

            // 2: why it was bad, shown on the board after the move
            string text = null, speech = null;
            var replyBoard = after.Clone();
            switch (mo.Why)
            {
                case Why.AllowedMate:
                    int mateIn = MovesToMate(score[i + 1]);
                    if (!reply.IsNull && GivesMate(replyBoard, reply))
                    {
                        text = $"But that let {Opp(side)} play {after.ToSan(reply)} — checkmate!";
                        speech = $"But that let {Opp(side)} play {Spoken(replyBoard, reply)}!";
                    }
                    else
                    {
                        text = $"That gave {Opp(side)} a forced checkmate in {mateIn}, starting with {after.ToSan(reply)}.";
                        speech = $"That gave {Opp(side)} a forced checkmate in {mateIn}, starting with {Spoken(replyBoard, reply)}.";
                    }
                    break;
                case Why.HungMovedPiece:
                case Why.LeftAttacked:
                case Why.LeftUndefended:
                {
                    var lost = PieceNames[(int)Board.TypeOf(after.Sq[reply.To])];
                    var attacker = PieceNames[(int)Board.TypeOf(after.Sq[reply.From])];
                    Func<Func<int, string>, string> lead = sq => mo.Why == Why.HungMovedPiece
                        ? $"The {movedName} landed on {sq(m.To)}, where {OppPoss(side)} {attacker} can simply take it."
                        : mo.Why == Why.LeftAttacked
                            ? $"{Cap(Poss(side))} {lost} on {sq(reply.To)} was already under attack, and it was left there."
                            : $"That left {Poss(side)} {lost} on {sq(reply.To)} without enough protection.";
                    text = $"{lead(Sq)} {Cap(Opp(side))} can win it with {after.ToSan(reply)}.";
                    speech = $"{lead(SpokenSq)} {Cap(Opp(side))} can win it with {Spoken(replyBoard, reply)}.";
                    break;
                }
                case Why.MissedMate:
                    break; // explained by the better-move beat
                default:
                    if (!reply.IsNull)
                    {
                        text = $"It gives {Opp(side)} the chance to play {after.ToSan(reply)}, and the position gets harder.";
                        speech = $"It gives {Opp(side)} the chance to play {Spoken(replyBoard, reply)}, and the position gets harder.";
                    }
                    break;
            }
            if (text != null)
            {
                var b2 = new ReviewBeat { Title = title, Ply = i + 1, Text = text, Speech = speech };
                if (!reply.IsNull) b2.Arrows.Add(new ReviewArrow(reply.From, reply.To, ArrowKind.Threat));
                r.Beats.Add(b2);
            }

            // 3: what would have been better
            if (!best.IsNull)
            {
                var b3 = new ReviewBeat { Title = title, Ply = i };
                b3.Arrows.Add(new ReviewArrow(best.From, best.To, ArrowKind.Better));
                string bestSan = before.ToSan(best), bestSpoken = Spoken(before.Clone(), best);
                string reason;
                var bestPiece = Board.TypeOf(before.Sq[best.From]);
                if (GivesMate(before.Clone(), best)) reason = "That is checkmate on the spot!";
                else if (score[i] > 0 && ChessAI.IsMateScore(score[i])) reason = $"That starts a forced checkmate in {MovesToMate(score[i])}.";
                else if (mo.Why == Why.AllowedMate) reason = "That keeps the king safe from the attack.";
                else if (best.IsCapture && !best.IsEnPassant && PieceValue[(int)Board.TypeOf(before.Sq[best.To])] >= PieceValue[(int)bestPiece])
                    reason = $"It wins {OppPoss(side)} {PieceNames[(int)Board.TypeOf(before.Sq[best.To])]}.";
                else if (best.IsCapture) reason = "It wins material.";
                else if (mo.Why == Why.LeftAttacked && best.From == reply.To) reason = $"It moves the {PieceNames[(int)bestPiece]} out of danger.";
                else if (mo.Why == Why.HungMovedPiece || mo.Why == Why.LeftUndefended || mo.Why == Why.LeftAttacked) reason = "It keeps every piece safe.";
                else reason = "It keeps the pieces working together.";
                string who = secondPerson && side == human ? "you" : Subj(side);
                b3.Text = $"Instead, {who} could have played {bestSan}. {reason}";
                b3.Speech = $"Instead, {who} could have played {bestSpoken}. {reason}";
                r.Beats.Add(b3);
            }
        }

        // ------------------------------------------------------------ highlights

        List<Moment> FindHighlights(MoveGrade[] grades)
        {
            var list = new List<Moment>();
            for (int i = 0; i < played.Count; i++)
            {
                var before = pos[i];
                if (!reviewed[before.Side] || grades[i] > MoveGrade.Good) continue;
                var m = played[i];
                bool mate = GivesMate(before.Clone(), m);
                // the opponent's previous move threw away a lot, and this move takes advantage of it
                bool punish = i > 0 && !reviewed[pos[i - 1].Side] && WinChance(score[i - 1]) - WinChance(-score[i]) > 0.18;
                bool bigCapture = m.IsCapture && !m.IsEnPassant && PieceValue[(int)Board.TypeOf(before.Sq[m.To])] >= 3;
                var mo = new Moment { Ply = i, Good = true };
                string moved = PieceNames[(int)Board.TypeOf(before.Sq[m.From])];
                if (mate) { mo.GoodReason = "Checkmate. Beautifully done!"; mo.Drop = 3; }
                else if (punish && m.Equals(bestMove[i])) { mo.GoodReason = $"{SubjCap(before.Side)} spotted the mistake and punished it right away. That is how masters think!"; mo.Drop = 2; }
                else if (bigCapture && m.Equals(bestMove[i])) { mo.GoodReason = $"A clean capture with the {moved}. Winning material like this is how battles are won."; mo.Drop = 1; }
                else continue;
                list.Add(mo);
            }
            list.Sort((a, c) => c.Drop.CompareTo(a.Drop));
            if (list.Count > 2) list.RemoveRange(2, list.Count - 2);
            return list;
        }

        void GoodBeat(GameReviewResult r, Moment mo)
        {
            int i = mo.Ply;
            var b = new ReviewBeat { Title = $"Move {MoveNo(i)} · Great move", Ply = i };
            b.Arrows.Add(new ReviewArrow(played[i].From, played[i].To, ArrowKind.Great));
            string who = secondPerson ? "you" : Subj(pos[i].Side);
            b.Text = $"Now this I liked! On move {MoveNo(i)}, {who} played {sans[i]}. {mo.GoodReason}";
            b.Speech = $"Now this I liked! On move {MoveNo(i)}, {who} played {Spoken(pos[i].Clone(), played[i])}. {mo.GoodReason}";
            r.Beats.Add(b);
        }

        // ------------------------------------------------------------ intro and tips

        void Intro(GameReviewResult r, Moment turning)
        {
            var b = new ReviewBeat { Title = "The Grandmaster's Review", Ply = played.Count };
            string open;
            if (secondPerson)
            {
                bool won = finalStatus == GameStatus.Checkmate && pos[played.Count].Side != human;
                bool lost = LostBy(human);
                if (won) open = "Well, well! Victory is yours, my young friend. But even a winner can learn something.";
                else if (lost) open = "A hard-fought battle, my young friend, but not lost in vain. Every defeat is a lesson in disguise. Let us find it together.";
                else if (finalStatus != GameStatus.Ongoing) open = "A draw! Neither army could break the other. Let us see where the battle could have been won.";
                else open = "Let us pause the battle and look at how it has gone so far, my young friend.";
            }
            else
            {
                open = finalStatus == GameStatus.Checkmate
                    ? $"What a battle! {(pos[played.Count].Side == Board.White ? "Black" : "White")} has won the day. Let us see how it happened."
                    : finalStatus != GameStatus.Ongoing ? "A draw! Let us look at the key moments together." : "Let us pause and look at the key moments so far.";
            }
            string stats;
            if (r.Moves == 0) stats = "";
            else if (secondPerson)
                stats = $" You played {r.Moves} moves, and {r.Excellent} of them were excellent." +
                        (r.Blunders + r.Mistakes > 0 ? $" There {(r.Blunders + r.Mistakes == 1 ? "was" : "were")} {Count(r.Blunders + r.Mistakes, "big mistake")} I want to show you." : " Not a single big mistake. Impressive!");
            else stats = $" Between you there {(r.Blunders + r.Mistakes == 1 ? "was" : "were")} {Count(r.Blunders + r.Mistakes, "big mistake")}.";
            b.Text = open + stats;
            b.Speech = b.Text;
            r.Beats.Add(b);
        }

        static string Count(int n, string noun) => n == 1 ? $"one {noun}" : $"{n} {noun}s";

        void Tips(GameReviewResult r, Why[] why)
        {
            var tips = new List<string>();
            int[] whyCount = new int[Enum.GetValues(typeof(Why)).Length];
            for (int i = 0; i < why.Length; i++)
                if (reviewed[pos[i].Side] && r.Grades[i] >= MoveGrade.Mistake) whyCount[(int)why[i]]++;

            if (whyCount[(int)Why.AllowedMate] > 0)
                tips.Add("Before every move, ask yourself: what checks can my opponent make next? Checkmates sneak up on kings that don't look.");
            if (whyCount[(int)Why.HungMovedPiece] + whyCount[(int)Why.LeftAttacked] + whyCount[(int)Why.LeftUndefended] > 0)
                tips.Add("Do a blunder check before you let go of a piece: can anything of mine be captured for free? If yes, protect it or move it.");
            if (whyCount[(int)Why.MissedCapture] + whyCount[(int)Why.MissedMate] > 0)
                tips.Add("Look for checks, captures and threats on every turn, yours and theirs. Free pieces are lying around more often than you think.");

            foreach (int c in new[] { Board.White, Board.Black })
            {
                if (!reviewed[c]) continue;
                bool castled = false;
                int queenEarly = 0, ownMoves = 0;
                for (int i = 0; i < played.Count; i++)
                {
                    if (pos[i].Side != c) continue;
                    ownMoves++;
                    var t = Board.TypeOf(pos[i].Sq[played[i].From]);
                    if (played[i].IsCastle) castled = true;
                    if (t == PieceType.Queen && ownMoves <= 5) queenEarly++;
                }
                string who = secondPerson ? "your" : (c == Board.White ? "White's" : "Black's");
                if (!castled && ownMoves >= 12 && tips.Count < 3)
                    tips.Add($"Try to castle early. It tucks {who} king away safely and wakes up a rook.");
                if (queenEarly >= 2 && tips.Count < 3)
                    tips.Add("Don't rush the queen out in the opening. Enemy pieces will chase her around and gain time. Bring out knights and bishops first.");
            }
            if (tips.Count == 0) tips.Add("Keep doing what you are doing: develop your pieces, keep your king safe, and always check what your opponent is threatening.");
            if (tips.Count > 3) tips.RemoveRange(3, tips.Count - 3);

            var b = new ReviewBeat { Title = "Lessons for next time", Ply = played.Count };
            bool lost = secondPerson && LostBy(human);
            string lead = lost ? "So, how do we avoid a loss like this next time?" : "Here is what to remember for the next battle.";
            b.Text = lead + " " + string.Join(" ", tips);
            b.Speech = b.Text;
            r.Beats.Add(b);

            var bye = new ReviewBeat { Title = "Farewell", Ply = played.Count };
            bye.Text = lost ? "Now, set up the pieces and try again. Every master was once a beginner who refused to give up!"
                            : "Splendid work. Now go and win another one. I shall be watching!";
            bye.Speech = bye.Text;
            r.Beats.Add(bye);
        }
    }
}

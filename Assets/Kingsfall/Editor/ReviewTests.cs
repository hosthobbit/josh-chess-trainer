using System.Collections.Generic;
using UnityEngine;
using Kingsfall.Chess;

namespace Kingsfall.EditorTools
{
    /// <summary>Prints the grandmaster's review for a few scripted games (batch: -executeMethod ...ReviewTests.Run).</summary>
    public static class ReviewTests
    {
        public static List<Move> Line(params string[] uci)
        {
            var b = Board.StartPosition();
            var list = new List<Move>();
            foreach (var u in uci)
            {
                var m = b.LegalMoves().Find(x => x.ToString() == u);
                if (m.IsNull) throw new System.Exception("illegal " + u);
                list.Add(m);
                b.MakeMove(m);
            }
            return list;
        }

        static void Print(string name, List<Move> moves, bool vsAI, int human)
        {
            var b = Board.StartPosition();
            foreach (var m in moves) b.MakeMove(m);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var review = new GameReviewBuilder(moves, b.Status(), vsAI, human, new ReviewAnalyser()).Build();
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"=== {name} ({sw.ElapsedMilliseconds} ms) grades: {string.Join(",", review.Grades)}");
            foreach (var beat in review.Beats)
            {
                sb.Append($"[{beat.Title} | ply {beat.Ply} |");
                foreach (var a in beat.Arrows) sb.Append($" {a.Kind}:{Board.SquareName(a.From)}{Board.SquareName(a.To)}");
                sb.AppendLine("]");
                sb.AppendLine("  T: " + beat.Text);
                sb.AppendLine("  S: " + beat.Speech);
            }
            Debug.Log(sb.ToString());
        }

        public static void Run()
        {
            // Scholar's mate: black (the human) walks into Qxf7#
            Print("scholar", Line("e2e4", "e7e5", "f1c4", "b8c6", "d1h5", "g8f6", "h5f7"), true, Board.Black);
            // white hangs the queen, then black wins: human is white
            Print("hung queen", Line("e2e4", "e7e5", "g1f3", "b8c6", "d1e2", "g8f6", "e2b5", "a7a6", "b1c3", "a6b5"), true, Board.White);
            // Legal's mate, two-player wording
            Print("two player", Line("e2e4", "e7e5", "g1f3", "d7d6", "f1c4", "c8g4", "b1c3", "g7g6", "f3e5", "g4d1", "c4f7", "e8e7", "c3d5"), false, Board.White);
            Debug.Log("KINGSFALL_REVIEWTEST_DONE");
        }
    }
}

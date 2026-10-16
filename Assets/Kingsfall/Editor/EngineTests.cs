using System.Collections.Generic;
using UnityEngine;
using Kingsfall.Chess;

namespace Kingsfall.EditorTools
{
    /// <summary>Perft move-generation check against known node counts.</summary>
    public static class EngineTests
    {
        static long Perft(Board b, int depth)
        {
            if (depth == 0) return 1;
            var moves = new List<Move>();
            b.GeneratePseudo(moves);
            long n = 0;
            foreach (var m in moves)
            {
                b.MakeMove(m);
                if (!b.InCheck(b.Side ^ 1)) n += Perft(b, depth - 1);
                b.UnmakeMove();
            }
            return n;
        }

        static Board FromFen(string fen)
        {
            var b = new Board();
            var parts = fen.Split(' ');
            int r = 7, f = 0;
            foreach (char c in parts[0])
            {
                if (c == '/') { r--; f = 0; continue; }
                if (char.IsDigit(c)) { f += c - '0'; continue; }
                int colour = char.IsUpper(c) ? 0 : 1;
                PieceType t = char.ToLower(c) switch { 'p' => PieceType.Pawn, 'n' => PieceType.Knight, 'b' => PieceType.Bishop, 'r' => PieceType.Rook, 'q' => PieceType.Queen, _ => PieceType.King };
                int sq = r * 8 + f;
                b.Sq[sq] = Board.Make(t, colour);
                if (t == PieceType.King) b.KingSq[colour] = sq;
                f++;
            }
            b.Side = parts[1] == "w" ? 0 : 1;
            b.Castling = 0;
            if (parts[2].Contains("K")) b.Castling |= Board.WK;
            if (parts[2].Contains("Q")) b.Castling |= Board.WQ;
            if (parts[2].Contains("k")) b.Castling |= Board.BK;
            if (parts[2].Contains("q")) b.Castling |= Board.BQ;
            b.EpSquare = parts[3] == "-" ? -1 : (parts[3][0] - 'a') + (parts[3][1] - '1') * 8;
            return b;
        }

        public static void Run()
        {
            bool ok = true;
            void Check(string name, Board b, int depth, long expect)
            {
                long got = Perft(b, depth);
                bool pass = got == expect;
                ok &= pass;
                Debug.Log($"PERFT {name} d{depth}: {got} expected {expect} {(pass ? "PASS" : "FAIL")}");
            }
            Check("start", Board.StartPosition(), 4, 197281);
            Check("kiwipete", FromFen("r3k2r/p1ppqpb1/bn2pnp1/3PN3/1p2P3/2N2Q1p/PPPBBPPP/R3K2R w KQkq -"), 3, 97862);
            Check("pos3", FromFen("8/2p5/3p4/KP5r/1R3p1k/8/4P1P1/8 w - -"), 5, 674624);
            Check("pos4", FromFen("r3k2r/Pppp1ppp/1b3nbN/nP6/BBP1P3/q4N2/Pp1P2PP/R2Q1RK1 w kq -"), 4, 422333);
            Check("pos5", FromFen("rnbq1k1r/pp1Pbppp/2p5/8/2B5/8/PPP1NnPP/RNBQK2R w KQ -"), 3, 62379);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var mv = new ChessAI(1).FindBestMove(Board.StartPosition(), Difficulty.Warlord);
            Debug.Log($"AI opening move {mv} in {sw.ElapsedMilliseconds}ms");
            Debug.Log(ok ? "ENGINE_TESTS_PASS" : "ENGINE_TESTS_FAIL");
            if (!ok) UnityEditor.EditorApplication.Exit(1);
        }
    }
}

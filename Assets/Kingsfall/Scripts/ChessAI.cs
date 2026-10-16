using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Kingsfall.Chess
{
    public enum Difficulty { Squire = 0, Knight = 1, Warlord = 2 }

    /// <summary>Alpha-beta search with iterative deepening, quiescence, a small TT and PST evaluation.</summary>
    public class ChessAI
    {
        const int Inf = 1000000;
        const int Mate = 100000;

        static readonly int[] Value = { 0, 100, 320, 330, 500, 900, 0 };

        // Piece-square tables from white's point of view, index a1 = 0 ... h8 = 63 (rank-major).
        static readonly int[] PawnPst =
        {
             0,  0,  0,  0,  0,  0,  0,  0,
             5, 10, 10,-20,-20, 10, 10,  5,
             5, -5,-10,  0,  0,-10, -5,  5,
             0,  0,  0, 20, 20,  0,  0,  0,
             5,  5, 10, 25, 25, 10,  5,  5,
            10, 10, 20, 30, 30, 20, 10, 10,
            50, 50, 50, 50, 50, 50, 50, 50,
             0,  0,  0,  0,  0,  0,  0,  0,
        };
        static readonly int[] KnightPst =
        {
            -50,-40,-30,-30,-30,-30,-40,-50,
            -40,-20,  0,  5,  5,  0,-20,-40,
            -30,  5, 10, 15, 15, 10,  5,-30,
            -30,  0, 15, 20, 20, 15,  0,-30,
            -30,  5, 15, 20, 20, 15,  5,-30,
            -30,  0, 10, 15, 15, 10,  0,-30,
            -40,-20,  0,  0,  0,  0,-20,-40,
            -50,-40,-30,-30,-30,-30,-40,-50,
        };
        static readonly int[] BishopPst =
        {
            -20,-10,-10,-10,-10,-10,-10,-20,
            -10,  5,  0,  0,  0,  0,  5,-10,
            -10, 10, 10, 10, 10, 10, 10,-10,
            -10,  0, 10, 10, 10, 10,  0,-10,
            -10,  5,  5, 10, 10,  5,  5,-10,
            -10,  0,  5, 10, 10,  5,  0,-10,
            -10,  0,  0,  0,  0,  0,  0,-10,
            -20,-10,-10,-10,-10,-10,-10,-20,
        };
        static readonly int[] RookPst =
        {
              0,  0,  0,  5,  5,  0,  0,  0,
             -5,  0,  0,  0,  0,  0,  0, -5,
             -5,  0,  0,  0,  0,  0,  0, -5,
             -5,  0,  0,  0,  0,  0,  0, -5,
             -5,  0,  0,  0,  0,  0,  0, -5,
             -5,  0,  0,  0,  0,  0,  0, -5,
              5, 10, 10, 10, 10, 10, 10,  5,
              0,  0,  0,  0,  0,  0,  0,  0,
        };
        static readonly int[] QueenPst =
        {
            -20,-10,-10, -5, -5,-10,-10,-20,
            -10,  0,  5,  0,  0,  0,  0,-10,
            -10,  5,  5,  5,  5,  5,  0,-10,
              0,  0,  5,  5,  5,  5,  0, -5,
             -5,  0,  5,  5,  5,  5,  0, -5,
            -10,  0,  5,  5,  5,  5,  0,-10,
            -10,  0,  0,  0,  0,  0,  0,-10,
            -20,-10,-10, -5, -5,-10,-10,-20,
        };
        static readonly int[] KingMidPst =
        {
             20, 30, 10,  0,  0, 10, 30, 20,
             20, 20,  0,  0,  0,  0, 20, 20,
            -10,-20,-20,-20,-20,-20,-20,-10,
            -20,-30,-30,-40,-40,-30,-30,-20,
            -30,-40,-40,-50,-50,-40,-40,-30,
            -30,-40,-40,-50,-50,-40,-40,-30,
            -30,-40,-40,-50,-50,-40,-40,-30,
            -30,-40,-40,-50,-50,-40,-40,-30,
        };
        static readonly int[] KingEndPst =
        {
            -50,-30,-30,-30,-30,-30,-30,-50,
            -30,-30,  0,  0,  0,  0,-30,-30,
            -30,-10, 20, 30, 30, 20,-10,-30,
            -30,-10, 30, 40, 40, 30,-10,-30,
            -30,-10, 30, 40, 40, 30,-10,-30,
            -30,-10, 20, 30, 30, 20,-10,-30,
            -30,-20,-10,  0,  0,-10,-20,-30,
            -50,-40,-30,-20,-20,-30,-40,-50,
        };

        struct TTEntry
        {
            public ulong Key;
            public int Depth;
            public int Score;
            public int Flag; // 0 exact, 1 lower, 2 upper
            public Move Best;
        }

        const int TTSize = 1 << 17;
        readonly TTEntry[] tt = new TTEntry[TTSize];
        readonly Move[,] killers = new Move[64, 2];
        readonly Random rng;
        Stopwatch clock;
        long timeLimitMs;
        bool aborted;
        long nodes;

        public ChessAI(int seed = 0) { rng = seed == 0 ? new Random() : new Random(seed); }

        public Move FindBestMove(Board root, Difficulty difficulty)
        {
            var board = root.Clone();
            var legal = board.LegalMoves();
            if (legal.Count == 0) return default;
            if (legal.Count == 1) return legal[0];

            int maxDepth;
            int noise;
            switch (difficulty)
            {
                case Difficulty.Squire: maxDepth = 2; timeLimitMs = 600; noise = 90; break;
                case Difficulty.Knight: maxDepth = 4; timeLimitMs = 1500; noise = 12; break;
                default: maxDepth = 64; timeLimitMs = 3000; noise = 0; break;
            }

            Array.Clear(killers, 0, killers.Length);
            clock = Stopwatch.StartNew();
            aborted = false;
            nodes = 0;
            Move best = legal[0];

            for (int depth = 1; depth <= maxDepth; depth++)
            {
                int alpha = -Inf, beta = Inf;
                Move iterBest = best;
                int iterScore = -Inf;
                Order(board, legal, best, 0);
                var scored = new List<(Move m, int s)>();
                foreach (var m in legal)
                {
                    board.MakeMove(m);
                    int score = -Search(board, depth - 1, -beta, -alpha, 1, true);
                    board.UnmakeMove();
                    if (aborted) break;
                    if (noise > 0) score += rng.Next(-noise, noise + 1);
                    scored.Add((m, score));
                    if (score > iterScore) { iterScore = score; iterBest = m; }
                    if (score > alpha) alpha = score;
                }
                if (aborted && depth > 1) break;
                best = iterBest;
                if (iterScore > Mate - 100) break;
                if (clock.ElapsedMilliseconds > timeLimitMs / 2) break;
            }
            return best;
        }

        int Search(Board b, int depth, int alpha, int beta, int ply, bool allowNull)
        {
            if ((++nodes & 2047) == 0 && clock.ElapsedMilliseconds > timeLimitMs) aborted = true;
            if (aborted) return 0;

            if (ply > 0 && (b.HalfMove >= 100 || b.RepetitionCount() >= 2)) return 0;

            bool inCheck = b.InCheck();
            if (inCheck) depth++;
            if (depth <= 0) return Quiesce(b, alpha, beta, ply);

            int alphaOrig = alpha;
            ref var e = ref tt[b.Hash & (TTSize - 1)];
            Move ttMove = default;
            if (e.Key == b.Hash)
            {
                ttMove = e.Best;
                if (e.Depth >= depth)
                {
                    if (e.Flag == 0) return e.Score;
                    if (e.Flag == 1 && e.Score >= beta) return e.Score;
                    if (e.Flag == 2 && e.Score <= alpha) return e.Score;
                }
            }

            // Null-move pruning outside of check and pawn endings.
            if (allowNull && !inCheck && depth >= 3 && HasPieces(b, b.Side))
            {
                b.MakeNull();
                int score = -Search(b, depth - 3, -beta, -beta + 1, ply + 1, false);
                b.UnmakeNull();
                if (aborted) return 0;
                if (score >= beta) return beta;
            }

            var moves = new List<Move>(48);
            b.GeneratePseudo(moves);
            Order(b, moves, ttMove, ply);

            int legalCount = 0;
            int bestScore = -Inf;
            Move bestMove = default;
            foreach (var m in moves)
            {
                b.MakeMove(m);
                if (b.InCheck(b.Side ^ 1)) { b.UnmakeMove(); continue; }
                legalCount++;
                int score = -Search(b, depth - 1, -beta, -alpha, ply + 1, true);
                b.UnmakeMove();
                if (aborted) return 0;
                if (score > bestScore) { bestScore = score; bestMove = m; }
                if (score > alpha) alpha = score;
                if (alpha >= beta)
                {
                    if (!m.IsCapture && ply < 64)
                    {
                        killers[ply, 1] = killers[ply, 0];
                        killers[ply, 0] = m;
                    }
                    break;
                }
            }

            if (legalCount == 0) return inCheck ? -Mate + ply : 0;

            e.Key = b.Hash;
            e.Depth = depth;
            e.Score = bestScore;
            e.Best = bestMove;
            e.Flag = bestScore <= alphaOrig ? 2 : bestScore >= beta ? 1 : 0;
            return bestScore;
        }

        int Quiesce(Board b, int alpha, int beta, int ply)
        {
            if ((++nodes & 2047) == 0 && clock.ElapsedMilliseconds > timeLimitMs) aborted = true;
            if (aborted) return 0;
            int stand = Evaluate(b);
            if (stand >= beta) return beta;
            if (stand > alpha) alpha = stand;
            if (ply > 40) return stand;

            var moves = new List<Move>(16);
            b.GeneratePseudo(moves, true);
            Order(b, moves, default, 63);
            foreach (var m in moves)
            {
                b.MakeMove(m);
                if (b.InCheck(b.Side ^ 1)) { b.UnmakeMove(); continue; }
                int score = -Quiesce(b, -beta, -alpha, ply + 1);
                b.UnmakeMove();
                if (aborted) return 0;
                if (score >= beta) return beta;
                if (score > alpha) alpha = score;
            }
            return alpha;
        }

        static bool HasPieces(Board b, int side)
        {
            for (int s = 0; s < 64; s++)
            {
                int p = b.Sq[s];
                if (p == 0 || Board.ColourOf(p) != side) continue;
                var t = Board.TypeOf(p);
                if (t != PieceType.Pawn && t != PieceType.King) return true;
            }
            return false;
        }

        void Order(Board b, List<Move> moves, Move first, int ply)
        {
            var keys = new int[moves.Count];
            for (int i = 0; i < moves.Count; i++)
            {
                var m = moves[i];
                int k = 0;
                if (!first.IsNull && m.Equals(first)) k = 1000000;
                else if (m.IsCapture)
                {
                    int victim = m.IsEnPassant ? 100 : Value[(int)Board.TypeOf(b.Sq[m.To])];
                    int attacker = Value[(int)Board.TypeOf(b.Sq[m.From])];
                    k = 100000 + victim * 10 - attacker / 10;
                }
                else if (ply < 64 && (m.Equals(killers[ply, 0]) || m.Equals(killers[ply, 1]))) k = 90000;
                if (m.IsPromotion) k += 80000 + Value[(int)m.Promo];
                keys[i] = k;
            }
            // insertion sort, descending
            for (int i = 1; i < moves.Count; i++)
            {
                var m = moves[i]; int k = keys[i]; int j = i - 1;
                while (j >= 0 && keys[j] < k) { moves[j + 1] = moves[j]; keys[j + 1] = keys[j]; j--; }
                moves[j + 1] = m; keys[j + 1] = k;
            }
        }

        /// <summary>Static evaluation from the side to move's point of view.</summary>
        public static int Evaluate(Board b)
        {
            int mg = 0;
            int phase = 0;
            int whiteKingEnd = 0, blackKingEnd = 0, whiteKingMid = 0, blackKingMid = 0;
            int bishopsW = 0, bishopsB = 0;
            for (int s = 0; s < 64; s++)
            {
                int p = b.Sq[s];
                if (p == 0) continue;
                var t = Board.TypeOf(p);
                int c = Board.ColourOf(p);
                int idx = c == Board.White ? s : (s ^ 56);
                int v = Value[(int)t];
                switch (t)
                {
                    case PieceType.Pawn: v += PawnPst[idx]; break;
                    case PieceType.Knight: v += KnightPst[idx]; phase += 1; break;
                    case PieceType.Bishop: v += BishopPst[idx]; phase += 1; if (c == 0) bishopsW++; else bishopsB++; break;
                    case PieceType.Rook: v += RookPst[idx]; phase += 2; break;
                    case PieceType.Queen: v += QueenPst[idx]; phase += 4; break;
                    case PieceType.King:
                        if (c == 0) { whiteKingMid = KingMidPst[idx]; whiteKingEnd = KingEndPst[idx]; }
                        else { blackKingMid = KingMidPst[idx]; blackKingEnd = KingEndPst[idx]; }
                        break;
                }
                mg += c == Board.White ? v : -v;
            }
            if (bishopsW >= 2) mg += 30;
            if (bishopsB >= 2) mg -= 30;
            phase = Math.Min(phase, 24);
            int king = ((whiteKingMid - blackKingMid) * phase + (whiteKingEnd - blackKingEnd) * (24 - phase)) / 24;
            int score = mg + king;
            return b.Side == Board.White ? score : -score;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Text;

namespace Kingsfall.Chess
{
    public enum PieceType { None = 0, Pawn = 1, Knight = 2, Bishop = 3, Rook = 4, Queen = 5, King = 6 }

    public enum GameStatus { Ongoing, Checkmate, Stalemate, DrawFiftyMove, DrawRepetition, DrawMaterial }

    [Flags]
    public enum MoveFlags { None = 0, Capture = 1, EnPassant = 2, Castle = 4, DoublePush = 8, Promotion = 16 }

    public struct Move : IEquatable<Move>
    {
        public int From;
        public int To;
        public PieceType Promo;
        public MoveFlags Flags;

        public Move(int from, int to, MoveFlags flags = MoveFlags.None, PieceType promo = PieceType.None)
        {
            From = from; To = to; Flags = flags; Promo = promo;
        }

        public bool IsCapture => (Flags & MoveFlags.Capture) != 0;
        public bool IsCastle => (Flags & MoveFlags.Castle) != 0;
        public bool IsEnPassant => (Flags & MoveFlags.EnPassant) != 0;
        public bool IsPromotion => Promo != PieceType.None;
        public bool IsNull => From == To;

        public bool Equals(Move o) => From == o.From && To == o.To && Promo == o.Promo;
        public override bool Equals(object obj) => obj is Move m && Equals(m);
        public override int GetHashCode() => From | (To << 6) | ((int)Promo << 12);
        public override string ToString() => Board.SquareName(From) + Board.SquareName(To) + (IsPromotion ? "nbrq"[(int)Promo - 2].ToString() : "");
    }

    /// <summary>Mailbox chess board with full rules. Square index = rank * 8 + file, white on ranks 0-1.</summary>
    public class Board
    {
        public const int White = 0, Black = 1;
        public const int WK = 1, WQ = 2, BK = 4, BQ = 8;

        // piece code: 0 empty, else type | (colour << 3)
        public readonly int[] Sq = new int[64];
        public int Side;
        public int Castling;
        public int EpSquare = -1;
        public int HalfMove;
        public int FullMove = 1;
        public ulong Hash;
        public readonly int[] KingSq = new int[2];

        struct Undo
        {
            public Move Move;
            public int Captured;
            public int CapturedSq;
            public int Castling;
            public int Ep;
            public int HalfMove;
            public ulong Hash;
        }

        readonly List<Undo> history = new List<Undo>(512);
        readonly List<ulong> hashHistory = new List<ulong>(512);

        static readonly ulong[,] ZPiece = new ulong[16, 64];
        static readonly ulong[] ZCastle = new ulong[16];
        static readonly ulong[] ZEp = new ulong[8];
        static readonly ulong ZSide;

        static readonly int[] KnightD = { 17, 15, 10, 6, -6, -10, -15, -17 };
        static readonly int[] KnightDf = { 1, -1, 2, -2, 2, -2, 1, -1 };
        static readonly int[] KingDr = { 1, 1, 1, 0, 0, -1, -1, -1 };
        static readonly int[] KingDf = { -1, 0, 1, -1, 1, -1, 0, 1 };
        static readonly int[] DiagDr = { 1, 1, -1, -1 };
        static readonly int[] DiagDf = { 1, -1, 1, -1 };
        static readonly int[] OrthDr = { 1, -1, 0, 0 };
        static readonly int[] OrthDf = { 0, 0, 1, -1 };

        static Board()
        {
            var rng = new Random(0x5EED);
            ulong R()
            {
                var b = new byte[8];
                rng.NextBytes(b);
                return BitConverter.ToUInt64(b, 0);
            }
            for (int p = 0; p < 16; p++) for (int s = 0; s < 64; s++) ZPiece[p, s] = R();
            for (int i = 0; i < 16; i++) ZCastle[i] = R();
            for (int i = 0; i < 8; i++) ZEp[i] = R();
            ZSide = R();
        }

        public static int Make(PieceType t, int colour) => (int)t | (colour << 3);
        public static PieceType TypeOf(int code) => (PieceType)(code & 7);
        public static int ColourOf(int code) => code >> 3;
        public static int File(int sq) => sq & 7;
        public static int Rank(int sq) => sq >> 3;
        public static string SquareName(int sq) => ((char)('a' + File(sq))).ToString() + (char)('1' + Rank(sq));

        public static Board StartPosition()
        {
            var b = new Board();
            PieceType[] back = { PieceType.Rook, PieceType.Knight, PieceType.Bishop, PieceType.Queen, PieceType.King, PieceType.Bishop, PieceType.Knight, PieceType.Rook };
            for (int f = 0; f < 8; f++)
            {
                b.Sq[f] = Make(back[f], White);
                b.Sq[8 + f] = Make(PieceType.Pawn, White);
                b.Sq[48 + f] = Make(PieceType.Pawn, Black);
                b.Sq[56 + f] = Make(back[f], Black);
            }
            b.Side = White;
            b.Castling = WK | WQ | BK | BQ;
            b.KingSq[0] = 4;
            b.KingSq[1] = 60;
            b.Hash = b.ComputeHash();
            return b;
        }

        public Board Clone()
        {
            var b = new Board();
            Array.Copy(Sq, b.Sq, 64);
            b.Side = Side; b.Castling = Castling; b.EpSquare = EpSquare; b.HalfMove = HalfMove; b.FullMove = FullMove;
            b.Hash = Hash; b.KingSq[0] = KingSq[0]; b.KingSq[1] = KingSq[1];
            b.hashHistory.AddRange(hashHistory);
            return b;
        }

        public int Ply => history.Count;

        ulong ComputeHash()
        {
            ulong h = 0;
            for (int s = 0; s < 64; s++) if (Sq[s] != 0) h ^= ZPiece[Sq[s], s];
            h ^= ZCastle[Castling];
            if (EpSquare >= 0) h ^= ZEp[File(EpSquare)];
            if (Side == Black) h ^= ZSide;
            return h;
        }

        // ---------------- attacks ----------------

        public bool IsAttacked(int sq, int by)
        {
            int r = Rank(sq), f = File(sq);
            // pawns
            int pr = by == White ? r - 1 : r + 1;
            if (pr >= 0 && pr < 8)
            {
                int pawn = Make(PieceType.Pawn, by);
                if (f > 0 && Sq[pr * 8 + f - 1] == pawn) return true;
                if (f < 7 && Sq[pr * 8 + f + 1] == pawn) return true;
            }
            // knights
            int knight = Make(PieceType.Knight, by);
            for (int i = 0; i < 8; i++)
            {
                int t = sq + KnightD[i], tf = f + KnightDf[i];
                if (t < 0 || t > 63 || tf < 0 || tf > 7) continue;
                if (Sq[t] == knight) return true;
            }
            // king
            int king = Make(PieceType.King, by);
            for (int i = 0; i < 8; i++)
            {
                int tr = r + KingDr[i], tf = f + KingDf[i];
                if (tr < 0 || tr > 7 || tf < 0 || tf > 7) continue;
                if (Sq[tr * 8 + tf] == king) return true;
            }
            // sliders
            int bishop = Make(PieceType.Bishop, by), rook = Make(PieceType.Rook, by), queen = Make(PieceType.Queen, by);
            for (int d = 0; d < 4; d++)
            {
                int tr = r + DiagDr[d], tf = f + DiagDf[d];
                while (tr >= 0 && tr < 8 && tf >= 0 && tf < 8)
                {
                    int p = Sq[tr * 8 + tf];
                    if (p != 0) { if (p == bishop || p == queen) return true; break; }
                    tr += DiagDr[d]; tf += DiagDf[d];
                }
                tr = r + OrthDr[d]; tf = f + OrthDf[d];
                while (tr >= 0 && tr < 8 && tf >= 0 && tf < 8)
                {
                    int p = Sq[tr * 8 + tf];
                    if (p != 0) { if (p == rook || p == queen) return true; break; }
                    tr += OrthDr[d]; tf += OrthDf[d];
                }
            }
            return false;
        }

        public bool InCheck(int colour) => IsAttacked(KingSq[colour], colour ^ 1);
        public bool InCheck() => InCheck(Side);

        // ---------------- move generation ----------------

        public void GeneratePseudo(List<Move> moves, bool capturesOnly = false)
        {
            int us = Side, them = us ^ 1;
            for (int s = 0; s < 64; s++)
            {
                int p = Sq[s];
                if (p == 0 || ColourOf(p) != us) continue;
                int r = Rank(s), f = File(s);
                switch (TypeOf(p))
                {
                    case PieceType.Pawn:
                    {
                        int dir = us == White ? 1 : -1;
                        int startRank = us == White ? 1 : 6;
                        int promoRank = us == White ? 7 : 0;
                        int nr = r + dir;
                        if (nr < 0 || nr > 7) break;
                        int fwd = nr * 8 + f;
                        if (Sq[fwd] == 0 && (!capturesOnly || nr == promoRank))
                        {
                            AddPawn(moves, s, fwd, MoveFlags.None, nr == promoRank);
                            if (r == startRank && !capturesOnly)
                            {
                                int dbl = (r + 2 * dir) * 8 + f;
                                if (Sq[dbl] == 0) moves.Add(new Move(s, dbl, MoveFlags.DoublePush));
                            }
                        }
                        for (int df = -1; df <= 1; df += 2)
                        {
                            int cf = f + df;
                            if (cf < 0 || cf > 7) continue;
                            int t = nr * 8 + cf;
                            if (Sq[t] != 0 && ColourOf(Sq[t]) == them) AddPawn(moves, s, t, MoveFlags.Capture, nr == promoRank);
                            else if (t == EpSquare) moves.Add(new Move(s, t, MoveFlags.Capture | MoveFlags.EnPassant));
                        }
                        break;
                    }
                    case PieceType.Knight:
                        for (int i = 0; i < 8; i++)
                        {
                            int t = s + KnightD[i], tf = f + KnightDf[i];
                            if (t < 0 || t > 63 || tf < 0 || tf > 7) continue;
                            AddIfFree(moves, s, t, them, capturesOnly);
                        }
                        break;
                    case PieceType.Bishop:
                        Slide(moves, s, DiagDr, DiagDf, them, capturesOnly);
                        break;
                    case PieceType.Rook:
                        Slide(moves, s, OrthDr, OrthDf, them, capturesOnly);
                        break;
                    case PieceType.Queen:
                        Slide(moves, s, DiagDr, DiagDf, them, capturesOnly);
                        Slide(moves, s, OrthDr, OrthDf, them, capturesOnly);
                        break;
                    case PieceType.King:
                        for (int i = 0; i < 8; i++)
                        {
                            int tr = r + KingDr[i], tf = f + KingDf[i];
                            if (tr < 0 || tr > 7 || tf < 0 || tf > 7) continue;
                            AddIfFree(moves, s, tr * 8 + tf, them, capturesOnly);
                        }
                        if (!capturesOnly) AddCastles(moves, us);
                        break;
                }
            }
        }

        void AddPawn(List<Move> moves, int from, int to, MoveFlags flags, bool promo)
        {
            if (!promo) { moves.Add(new Move(from, to, flags)); return; }
            flags |= MoveFlags.Promotion;
            moves.Add(new Move(from, to, flags, PieceType.Queen));
            moves.Add(new Move(from, to, flags, PieceType.Knight));
            moves.Add(new Move(from, to, flags, PieceType.Rook));
            moves.Add(new Move(from, to, flags, PieceType.Bishop));
        }

        void AddIfFree(List<Move> moves, int from, int to, int them, bool capturesOnly)
        {
            int t = Sq[to];
            if (t == 0) { if (!capturesOnly) moves.Add(new Move(from, to)); }
            else if (ColourOf(t) == them) moves.Add(new Move(from, to, MoveFlags.Capture));
        }

        void Slide(List<Move> moves, int s, int[] dr, int[] df, int them, bool capturesOnly)
        {
            int r = Rank(s), f = File(s);
            for (int d = 0; d < 4; d++)
            {
                int tr = r + dr[d], tf = f + df[d];
                while (tr >= 0 && tr < 8 && tf >= 0 && tf < 8)
                {
                    int t = tr * 8 + tf;
                    int p = Sq[t];
                    if (p == 0) { if (!capturesOnly) moves.Add(new Move(s, t)); }
                    else
                    {
                        if (ColourOf(p) == them) moves.Add(new Move(s, t, MoveFlags.Capture));
                        break;
                    }
                    tr += dr[d]; tf += df[d];
                }
            }
        }

        void AddCastles(List<Move> moves, int us)
        {
            int them = us ^ 1;
            int home = us == White ? 0 : 56;
            int kingSide = us == White ? WK : BK, queenSide = us == White ? WQ : BQ;
            if (KingSq[us] != home + 4) return;
            if ((Castling & kingSide) != 0 && Sq[home + 5] == 0 && Sq[home + 6] == 0 && Sq[home + 7] == Make(PieceType.Rook, us)
                && !IsAttacked(home + 4, them) && !IsAttacked(home + 5, them) && !IsAttacked(home + 6, them))
                moves.Add(new Move(home + 4, home + 6, MoveFlags.Castle));
            if ((Castling & queenSide) != 0 && Sq[home + 3] == 0 && Sq[home + 2] == 0 && Sq[home + 1] == 0 && Sq[home] == Make(PieceType.Rook, us)
                && !IsAttacked(home + 4, them) && !IsAttacked(home + 3, them) && !IsAttacked(home + 2, them))
                moves.Add(new Move(home + 4, home + 2, MoveFlags.Castle));
        }

        public List<Move> LegalMoves()
        {
            var pseudo = new List<Move>(64);
            GeneratePseudo(pseudo);
            var legal = new List<Move>(pseudo.Count);
            foreach (var m in pseudo)
            {
                MakeMove(m);
                if (!InCheck(Side ^ 1)) legal.Add(m);
                UnmakeMove();
            }
            return legal;
        }

        public bool HasLegalMove()
        {
            var pseudo = new List<Move>(64);
            GeneratePseudo(pseudo);
            foreach (var m in pseudo)
            {
                MakeMove(m);
                bool ok = !InCheck(Side ^ 1);
                UnmakeMove();
                if (ok) return true;
            }
            return false;
        }

        // ---------------- make / unmake ----------------

        static readonly int[] CastleMask = BuildCastleMask();

        static int[] BuildCastleMask()
        {
            var m = new int[64];
            for (int i = 0; i < 64; i++) m[i] = 15;
            m[0] &= ~WQ; m[7] &= ~WK; m[4] &= ~(WK | WQ);
            m[56] &= ~BQ; m[63] &= ~BK; m[60] &= ~(BK | BQ);
            return m;
        }

        public void MakeMove(Move m)
        {
            var u = new Undo { Move = m, Castling = Castling, Ep = EpSquare, HalfMove = HalfMove, Hash = Hash, Captured = 0, CapturedSq = -1 };
            hashHistory.Add(Hash);
            int piece = Sq[m.From];
            int us = Side;

            Hash ^= ZCastle[Castling];
            if (EpSquare >= 0) Hash ^= ZEp[File(EpSquare)];

            if (m.IsEnPassant)
            {
                int capSq = m.To + (us == White ? -8 : 8);
                u.Captured = Sq[capSq]; u.CapturedSq = capSq;
                Hash ^= ZPiece[Sq[capSq], capSq];
                Sq[capSq] = 0;
            }
            else if (Sq[m.To] != 0)
            {
                u.Captured = Sq[m.To]; u.CapturedSq = m.To;
                Hash ^= ZPiece[Sq[m.To], m.To];
            }

            Hash ^= ZPiece[piece, m.From];
            Sq[m.From] = 0;
            int placed = m.IsPromotion ? Make(m.Promo, us) : piece;
            Sq[m.To] = placed;
            Hash ^= ZPiece[placed, m.To];

            if (TypeOf(piece) == PieceType.King)
            {
                KingSq[us] = m.To;
                if (m.IsCastle)
                {
                    int home = us == White ? 0 : 56;
                    int rf, rt;
                    if (m.To == home + 6) { rf = home + 7; rt = home + 5; } else { rf = home; rt = home + 3; }
                    int rook = Sq[rf];
                    Hash ^= ZPiece[rook, rf] ^ ZPiece[rook, rt];
                    Sq[rt] = rook; Sq[rf] = 0;
                }
            }

            Castling &= CastleMask[m.From] & CastleMask[m.To];
            EpSquare = (m.Flags & MoveFlags.DoublePush) != 0 ? (m.From + m.To) / 2 : -1;
            HalfMove = (TypeOf(piece) == PieceType.Pawn || u.Captured != 0) ? 0 : HalfMove + 1;
            if (us == Black) FullMove++;
            Side ^= 1;

            Hash ^= ZCastle[Castling];
            if (EpSquare >= 0) Hash ^= ZEp[File(EpSquare)];
            Hash ^= ZSide;
            history.Add(u);
        }

        public void UnmakeMove()
        {
            var u = history[history.Count - 1];
            history.RemoveAt(history.Count - 1);
            hashHistory.RemoveAt(hashHistory.Count - 1);
            var m = u.Move;
            Side ^= 1;
            int us = Side;
            if (us == Black) FullMove--;
            int placed = Sq[m.To];
            int piece = m.IsPromotion ? Make(PieceType.Pawn, us) : placed;
            Sq[m.From] = piece;
            Sq[m.To] = 0;
            if (u.Captured != 0) Sq[u.CapturedSq] = u.Captured;
            if (TypeOf(piece) == PieceType.King)
            {
                KingSq[us] = m.From;
                if (m.IsCastle)
                {
                    int home = us == White ? 0 : 56;
                    int rf, rt;
                    if (m.To == home + 6) { rf = home + 7; rt = home + 5; } else { rf = home; rt = home + 3; }
                    Sq[rf] = Sq[rt]; Sq[rt] = 0;
                }
            }
            Castling = u.Castling; EpSquare = u.Ep; HalfMove = u.HalfMove; Hash = u.Hash;
        }

        public void MakeNull()
        {
            var u = new Undo { Move = new Move(0, 0), Castling = Castling, Ep = EpSquare, HalfMove = HalfMove, Hash = Hash, CapturedSq = -1 };
            hashHistory.Add(Hash);
            if (EpSquare >= 0) Hash ^= ZEp[File(EpSquare)];
            EpSquare = -1;
            Side ^= 1;
            Hash ^= ZSide;
            history.Add(u);
        }

        public void UnmakeNull()
        {
            var u = history[history.Count - 1];
            history.RemoveAt(history.Count - 1);
            hashHistory.RemoveAt(hashHistory.Count - 1);
            Side ^= 1;
            EpSquare = u.Ep; Hash = u.Hash; HalfMove = u.HalfMove; Castling = u.Castling;
        }

        // ---------------- status ----------------

        public int RepetitionCount()
        {
            int count = 1;
            for (int i = hashHistory.Count - 2; i >= 0 && i >= hashHistory.Count - HalfMove - 1; i -= 2)
                if (hashHistory[i] == Hash) count++;
            return count;
        }

        public bool InsufficientMaterial()
        {
            int minors = 0;
            int bishopColourMask = 0;
            for (int s = 0; s < 64; s++)
            {
                var t = TypeOf(Sq[s]);
                if (t == PieceType.None || t == PieceType.King) continue;
                if (t == PieceType.Pawn || t == PieceType.Rook || t == PieceType.Queen) return false;
                minors++;
                if (t == PieceType.Bishop) bishopColourMask |= 1 << ((Rank(s) + File(s)) & 1);
                else bishopColourMask |= 4;
            }
            if (minors <= 1) return true;
            // only bishops all on the same colour
            return (bishopColourMask == 1 || bishopColourMask == 2);
        }

        public GameStatus Status()
        {
            if (!HasLegalMove()) return InCheck() ? GameStatus.Checkmate : GameStatus.Stalemate;
            if (HalfMove >= 100) return GameStatus.DrawFiftyMove;
            if (RepetitionCount() >= 3) return GameStatus.DrawRepetition;
            if (InsufficientMaterial()) return GameStatus.DrawMaterial;
            return GameStatus.Ongoing;
        }

        // ---------------- notation ----------------

        public string ToSan(Move m)
        {
            var sb = new StringBuilder();
            int piece = Sq[m.From];
            var t = TypeOf(piece);
            if (m.IsCastle) sb.Append(File(m.To) == 6 ? "O-O" : "O-O-O");
            else
            {
                if (t == PieceType.Pawn)
                {
                    if (m.IsCapture) sb.Append((char)('a' + File(m.From))).Append('x');
                    sb.Append(SquareName(m.To));
                    if (m.IsPromotion) sb.Append('=').Append("NBRQ"[(int)m.Promo - 2]);
                }
                else
                {
                    sb.Append("NBRQK"[(int)t - 2]);
                    bool needFile = false, needRank = false, ambiguous = false;
                    foreach (var o in LegalMoves())
                    {
                        if (o.To != m.To || o.From == m.From || Sq[o.From] != piece) continue;
                        ambiguous = true;
                        if (File(o.From) == File(m.From)) needRank = true; else needFile = true;
                    }
                    if (ambiguous && !needFile && !needRank) needFile = true;
                    if (needFile) sb.Append((char)('a' + File(m.From)));
                    if (needRank) sb.Append((char)('1' + Rank(m.From)));
                    if (m.IsCapture) sb.Append('x');
                    sb.Append(SquareName(m.To));
                }
            }
            MakeMove(m);
            if (InCheck()) sb.Append(HasLegalMove() ? "+" : "#");
            UnmakeMove();
            return sb.ToString();
        }
    }
}

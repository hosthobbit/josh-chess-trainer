using System;
using UnityEngine;
using Kingsfall.Chess;

namespace Kingsfall.EditorTools
{
    public class Sculpt
    {
        public Func<Vector3, float> Shape;
        public Func<Vector3, float> Gold;
        public Bounds Bounds;
    }

    /// <summary>Staunton-inspired pieces sculpted from SDFs. Unit = one board square. Pieces face +Z.</summary>
    public static class PieceSculpts
    {
        static Vector2 V(float r, float y) => new Vector2(r, y);

        // Shared weighted foot: wide base, bead, gold ring.
        static readonly Vector2[] Foot =
        {
            V(0.335f, 0.000f), V(0.360f, 0.010f), V(0.368f, 0.032f), V(0.358f, 0.052f), V(0.330f, 0.064f),
            V(0.305f, 0.074f), V(0.318f, 0.094f), V(0.306f, 0.114f), V(0.272f, 0.130f),
        };

        static Vector2[] WithFoot(float scale, params Vector2[] rest)
        {
            var list = new Vector2[Foot.Length + rest.Length];
            for (int i = 0; i < Foot.Length; i++) list[i] = new Vector2(Foot[i].x * scale, Foot[i].y);
            for (int i = 0; i < rest.Length; i++) list[Foot.Length + i] = rest[i];
            return list;
        }

        static float Band(Vector3 p, float y0, float y1, float soft = 0.004f)
        {
            return Mathf.Clamp01((p.y - y0) / soft) * Mathf.Clamp01((y1 - p.y) / soft);
        }

        static float FootGold(Vector3 p) => Band(p, 0.072f, 0.118f);

        public static Sculpt Get(PieceType t)
        {
            switch (t)
            {
                case PieceType.Pawn: return Pawn();
                case PieceType.Knight: return Knight();
                case PieceType.Bishop: return Bishop();
                case PieceType.Rook: return Rook();
                case PieceType.Queen: return Queen();
                default: return King();
            }
        }

        static Bounds B(float r, float h) => new Bounds(new Vector3(0, h * 0.5f, 0), new Vector3(r * 2f, h, r * 2f));

        static Sculpt Pawn()
        {
            var body = new Lathe(WithFoot(1f,
                V(0.215f, 0.160f), V(0.170f, 0.210f), V(0.140f, 0.280f), V(0.122f, 0.360f), V(0.118f, 0.420f),
                V(0.170f, 0.442f), V(0.192f, 0.458f), V(0.182f, 0.474f), V(0.125f, 0.488f), V(0.100f, 0.500f)));
            return new Sculpt
            {
                Shape = p =>
                {
                    float d = body.Eval(p);
                    d = Sdf.SMin(d, Sdf.Sphere(p, new Vector3(0, 0.615f, 0), 0.152f), 0.03f);
                    return d;
                },
                Gold = p => Mathf.Max(FootGold(p), Band(p, 0.440f, 0.478f)),
                Bounds = B(0.38f, 0.78f),
            };
        }

        static Sculpt Rook()
        {
            var body = new Lathe(WithFoot(1.02f,
                V(0.240f, 0.160f), V(0.222f, 0.250f), V(0.212f, 0.400f), V(0.216f, 0.540f), V(0.232f, 0.615f),
                V(0.262f, 0.638f), V(0.272f, 0.658f), V(0.252f, 0.674f), V(0.258f, 0.690f), V(0.292f, 0.712f),
                V(0.302f, 0.735f), V(0.300f, 0.900f)));
            return new Sculpt
            {
                Shape = p =>
                {
                    float d = body.Eval(p);
                    // hollow battlement
                    d = Sdf.Sub(d, Sdf.Cylinder(p, new Vector3(0, 0.95f, 0), 0.205f, 0.13f), 0.01f);
                    // six crenel notches
                    for (int i = 0; i < 6; i++)
                    {
                        Vector3 q = Sdf.RotY(p, i * 60f + 30f);
                        d = Sdf.Sub(d, Sdf.Box(q, new Vector3(0, 0.93f, 0.28f), new Vector3(0.045f, 0.09f, 0.12f), 0.008f), 0.006f);
                    }
                    // arrow slits
                    for (int i = 0; i < 4; i++)
                    {
                        Vector3 q = Sdf.RotY(p, i * 90f + 45f);
                        d = Sdf.Sub(d, Sdf.Box(q, new Vector3(0, 0.43f, 0.215f), new Vector3(0.016f, 0.07f, 0.03f), 0.006f), 0.004f);
                    }
                    // masonry courses on the tower
                    float course = Mathf.Abs(Mathf.Repeat(p.y - 0.2f, 0.09f) - 0.045f);
                    if (p.y > 0.19f && p.y < 0.6f) d += Mathf.Max(0f, 0.004f - course) * 0.9f;
                    return d;
                },
                Gold = p => Mathf.Max(FootGold(p), Band(p, 0.630f, 0.700f)),
                Bounds = B(0.38f, 0.92f),
            };
        }

        static float Flat(Func<Vector3, float> f, Vector3 p, float sx)
        {
            return f(new Vector3(p.x / sx, p.y, p.z)) * Mathf.Min(sx, 1f);
        }

        static Sculpt Knight()
        {
            var foot = new Lathe(WithFoot(1.02f,
                V(0.262f, 0.158f), V(0.272f, 0.178f), V(0.282f, 0.198f), V(0.262f, 0.218f), V(0.240f, 0.226f)));
            Func<Vector3, float> mane = q =>
            {
                // gently rippled mane down the back of the neck
                Vector3 a = new Vector3(0, 0.80f, -0.06f), b = new Vector3(0, 0.30f, -0.19f);
                Vector3 pa = q - a, ba = b - a;
                float h = Mathf.Clamp01(Vector3.Dot(pa, ba) / Vector3.Dot(ba, ba));
                float r = Mathf.Lerp(0.062f, 0.085f, h) + 0.012f * Mathf.Sin(h * 38f);
                return (pa - ba * h).magnitude - r;
            };
            return new Sculpt
            {
                Shape = p =>
                {
                    float d = foot.Eval(p);
                    float neck = Flat(q => Sdf.RoundCone(q, new Vector3(0, 0.24f, -0.04f), new Vector3(0, 0.64f, 0.02f), 0.165f, 0.11f), p, 0.72f);
                    float chest = Flat(q => Sdf.Sphere(q, new Vector3(0, 0.34f, 0.07f), 0.165f), p, 0.85f);
                    float head = Flat(q => Sdf.RoundCone(q, new Vector3(0, 0.71f, -0.01f), new Vector3(0, 0.585f, 0.27f), 0.112f, 0.082f), p, 0.78f);
                    float muzzle = Flat(q => Sdf.RoundCone(q, new Vector3(0, 0.60f, 0.24f), new Vector3(0, 0.555f, 0.345f), 0.09f, 0.072f), p, 0.82f);
                    float jaw = Flat(q => Sdf.Sphere(q, new Vector3(0, 0.575f, 0.10f), 0.112f), p, 0.8f);
                    float poll = Flat(q => Sdf.Sphere(q, new Vector3(0, 0.75f, 0.0f), 0.085f), p, 0.85f);
                    float m = Flat(mane, p, 0.5f);

                    float horse = Sdf.SMin(neck, chest, 0.08f);
                    horse = Sdf.SMin(horse, jaw, 0.06f);
                    horse = Sdf.SMin(horse, head, 0.05f);
                    horse = Sdf.SMin(horse, muzzle, 0.04f);
                    horse = Sdf.SMin(horse, poll, 0.04f);
                    horse = Sdf.SMin(horse, m, 0.03f);
                    // ears
                    for (int s = -1; s <= 1; s += 2)
                    {
                        float ear = Sdf.RoundCone(p, new Vector3(0.045f * s, 0.77f, -0.02f), new Vector3(0.062f * s, 0.89f, -0.065f), 0.034f, 0.008f);
                        horse = Sdf.SMin(horse, ear, 0.025f);
                    }
                    // eyes, nostrils, mouth
                    for (int s = -1; s <= 1; s += 2)
                    {
                        horse = Sdf.Sub(horse, Sdf.Sphere(p, new Vector3(0.078f * s, 0.685f, 0.13f), 0.026f), 0.012f);
                        horse = Sdf.SMin(horse, Sdf.Sphere(p, new Vector3(0.068f * s, 0.685f, 0.13f), 0.019f), 0.004f);
                        horse = Sdf.Sub(horse, Sdf.Sphere(p, new Vector3(0.034f * s, 0.575f, 0.385f), 0.017f), 0.008f);
                    }
                    horse = Sdf.Sub(horse, Sdf.Box(p, new Vector3(0, 0.528f, 0.36f), new Vector3(0.09f, 0.006f, 0.06f), 0.004f), 0.006f);
                    // keep the horse above the plinth top
                    horse = Mathf.Max(horse, 0.2f - p.y);
                    return Sdf.SMin(d, horse, 0.04f);
                },
                Gold = p =>
                {
                    float maneGold = Flat(mane, p, 0.5f) < 0.006f && p.y > 0.3f ? 1f : 0f;
                    return Mathf.Max(Mathf.Max(FootGold(p), Band(p, 0.155f, 0.205f)), maneGold);
                },
                Bounds = new Bounds(new Vector3(0, 0.46f, 0.02f), new Vector3(0.76f, 0.92f, 0.82f)),
            };
        }

        static Sculpt Bishop()
        {
            var body = new Lathe(WithFoot(1f,
                V(0.222f, 0.160f), V(0.172f, 0.220f), V(0.140f, 0.320f), V(0.120f, 0.450f), V(0.114f, 0.560f),
                V(0.190f, 0.588f), V(0.210f, 0.604f), V(0.190f, 0.620f), V(0.150f, 0.634f), V(0.170f, 0.648f),
                V(0.150f, 0.664f), V(0.110f, 0.676f)));
            return new Sculpt
            {
                Shape = p =>
                {
                    float d = body.Eval(p);
                    float mitre = Sdf.Ellipsoid(p, new Vector3(0, 0.83f, 0), new Vector3(0.152f, 0.205f, 0.152f));
                    mitre = Sdf.SMin(mitre, Sdf.RoundCone(p, new Vector3(0, 0.90f, 0), new Vector3(0, 1.04f, 0), 0.10f, 0.012f), 0.05f);
                    // the diagonal slit
                    Vector3 q = Sdf.RotZ(p - new Vector3(0, 0.88f, 0), -38f);
                    mitre = Sdf.Sub(mitre, Sdf.Box(q, new Vector3(0, 0, 0.13f), new Vector3(0.22f, 0.013f, 0.13f)), 0.006f);
                    d = Sdf.SMin(d, mitre, 0.03f);
                    d = Sdf.SMin(d, Sdf.Sphere(p, new Vector3(0, 1.07f, 0), 0.036f), 0.02f);
                    return d;
                },
                Gold = p => Mathf.Max(Mathf.Max(FootGold(p), Band(p, 0.584f, 0.626f)), p.y > 1.035f ? 1f : 0f),
                Bounds = B(0.38f, 1.12f),
            };
        }

        static Sculpt Queen()
        {
            var body = new Lathe(WithFoot(1.03f,
                V(0.232f, 0.160f), V(0.182f, 0.230f), V(0.146f, 0.340f), V(0.126f, 0.480f), V(0.120f, 0.620f),
                V(0.130f, 0.720f), V(0.200f, 0.748f), V(0.222f, 0.764f), V(0.200f, 0.780f), V(0.150f, 0.794f),
                V(0.175f, 0.810f), V(0.155f, 0.826f), V(0.130f, 0.842f), V(0.150f, 0.900f), V(0.190f, 0.980f),
                V(0.228f, 1.060f), V(0.240f, 1.090f)));
            return new Sculpt
            {
                Shape = p =>
                {
                    float d = body.Eval(p);
                    d = Sdf.Sub(d, Sdf.Cylinder(p, new Vector3(0, 1.12f, 0), 0.19f, 0.06f), 0.01f);
                    for (int i = 0; i < 8; i++)
                    {
                        Vector3 q = Sdf.RotY(p, i * 45f + 22.5f);
                        d = Sdf.Sub(d, Sdf.Sphere(q, new Vector3(0, 1.115f, 0.25f), 0.052f), 0.01f);
                        Vector3 r = Sdf.RotY(p, i * 45f);
                        d = Sdf.SMin(d, Sdf.Sphere(r, new Vector3(0, 1.098f, 0.222f), 0.027f), 0.012f);
                    }
                    d = Sdf.SMin(d, Sdf.Sphere(p, new Vector3(0, 1.05f, 0), 0.16f), 0.02f);
                    d = Sdf.SMin(d, Sdf.RoundCone(p, new Vector3(0, 1.17f, 0), new Vector3(0, 1.22f, 0), 0.05f, 0.025f), 0.02f);
                    d = Sdf.SMin(d, Sdf.Sphere(p, new Vector3(0, 1.255f, 0), 0.048f), 0.015f);
                    return d;
                },
                Gold = p =>
                {
                    float g = Mathf.Max(FootGold(p), Band(p, 0.744f, 0.786f));
                    float rr = new Vector2(p.x, p.z).magnitude;
                    if (p.y > 1.07f && rr > 0.195f) g = 1f;     // coronet balls
                    if (p.y > 1.205f) g = 1f;                  // finial
                    return g;
                },
                Bounds = B(0.39f, 1.32f),
            };
        }

        static Sculpt King()
        {
            var body = new Lathe(WithFoot(1.05f,
                V(0.240f, 0.160f), V(0.190f, 0.230f), V(0.155f, 0.350f), V(0.135f, 0.500f), V(0.130f, 0.660f),
                V(0.140f, 0.760f), V(0.210f, 0.790f), V(0.232f, 0.806f), V(0.210f, 0.822f), V(0.160f, 0.836f),
                V(0.185f, 0.852f), V(0.165f, 0.866f), V(0.140f, 0.882f), V(0.160f, 0.940f), V(0.200f, 1.020f),
                V(0.236f, 1.100f), V(0.242f, 1.130f), V(0.180f, 1.152f), V(0.120f, 1.172f), V(0.080f, 1.190f)));
            return new Sculpt
            {
                Shape = p =>
                {
                    float d = body.Eval(p);
                    for (int i = 0; i < 8; i++)
                    {
                        Vector3 q = Sdf.RotY(p, i * 45f + 22.5f);
                        d = Sdf.Sub(d, Sdf.Capsule(q, new Vector3(0, 0.96f, 0.205f), new Vector3(0, 1.10f, 0.25f), 0.022f), 0.01f);
                    }
                    d = Sdf.SMin(d, Sdf.Sphere(p, new Vector3(0, 1.215f, 0), 0.05f), 0.02f);
                    float cross = Sdf.Box(p, new Vector3(0, 1.36f, 0), new Vector3(0.032f, 0.115f, 0.032f), 0.01f);
                    cross = Mathf.Min(cross, Sdf.Box(p, new Vector3(0, 1.385f, 0), new Vector3(0.095f, 0.032f, 0.032f), 0.01f));
                    d = Sdf.SMin(d, cross, 0.012f);
                    return d;
                },
                Gold = p => Mathf.Max(Mathf.Max(FootGold(p), Band(p, 0.786f, 0.828f)), p.y > 1.17f ? 1f : 0f),
                Bounds = B(0.40f, 1.50f),
            };
        }
    }
}

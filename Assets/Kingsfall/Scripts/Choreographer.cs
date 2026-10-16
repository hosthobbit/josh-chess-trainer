using System.Collections;
using UnityEngine;
using Kingsfall.Chess;

namespace Kingsfall
{
    /// <summary>All piece animation: ordinary moves, the per-piece capture battles, promotion and checkmate.</summary>
    public class Choreographer : MonoBehaviour
    {
        public static Choreographer I { get; private set; }
        public bool Cinematics = true;

        CameraRig Rig => CameraRig.I;
        Fx FX => Fx.I;
        SoundBank Snd => SoundBank.I;
        PostFx Post => PostFx.I;

        static readonly Color Gold = new Color(1f, 0.72f, 0.3f);
        static readonly Color HolyLight = new Color(2.2f, 1.9f, 1.1f);
        static readonly Color Arcane = new Color(0.55f, 0.35f, 2.2f);
        static readonly Color Lava = new Color(2.5f, 0.6f, 0.1f);

        void Awake() { I = this; }

        static Vector3 Flat(Vector3 v) { v.y = 0; return v; }

        Color ImpactColour(PieceView attacker) => attacker.Colour == 0 ? new Color(1f, 0.85f, 0.55f) : new Color(1f, 0.4f, 0.12f);

        // ------------------------------------------------------------ primitives

        IEnumerator Slide(PieceView p, Vector3 to, float dur, float lift = 0.12f, float lean = 8f)
        {
            Vector3 from = p.transform.position;
            Snd.Play(Sfx.Move, 0.5f);
            yield return Tween.Run(dur, k =>
            {
                p.transform.position = Vector3.Lerp(from, to, Ease.InOutCubic(k));
                p.Offset = new Vector3(0, Mathf.Sin(k * Mathf.PI) * lift, 0);
                p.Lean = new Vector3(Mathf.Sin(k * Mathf.PI) * lean * (k < 0.5f ? 1f : 0.3f), 0, 0);
            });
            p.Offset = Vector3.zero;
            p.Lean = Vector3.zero;
        }

        IEnumerator Hop(PieceView p, Vector3 to, float height, float dur, bool dust = true)
        {
            Vector3 from = p.transform.position;
            Snd.Play(Sfx.Whoosh, 0.35f, 1.3f);
            yield return Tween.Run(dur, k => p.transform.position = Tween.Arc(from, to, height, k), Ease.InOutQuad);
            Snd.Play(Sfx.Land, 0.7f);
            if (dust) FX.Dust(to, 0.3f, 14, 0.8f);
            yield return Squish(p, 0.12f);
        }

        IEnumerator Squish(PieceView p, float amount)
        {
            yield return Tween.Run(0.28f, k =>
            {
                float e = Mathf.Sin(k * Mathf.PI * 2.5f) * (1f - k) * amount;
                p.Squash = new Vector3(1f + e * 0.6f, 1f - e, 1f + e * 0.6f);
            });
            p.Squash = Vector3.one;
        }

        IEnumerator Face(PieceView p, Vector3 target, float dur)
        {
            Vector3 d = Flat(target - p.transform.position);
            if (d.sqrMagnitude < 1e-4f) yield break;
            Quaternion from = p.transform.rotation, to = Quaternion.LookRotation(d.normalized);
            yield return Tween.Run(dur, k => p.transform.rotation = Quaternion.Slerp(from, to, k), Ease.InOutQuad);
        }

        IEnumerator LeanTo(PieceView p, Vector3 euler, float dur, System.Func<float, float> ease = null)
        {
            Vector3 from = p.Lean;
            yield return Tween.Run(dur, k => p.Lean = Vector3.LerpUnclamped(from, euler, k), ease ?? Ease.OutQuad);
        }

        IEnumerator Knock(PieceView v, float lean, float push)
        {
            Vector3 l0 = v.Lean, o0 = v.Offset;
            yield return Tween.Run(0.12f, k => { v.Lean = l0 + new Vector3(-lean * k, 0, 0); v.Offset = o0 + new Vector3(0, 0, -push * k); }, Ease.OutQuad);
            Vector3 l1 = v.Lean, o1 = v.Offset;
            yield return Tween.Run(0.45f, k => { v.Lean = Vector3.LerpUnclamped(l1, l0 * 0.6f, Ease.OutElastic(k)); v.Offset = Vector3.Lerp(o1, o0 + new Vector3(0, 0, -push * 0.4f), k); });
        }

        IEnumerator FlashPulse(PieceView v, float peak = 1f, float dur = 0.25f)
        {
            peak *= 0.45f;
            yield return Tween.Run(dur, k => v.Flash = peak * (1f - k) * (1f - k));
            v.Flash = 0;
        }

        void Hit(float trauma, float impact, float slow = 1f, float slowSeconds = 0f)
        {
            Rig.AddTrauma(trauma);
            Post.Impact(impact);
            if (slow < 1f && Cinematics) Post.SlowMo(slow, slowSeconds);
        }

        // ------------------------------------------------------------ camera shots

        /// <summary>How badly other pieces block the view from pos to target (0 = clear).</summary>
        static PieceView extraOccluder;

        static float Occlusion(Vector3 pos, Vector3 target)
        {
            float pen = 0f;
            var list = new System.Collections.Generic.List<PieceView>(BoardView.I.All());
            if (extraOccluder) list.Add(extraOccluder);
            foreach (var p in list)
            {
                if (!p.gameObject.activeInHierarchy) continue;
                Vector3 bottom = p.transform.position + Vector3.up * 0.15f, top = p.transform.position + Vector3.up * p.Height;
                for (int i = 0; i <= 3; i++)
                {
                    Vector3 q = Vector3.Lerp(bottom, top, i / 3f);
                    Vector3 seg = target - pos;
                    float t = Mathf.Clamp01(Vector3.Dot(q - pos, seg) / seg.sqrMagnitude);
                    if (t > 0.97f) continue;
                    float d = (pos + seg * t - q).magnitude;
                    if (d < 0.5f) pen += (0.5f - d) * (1f + t);
                }
            }
            return pen;
        }

        /// <summary>Pick the clearest camera angle around `centre` starting from baseDir.</summary>
        Vector3 BestAngle(Vector3 centre, Vector3 look, Vector3 baseDir, float dist, float height, float[] offsets, bool bothSides)
        {
            Vector3 camNow = Flat(Rig.transform.position - centre);
            float best = float.MaxValue;
            Vector3 bestPos = centre + baseDir * dist + Vector3.up * height;
            for (int s = 0; s < (bothSides ? 2 : 1); s++)
            foreach (float off in offsets)
            foreach (float h in new[] { height, height + 0.8f })
            {
                Vector3 d = Quaternion.Euler(0, off, 0) * (s == 0 ? baseDir : -baseDir);
                Vector3 pos = centre + d * dist + Vector3.up * h;
                float score = Occlusion(pos, look) * 10f;
                score += Mathf.Abs(off) * 0.004f + (h > height ? 0.15f : 0f);
                if (Vector3.Dot(d, camNow.normalized) < 0f) score += 0.35f; // prefer staying on the viewer's side
                if (Flat(pos).magnitude > 10.5f) score += 5f;              // keep inside the pillar ring
                if (score < best) { best = score; bestPos = pos; }
            }
            return bestPos;
        }

        static readonly float[] SideOffsets = { 0f, -25f, 25f, -50f, 50f, -75f, 75f };
        static readonly float[] ShoulderOffsets = { 45f, -45f, 65f, -65f, 30f, -30f, 85f, -85f };

        void SideShot(Vector3 a, Vector3 b, float extra = 0f, float height = 1.7f)
        {
            if (!Cinematics) return;
            Vector3 mid = (a + b) * 0.5f;
            Vector3 dir = Flat(b - a);
            if (dir.sqrMagnitude < 1e-4f) dir = Vector3.forward;
            dir.Normalize();
            Vector3 side = Vector3.Cross(Vector3.up, dir);
            float span = Flat(b - a).magnitude;
            Vector3 look = mid + Vector3.up * 0.55f;
            Vector3 pos = BestAngle(mid, look, side, 3.0f + span * 0.45f + extra, height, SideOffsets, true);
            Rig.Cinematic(pos, look, 36f, 3f);
            Post.CinematicFocus(true);
        }

        PieceView shoulderPiece;

        void ShoulderShot(Vector3 attacker, Vector3 victim)
        {
            if (!Cinematics) return;
            Vector3 dir = Flat(victim - attacker).normalized;
            Vector3 look = Vector3.Lerp(attacker, victim, 0.7f) + Vector3.up * 0.6f;
            extraOccluder = shoulderPiece;
            Vector3 pos = BestAngle(attacker, look, -dir, 2.6f, 2.3f, ShoulderOffsets, false);
            extraOccluder = null;
            Rig.Cinematic(pos, look, 40f, 2.5f);
            Post.CinematicFocus(true);
        }

        void CloseShot(Vector3 focus, Vector3 fromDir, float dist = 2.6f, float height = 1.2f, float fov = 34f)
        {
            if (!Cinematics) return;
            Vector3 look = focus + Vector3.up * 0.45f;
            Vector3 pos = BestAngle(focus, look, Flat(fromDir).normalized, dist, height, SideOffsets, false);
            Rig.Cinematic(pos, look, fov, 2.5f);
            Post.CinematicFocus(true);
        }

        void EndShot()
        {
            Rig.EndCinematic();
            Post.CinematicFocus(false);
        }

        // ------------------------------------------------------------ ordinary moves

        public IEnumerator MovePiece(PieceView p, int to)
        {
            Vector3 dest = BoardView.SquareToWorld(to);
            float dist = Flat(dest - p.transform.position).magnitude;
            if (p.Type == PieceType.Knight)
            {
                yield return LeanTo(p, new Vector3(-12, 0, 0), 0.12f);
                Vector3 from = p.transform.position;
                Snd.Play(Sfx.Whoosh, 0.4f);
                yield return Tween.Run(0.5f, k =>
                {
                    p.transform.position = Tween.Arc(from, dest, 0.9f, k);
                    p.Lean = new Vector3(Mathf.Lerp(-12f, 14f, k), 0, 0);
                }, Ease.InOutQuad);
                p.Lean = Vector3.zero;
                Snd.Play(Sfx.Land, 0.8f);
                FX.Dust(dest, 0.3f, 16, 0.9f);
                yield return Squish(p, 0.14f);
            }
            else
            {
                yield return Slide(p, dest, 0.32f + dist * 0.07f, 0.1f + dist * 0.02f);
                Snd.Play(Sfx.Land, 0.45f, 1.15f);
                yield return Squish(p, 0.06f);
            }
            p.Square = to;
        }

        public IEnumerator Castle(PieceView king, PieceView rook, int kingTo, int rookTo)
        {
            Vector3 kd = BoardView.SquareToWorld(kingTo), rd = BoardView.SquareToWorld(rookTo);
            Vector3 rFrom = rook.transform.position;
            StartCoroutine(Slide(king, kd, 0.55f));
            yield return Tween.Wait(0.15f);
            Snd.Play(Sfx.Whoosh, 0.4f);
            yield return Tween.Run(0.6f, k =>
            {
                rook.transform.position = Tween.Arc(rFrom, rd, 1.6f, k);
                rook.Lean = new Vector3(0, 0, Mathf.Sin(k * Mathf.PI * 2f) * 25f);
                rook.transform.rotation = Quaternion.LookRotation(rook.Forward) * Quaternion.Euler(0, k * 360f, 0);
            }, Ease.InOutQuad);
            rook.Lean = Vector3.zero;
            rook.transform.rotation = Quaternion.LookRotation(rook.Forward);
            Snd.Play(Sfx.Land, 0.9f);
            FX.Dust(rd, 0.35f, 20, 1f);
            FX.Embers(rd + Vector3.up * 0.4f, 20, Gold);
            yield return Squish(rook, 0.15f);
            king.Square = kingTo; rook.Square = rookTo;
        }

        public IEnumerator Promote(PieceView pawn, PieceType newType, System.Action<PieceView> spawned)
        {
            Vector3 pos = pawn.transform.position;
            CloseShot(pos, -pawn.Forward + Vector3.right * 0.4f, 3.2f, 1.4f, 38f);
            Snd.Play(Sfx.Promote, 0.9f);
            StartCoroutine(FX.LightPillar(pos, HolyLight * 0.25f, 2.2f, 0.45f));
            var soul = FX.SoulStream(pawn, new Color(1.6f, 1.2f, 0.5f), 1.2f);
            yield return Tween.Run(0.9f, k => { pawn.Dissolve = k; pawn.Offset = new Vector3(0, k * 0.3f, 0); });
            soul.Stop();
            pawn.gameObject.SetActive(false);
            var np = BoardView.I.Spawn(newType, pawn.Colour, pawn.Square);
            np.Dissolve = 1f;
            spawned?.Invoke(np);
            FX.Embers(pos + Vector3.up * 0.6f, 60, Gold, 0.4f);
            FX.Shockwave(pos, 3f, Gold * 2f, 0.8f);
            yield return Tween.Run(0.9f, k => { np.Dissolve = 1f - k; np.Glow = (1f - k) * 3f; np.Offset = new Vector3(0, (1f - k) * 0.25f, 0); }, Ease.OutQuad);
            np.Dissolve = 0; np.Glow = 0; np.Offset = Vector3.zero;
            Destroy(pawn.gameObject);
            yield return Tween.Wait(0.3f);
            EndShot();
        }

        // ------------------------------------------------------------ captures

        /// <summary>Attacker defeats victim, finishing on square `to`.</summary>
        public IEnumerator Capture(PieceView attacker, PieceView victim, int to)
        {
            Vector3 a = attacker.transform.position;
            Vector3 v = victim.transform.position;

            switch (attacker.Type)
            {
                case PieceType.Pawn: yield return PawnBash(attacker, victim); break;
                case PieceType.Knight: yield return KnightStomp(attacker, victim); break;
                case PieceType.Bishop: yield return BishopSmite(attacker, victim); break;
                case PieceType.Rook: yield return RookRam(attacker, victim); break;
                case PieceType.Queen: yield return QueenBarrage(attacker, victim); break;
                default: yield return KingJudgement(attacker, victim); break;
            }

            // Finish on the destination square and face forward again.
            Vector3 dest = BoardView.SquareToWorld(to);
            if (Flat(dest - attacker.transform.position).sqrMagnitude > 0.01f)
                yield return Slide(attacker, dest, 0.35f, 0.08f);
            attacker.ResetAnim();
            yield return Face(attacker, attacker.transform.position + attacker.Forward, 0.25f);
            attacker.Square = to;
            yield return Tween.Wait(0.15f);
            EndShot();
        }

        Vector3 Stage(Vector3 a, Vector3 v, float gap)
        {
            Vector3 dir = Flat(v - a).normalized;
            float dist = Flat(v - a).magnitude;
            return dist > gap ? v - dir * gap : a;
        }

        // Pawn: hop up close, wind up, two headbutts — the second one shatters.
        IEnumerator PawnBash(PieceView p, PieceView v)
        {
            Vector3 vpos = v.transform.position;
            Vector3 dir = Flat(vpos - p.transform.position).normalized;
            SideShot(p.transform.position, vpos);
            v.Tremble(0.25f);
            StartCoroutine(Face(v, p.transform.position, 0.3f));
            yield return Face(p, vpos, 0.2f);
            Vector3 stage = vpos - dir * 0.78f;
            yield return Hop(p, stage, 0.3f, 0.32f);

            for (int blow = 0; blow < 2; blow++)
            {
                bool final = blow == 1;
                // wind up
                yield return Tween.Run(final ? 0.38f : 0.26f, k =>
                {
                    p.Lean = new Vector3(-28f * k, 0, 0);
                    p.Squash = new Vector3(1f + 0.1f * k, 1f - 0.12f * k, 1f + 0.1f * k);
                    p.Offset = new Vector3(0, 0, -0.12f * k);
                }, Ease.OutQuad);
                Snd.Play(Sfx.Whoosh, 0.5f, 1.4f);
                // lunge
                yield return Tween.Run(0.085f, k =>
                {
                    p.Lean = new Vector3(Mathf.Lerp(-28f, 38f, k), 0, 0);
                    p.Squash = Vector3.Lerp(new Vector3(1.1f, 0.88f, 1.1f), new Vector3(0.94f, 1.08f, 0.94f), k);
                    p.Offset = new Vector3(0, 0.04f * k, Mathf.Lerp(-0.12f, 0.3f, k));
                }, Ease.InQuad);

                Vector3 contact = vpos - dir * 0.28f + Vector3.up * 0.55f;
                FX.Sparks(contact, -dir + Vector3.up * 0.4f, final ? 90 : 45, final ? 8f : 5f, ImpactColour(p));
                FX.FlashLight(contact, ImpactColour(p), final ? 30f : 12f, 5f, 0.25f);
                if (!final)
                {
                    Snd.Play(Sfx.Impact, 0.8f, 1.2f);
                    Hit(0.35f, 0.35f, 0.25f, 0.18f);
                    v.Crack = 0.45f;
                    StartCoroutine(FlashPulse(v, 0.8f));
                    StartCoroutine(Knock(v, 22f, 0.12f));
                }
                else
                {
                    Snd.Play(Sfx.Impact, 1f);
                    Snd.Play(Sfx.Shatter, 0.9f);
                    Hit(0.65f, 0.7f, 0.18f, 0.55f);
                    v.Tremble(0);
                    FX.Shatter(v, contact, dir * 3.2f, 2.2f, 1.8f);
                    FX.Dust(vpos, 0.45f, 40, 1.8f);
                    FX.Embers(vpos + Vector3.up * 0.4f, 30, ImpactColour(p));
                }
                // recoil
                yield return Tween.Run(0.25f, k =>
                {
                    p.Lean = new Vector3(Mathf.Lerp(38f, 0f, Ease.OutBack(k)), 0, 0);
                    p.Squash = Vector3.Lerp(new Vector3(0.94f, 1.08f, 0.94f), Vector3.one, k);
                    p.Offset = new Vector3(0, 0, Mathf.Lerp(0.3f, 0f, k));
                });
                if (!final) yield return Tween.Wait(0.18f);
            }
            yield return Tween.Wait(0.4f);
            // a proud little bounce
            yield return Tween.Run(0.3f, k => p.Offset = new Vector3(0, Mathf.Sin(k * Mathf.PI) * 0.15f, 0));
        }

        // Knight: rear up, leap high, stomp the victim into the ground.
        IEnumerator KnightStomp(PieceView n, PieceView v)
        {
            Vector3 vpos = v.transform.position;
            Vector3 start = n.transform.position;
            Vector3 dir = Flat(vpos - start).normalized;
            SideShot(start, vpos, 0.6f, 2.2f);
            v.Tremble(0.3f);
            StartCoroutine(Face(v, start, 0.3f));
            yield return Face(n, vpos, 0.25f);

            Snd.Play(Sfx.Charge, 0.7f, 1.2f);
            yield return Tween.Run(0.45f, k =>
            {
                n.Lean = new Vector3(-35f * k, 0, 0);
                n.Squash = new Vector3(1f, 1f - 0.12f * k, 1f);
            }, Ease.OutQuad);
            yield return Tween.Wait(0.1f);

            Snd.Play(Sfx.Whoosh, 0.7f, 0.8f);
            FX.Dust(start, 0.35f, 20, 1.2f);
            float apex = 2.6f;
            yield return Tween.Run(0.62f, k =>
            {
                Vector3 p = Vector3.Lerp(start, vpos, Ease.InOutQuad(k));
                // parabola that ends standing on top of the victim
                p.y = Mathf.Lerp(0f, 0.55f, k) + 4f * apex * k * (1f - k);
                n.transform.position = p;
                n.Lean = new Vector3(Mathf.Lerp(-35f, 30f, k), 0, 0);
                n.Squash = Vector3.Lerp(new Vector3(1f, 0.88f, 1f), new Vector3(0.92f, 1.12f, 0.92f), k);
                if (k > 0.45f) v.Squash = new Vector3(1f, 1f - (k - 0.45f) * 0.25f, 1f);
            }, Ease.Linear);

            // IMPACT
            v.Tremble(0);
            Snd.Play(Sfx.Impact, 1f, 0.8f);
            Snd.Play(Sfx.Shatter, 1f, 0.9f);
            Hit(0.85f, 0.9f, 0.15f, 0.7f);
            Post.ExposureKick(0.25f);
            v.Squash = new Vector3(1.35f, 0.3f, 1.35f);
            FX.Shatter(v, vpos + Vector3.up * 0.05f, dir * 0.5f, 4.2f, 2.2f, 10f);
            FX.Shockwave(vpos, 4.5f, ImpactColour(n) * 1.3f, 0.7f);
            FX.Dust(vpos, 0.5f, 70, 2.8f);
            FX.Sparks(vpos + Vector3.up * 0.1f, Vector3.up, 80, 6f, ImpactColour(n), 80f);
            FX.FlashLight(vpos + Vector3.up * 0.5f, ImpactColour(n), 50f, 7f, 0.4f);
            // land on the square
            Vector3 top = n.transform.position;
            yield return Tween.Run(0.12f, k => n.transform.position = Vector3.Lerp(top, vpos, k), Ease.InQuad);
            Snd.Play(Sfx.Land, 1f, 0.8f);
            yield return Tween.Run(0.5f, k =>
            {
                n.Lean = new Vector3(Mathf.Lerp(30f, 0f, Ease.OutElastic(k)), 0, 0);
                float e = Mathf.Sin(k * Mathf.PI * 3f) * (1f - k) * 0.22f;
                n.Squash = new Vector3(1f + e, 1f - e, 1f + e);
            });
            yield return Tween.Wait(0.55f);
        }

        // Bishop: rise in holy light, call lightning down, the victim burns away into embers.
        IEnumerator BishopSmite(PieceView b, PieceView v)
        {
            Vector3 vpos = v.transform.position;
            Vector3 start = b.transform.position;
            shoulderPiece = b;
            ShoulderShot(start, vpos);
            StartCoroutine(Face(v, start, 0.3f));
            yield return Face(b, vpos, 0.25f);

            Color holy = b.Colour == 0 ? HolyLight : new Color(2.4f, 0.5f, 1.4f);
            b.SetGlowColour(holy);
            Snd.Play(Sfx.Magic, 0.8f, 0.8f);
            var aura = FX.SoulStream(b, holy * 0.6f, 1.6f);
            yield return Tween.Run(0.7f, k => { b.Offset = new Vector3(0, 0.45f * Ease.OutCubic(k), 0); b.Glow = 2.5f * k; b.Lean = new Vector3(-12f * k, 0, 0); });
            v.Tremble(0.4f);

            if (Cinematics) CloseShot(vpos, Flat(start - vpos) + Vector3.Cross(Vector3.up, Flat(vpos - start)) * 0.6f, 3.6f, 1.6f, 42f);
            Snd.Play(Sfx.Zap, 1f);
            Hit(0.5f, 0.6f, 0.4f, 0.4f);
            Post.ExposureKick(0.25f);
            StartCoroutine(FX.LightPillar(vpos, holy * 0.22f, 1.8f, 0.42f));
            StartCoroutine(FX.Lightning(b.Body.position + Vector3.up * (b.Height + 0.1f), vpos + Vector3.up * 0.7f, 0.8f, holy * 1.4f, 0.07f, 2));
            yield return FX.Lightning(vpos + Vector3.up * 12f, vpos + Vector3.up * (v.Height - 0.05f), 0.85f, holy * 1.6f, 0.16f, 4);
            FX.Shockwave(vpos, 3f, holy, 0.6f);
            FX.Sparks(vpos + Vector3.up * v.Height, Vector3.up, 70, 5f, holy, 80f);

            v.Tremble(0.15f);
            v.SetGlowColour(holy);
            Snd.Play(Sfx.Crumble, 1f);
            var soul = FX.SoulStream(v, holy, 1.5f);
            yield return Tween.Run(1.3f, k => { v.Dissolve = Mathf.Lerp(0.05f, 1f, k); v.Glow = 2f * (1f - k); v.Crack = k; v.Offset = new Vector3(0, k * 0.15f, 0); }, Ease.InOutQuad);
            soul.Stop();
            v.gameObject.SetActive(false);
            FX.Embers(vpos + Vector3.up * 0.5f, 50, holy * 0.8f, 0.35f, 2f);

            aura.Stop();
            yield return Tween.Run(0.45f, k => { b.Offset = new Vector3(0, 0.45f * (1f - k), 0); b.Glow = 2.5f * (1f - k); b.Lean = new Vector3(-12f * (1f - k), 0, 0); });
            b.SetGlowColour(new Color(1.2f, 0.9f, 0.3f));
            yield return Slide(b, vpos, 0.3f + Flat(vpos - start).magnitude * 0.06f, 0.1f);
        }

        // Rook: back up, charge, launch the victim across the hall where it smashes to pieces.
        IEnumerator RookRam(PieceView r, PieceView v)
        {
            Vector3 vpos = v.transform.position;
            Vector3 start = r.transform.position;
            Vector3 dir = Flat(vpos - start).normalized;
            SideShot(start, vpos, 1.2f, 1.9f);
            v.Tremble(0.25f);
            StartCoroutine(Face(v, start, 0.3f));
            yield return Face(r, vpos, 0.2f);

            float dist = Flat(vpos - start).magnitude;
            Vector3 stage = dist > 2.2f ? vpos - dir * 2.2f : start;
            if (stage != start) yield return Slide(r, stage, 0.25f + dist * 0.05f, 0.05f, 4f);
            Vector3 back = stage - dir * 0.45f;

            Snd.Play(Sfx.Charge, 0.9f);
            Vector3 s0 = r.transform.position;
            yield return Tween.Run(0.45f, k =>
            {
                r.transform.position = Vector3.Lerp(s0, back, Ease.OutQuad(k));
                r.Lean = new Vector3(-14f * k, 0, 0);
                r.Tremble(0.15f * k);
            });
            FX.Dust(back, 0.3f, 14, 1f);
            r.Tremble(0);
            Vector3 contact = vpos - dir * 0.6f;
            Snd.Play(Sfx.Whoosh, 0.8f, 0.7f);
            yield return Tween.Run(0.2f, k =>
            {
                r.transform.position = Vector3.Lerp(back, contact, Ease.InCubic(k));
                r.Lean = new Vector3(Mathf.Lerp(-14f, 16f, k), 0, 0);
            });

            // IMPACT — launch!
            v.Tremble(0);
            Snd.Play(Sfx.Impact, 1f, 0.75f);
            Snd.Play(Sfx.Clank, 0.7f, 0.8f);
            Hit(0.8f, 0.8f, 0.22f, 0.45f);
            FX.Sparks(vpos - dir * 0.3f + Vector3.up * 0.5f, dir + Vector3.up * 0.3f, 100, 9f, ImpactColour(r));
            FX.FlashLight(vpos + Vector3.up * 0.5f, ImpactColour(r), 30f, 6f, 0.3f);
            FX.Dust(vpos, 0.4f, 30, 2f);
            StartCoroutine(FlashPulse(v, 1f, 0.3f));
            v.Crack = 0.7f;

            var rb = v.gameObject.AddComponent<Rigidbody>();
            var col = v.gameObject.AddComponent<CapsuleCollider>();
            col.center = new Vector3(0, v.Height * 0.5f, 0); col.height = v.Height; col.radius = 0.3f;
            rb.mass = 2f;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.linearVelocity = dir * 8.5f + Vector3.up * 4.2f;
            rb.angularVelocity = Vector3.Cross(Vector3.up, dir) * 9f + Random.insideUnitSphere * 2f;

            // Follow the flying piece briefly with the camera
            if (Cinematics) Rig.Cinematic(Rig.transform.position + Vector3.up * 0.4f, vpos + dir * 2f + Vector3.up * 1f, 44f, 3f);

            yield return Tween.Run(0.35f, k => r.Lean = new Vector3(Mathf.Lerp(16f, 0f, Ease.OutElastic(k)), 0, 0));
            float flight = 0f;
            while (flight < 1.1f)
            {
                if (Cinematics) Rig.Cinematic(Rig.transform.position, v.transform.position + Vector3.up * 0.4f, 44f, 4f);
                yield return null;
                flight += Time.deltaTime;
                if (flight > 0.45f && (v.transform.position.y < -0.55f || rb.linearVelocity.y < -2f && v.transform.position.y < 0.5f)) break;
            }
            Vector3 crash = v.transform.position;
            Snd.Play(Sfx.Shatter, 1f, 0.85f);
            Snd.Play(Sfx.Impact, 0.7f, 0.9f);
            Hit(0.5f, 0.6f);
            FX.Shatter(v, crash - dir * 0.2f - Vector3.up * 0.2f, rb.linearVelocity * 0.35f, 3f, 2f, 10f);
            FX.Dust(new Vector3(crash.x, Mathf.Max(crash.y - 0.3f, -0.85f), crash.z), 0.5f, 40, 2f);
            FX.Embers(crash, 30, ImpactColour(r));
            Destroy(rb);
            yield return Tween.Wait(0.5f);
        }

        // Queen: levitate, spin, unleash an arcane barrage, then a final colossal orb.
        IEnumerator QueenBarrage(PieceView q, PieceView v)
        {
            Vector3 vpos = v.transform.position;
            Vector3 start = q.transform.position;
            Vector3 dir = Flat(vpos - start).normalized;
            Vector3 side = Vector3.Cross(Vector3.up, dir);
            shoulderPiece = q;
            ShoulderShot(start, vpos);
            StartCoroutine(Face(v, start, 0.3f));
            yield return Face(q, vpos, 0.2f);

            Color power = q.Colour == 0 ? new Color(0.6f, 0.9f, 2.6f) : new Color(2.4f, 0.3f, 1.6f);
            q.SetGlowColour(power);
            Snd.Play(Sfx.Magic, 0.9f, 0.7f);
            var aura = FX.SoulStream(q, power * 0.5f, 3f);
            yield return Tween.Run(0.8f, k =>
            {
                q.Offset = new Vector3(0, 0.6f * Ease.OutCubic(k), 0);
                q.Lean = new Vector3(0, 360f * Ease.InOutCubic(k), 0);
                q.Glow = 2.2f * k;
            });
            v.Tremble(0.45f);

            Vector3 muzzle = q.Body.position + Vector3.up * (q.Height * 0.85f);
            Vector3 target = vpos + Vector3.up * (v.Height * 0.55f);
            for (int i = 0; i < 3; i++)
            {
                float s = i == 1 ? -1f : 1f;
                Snd.Play(Sfx.Magic, 0.5f, 1.6f + i * 0.15f);
                yield return FX.Orb(muzzle, target, side * s * 1.3f + Vector3.up * (0.8f + i * 0.3f), 0.4f, power, 0.12f);
                Snd.Play(Sfx.Impact, 0.6f, 1.4f);
                FX.Sparks(target, Flat(target - muzzle).normalized, 50, 5f, power * 0.8f);
                FX.FlashLight(target, power, 18f, 4f, 0.2f);
                Hit(0.25f, 0.3f);
                v.Crack = Mathf.Min(1f, v.Crack + 0.33f);
                StartCoroutine(FlashPulse(v, 0.7f, 0.2f));
                StartCoroutine(Knock(v, 12f, 0.05f));
            }

            // final charge
            Snd.Play(Sfx.Charge, 0.6f, 1.6f);
            yield return Tween.Run(0.45f, k => { q.Glow = 2.2f + 2f * k; q.Lean = new Vector3(-15f * k, 360f, 0); });
            if (Cinematics) SideShot(start, vpos, 1.0f, 2.0f);
            yield return FX.Orb(muzzle, target, Vector3.up * 1.4f, 0.55f, power * 1.6f, 0.32f);

            // EXPLOSION
            v.Tremble(0);
            Snd.Play(Sfx.Blast, 1f);
            Snd.Play(Sfx.Shatter, 0.8f, 1.1f);
            Hit(1f, 1f, 0.15f, 0.8f);
            Post.ExposureKick(0.8f);
            FX.Shatter(v, target - dir * 0.15f, dir * 2.5f + Vector3.up * 1f, 5.5f, 3f, 14f);
            FX.FlashLight(target, power * 1.2f, 80f, 9f, 0.6f);
            FX.Shockwave(vpos, 5f, power * 1.5f, 0.8f);
            FX.Sparks(target, Vector3.up, 120, 10f, power, 90f);
            FX.Embers(target, 60, power, 0.4f, 1.2f);
            FX.Smoke(target, 16, 1.0f);
            FX.Dust(vpos, 0.5f, 40, 2.5f);

            aura.Stop();
            yield return Tween.Run(0.6f, k => { q.Offset = new Vector3(0, 0.6f * (1f - Ease.InOutQuad(k)), 0); q.Glow = 4.2f * (1f - k); q.Lean = new Vector3(-15f * (1f - k), 360f, 0); });
            q.Lean = Vector3.zero;
            q.SetGlowColour(new Color(1.2f, 0.9f, 0.3f));
            yield return Tween.Wait(0.3f);
            yield return Slide(q, vpos, 0.3f + Flat(vpos - start).magnitude * 0.06f, 0.12f);
        }

        // King: walk up and hammer the victim with royal head-blows, then a crushing body-slam.
        IEnumerator KingJudgement(PieceView k, PieceView v)
        {
            Vector3 vpos = v.transform.position;
            Vector3 start = k.transform.position;
            Vector3 dir = Flat(vpos - start).normalized;
            SideShot(start, vpos, 0.4f, 1.9f);
            v.Tremble(0.35f);
            StartCoroutine(Face(v, start, 0.3f));
            yield return Face(k, vpos, 0.25f);

            Vector3 stage = vpos - dir * 0.9f;
            if (Flat(stage - start).magnitude > 0.05f)
            {
                Vector3 mid = Vector3.Lerp(start, stage, 0.5f);
                yield return Hop(k, mid, 0.18f, 0.22f, false);
                yield return Hop(k, stage, 0.18f, 0.22f, false);
            }

            for (int blow = 0; blow < 3; blow++)
            {
                yield return Tween.Run(0.2f, t => k.Lean = new Vector3(-22f * t, 0, 0), Ease.OutQuad);
                Snd.Play(Sfx.Whoosh, 0.5f, 1.1f);
                yield return Tween.Run(0.08f, t => k.Lean = new Vector3(Mathf.Lerp(-22f, 34f, t), 0, 0), Ease.InQuad);
                Vector3 contact = vpos + Vector3.up * (v.Height * v.Squash.y * 0.9f) - dir * 0.15f;
                Snd.Play(Sfx.Clank, 0.9f, 0.9f - blow * 0.1f);
                Snd.Play(Sfx.Impact, 0.7f, 1.2f - blow * 0.1f);
                FX.Sparks(contact, Vector3.up + dir * 0.5f, 50 + blow * 20, 6f, ImpactColour(k));
                FX.FlashLight(contact, ImpactColour(k), 15f + blow * 8f, 4f, 0.2f);
                Hit(0.3f + blow * 0.12f, 0.3f + blow * 0.15f, blow == 2 ? 0.3f : 1f, 0.25f);
                v.Crack = Mathf.Min(1f, v.Crack + 0.34f);
                float sy = 1f - (blow + 1) * 0.13f;
                StartCoroutine(FlashPulse(v, 0.8f));
                StartCoroutine(Tween.Run(0.15f, t => v.Squash = Vector3.Lerp(v.Squash, new Vector3(1f + (1f - sy) * 0.6f, sy, 1f + (1f - sy) * 0.6f), t), Ease.OutQuad));
                StartCoroutine(Knock(v, 8f + blow * 4f, 0.03f));
                yield return Tween.Run(0.22f, t => k.Lean = new Vector3(Mathf.Lerp(34f, 0f, Ease.OutBack(t)), 0, 0));
                yield return Tween.Wait(0.12f);
            }

            // body slam
            Snd.Play(Sfx.Charge, 0.6f, 0.9f);
            Vector3 s0 = k.transform.position;
            yield return Tween.Run(0.35f, t => { k.Offset = new Vector3(0, 0.5f * Ease.OutQuad(t), 0); k.Lean = new Vector3(-18f * t, 0, 0); });
            yield return Tween.Run(0.16f, t =>
            {
                k.transform.position = Vector3.Lerp(s0, vpos - dir * 0.2f, t);
                k.Offset = new Vector3(0, 0.5f * (1f - t), 0);
                k.Lean = new Vector3(Mathf.Lerp(-18f, 55f, t), 0, 0);
            }, Ease.InQuad);

            v.Tremble(0);
            Snd.Play(Sfx.Impact, 1f, 0.7f);
            Snd.Play(Sfx.Shatter, 1f, 0.8f);
            Hit(0.9f, 0.9f, 0.15f, 0.7f);
            Post.ExposureKick(0.4f);
            v.Squash = new Vector3(1.4f, 0.25f, 1.4f);
            FX.Shatter(v, vpos - dir * 0.2f + Vector3.up * 0.2f, dir * 2f, 3.8f, 2f, 11f);
            FX.Shockwave(vpos, 4f, ImpactColour(k) * 2f, 0.7f);
            FX.Dust(vpos, 0.5f, 60, 2.6f);
            FX.Sparks(vpos + Vector3.up * 0.3f, Vector3.up, 90, 7f, ImpactColour(k), 85f);
            FX.FlashLight(vpos + Vector3.up * 0.4f, ImpactColour(k), 45f, 7f, 0.4f);

            Vector3 s1 = k.transform.position;
            yield return Tween.Run(0.6f, t =>
            {
                k.transform.position = Vector3.Lerp(s1, vpos, Ease.OutQuad(t));
                k.Lean = new Vector3(Mathf.Lerp(55f, 0f, Ease.OutElastic(t)), 0, 0);
            });
            yield return Tween.Wait(0.4f);
        }

        // ------------------------------------------------------------ checkmate

        public IEnumerator Checkmate(PieceView king, PieceView attacker)
        {
            Vector3 kp = king.transform.position;
            Vector3 fromDir = attacker ? Flat(kp - attacker.transform.position).normalized : -king.Forward;
            if (fromDir.sqrMagnitude < 0.01f) fromDir = -king.Forward;
            CloseShot(kp, -fromDir + Vector3.Cross(Vector3.up, fromDir) * 0.8f, 3.6f, 1.5f, 38f);
            Snd.Play(Sfx.Check, 1f, 0.6f, 0f);
            king.SetGlowColour(Lava * 1.5f);
            king.Tremble(0.5f);
            yield return Tween.Run(1.2f, t => { king.Glow = 3f * t; king.Crack = t; }, Ease.InQuad);
            king.Tremble(0);
            // topple
            Snd.Play(Sfx.Whoosh, 0.6f, 0.6f);
            Quaternion r0 = king.transform.rotation;
            Quaternion r1 = Quaternion.LookRotation(fromDir);
            king.transform.rotation = r1;
            king.Lean = Vector3.zero;
            yield return Tween.Run(0.9f, t => king.Lean = new Vector3(88f * t, 0, 0), Ease.OutBounce);
            Snd.Play(Sfx.Impact, 1f, 0.6f);
            Snd.Play(Sfx.Gong, 1f, 1f, 0f);
            Hit(0.8f, 0.9f, 0.3f, 1.2f);
            Post.ExposureKick(0.6f);
            FX.Dust(kp + fromDir * 0.7f, 0.6f, 60, 2f);
            yield return Tween.Wait(0.6f);
            Snd.Play(Sfx.Shatter, 1f, 0.7f);
            FX.Shatter(king, kp + Vector3.up * 0.2f, Vector3.up * 0.5f, 2.5f, 3f, 8f, 10f);
            FX.Embers(kp + Vector3.up * 0.3f, 90, Lava, 0.6f, 1.2f);
            FX.Shockwave(kp, 6f, Lava, 1f);
            FX.FlashLight(kp + Vector3.up * 0.5f, Lava, 60f, 8f, 0.8f);
            yield return Tween.Wait(1.6f);
            // slow victory orbit
            if (Cinematics)
            {
                float t = 0;
                while (t < 3.5f)
                {
                    float ang = t * 0.35f;
                    Vector3 d = Quaternion.Euler(0, ang * Mathf.Rad2Deg, 0) * (-fromDir);
                    Rig.Cinematic(kp + d * 5f + Vector3.up * 2.6f, kp + Vector3.up * 0.3f, 40f, 2f);
                    yield return null;
                    t += Time.unscaledDeltaTime;
                }
            }
            EndShot();
        }
    }
}

using UnityEngine;
using Kingsfall.Chess;

namespace Kingsfall
{
    /// <summary>
    /// A chess piece in the world. The root sits on the square; "Body" carries all
    /// animation offsets (lean, squash, hop) so the logical position stays clean.
    /// </summary>
    public class PieceView : MonoBehaviour
    {
        public PieceType Type;
        public int Colour;
        public int Square;

        public Transform Body;
        public MeshRenderer Renderer;
        MaterialPropertyBlock block;

        public float Flash, Glow, Dissolve, Crack;
        float shownFlash = -1, shownGlow = -1, shownDissolve = -1, shownCrack = -1;
        Color glowColour = new Color(1.2f, 0.9f, 0.3f);
        Color shownGlowColour;

        // Animation channels (local to Body).
        public Vector3 Offset;
        public Vector3 Lean;          // euler degrees
        public Vector3 Squash = Vector3.one;
        public float Hover;           // selection lift
        float hoverTarget;
        float trembleAmount;
        float idleSeed;

        public Vector3 Forward => Colour == Board.White ? Vector3.forward : Vector3.back;

        public static PieceView Create(PieceType type, int colour, int square, Transform parent)
        {
            var lib = KingsfallLibrary.I;
            var go = new GameObject($"{(colour == 0 ? "White" : "Black")} {type}");
            go.transform.SetParent(parent, false);
            var pv = go.AddComponent<PieceView>();
            pv.Type = type;
            pv.Colour = colour;
            pv.Square = square;

            var body = new GameObject("Body");
            body.transform.SetParent(go.transform, false);
            pv.Body = body.transform;
            body.AddComponent<MeshFilter>().sharedMesh = lib.pieceMeshes[(int)type];
            pv.Renderer = body.AddComponent<MeshRenderer>();
            pv.Renderer.sharedMaterial = colour == 0 ? lib.whitePiece : lib.blackPiece;
            pv.Renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            pv.Renderer.receiveShadows = true;
            pv.Renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.BlendProbes;

            pv.block = new MaterialPropertyBlock();
            pv.idleSeed = Random.value * 100f;
            // Each piece gets its own marble so the set doesn't look cloned.
            pv.block.SetVector("_NoiseOffset", new Vector4(Random.value * 50f, Random.value * 50f, Random.value * 50f, 0));
            pv.Renderer.SetPropertyBlock(pv.block);

            go.transform.position = BoardView.SquareToWorld(square);
            go.transform.rotation = Quaternion.LookRotation(pv.Forward);
            return pv;
        }

        public void SetHover(bool on) => hoverTarget = on ? 0.12f : 0f;
        public void SetGlowColour(Color c) => glowColour = c;
        public void Tremble(float amount) => trembleAmount = amount;

        public void ResetAnim()
        {
            Offset = Vector3.zero; Lean = Vector3.zero; Squash = Vector3.one;
            Flash = 0; Dissolve = 0; Crack = 0; trembleAmount = 0;
        }

        void LateUpdate()
        {
            Hover = Mathf.Lerp(Hover, hoverTarget, 1f - Mathf.Exp(-Time.unscaledDeltaTime * 12f));
            Vector3 tremble = Vector3.zero;
            if (trembleAmount > 0f)
            {
                float t = Time.time * 40f + idleSeed;
                tremble = new Vector3(Mathf.PerlinNoise(t, 0) - 0.5f, 0, Mathf.PerlinNoise(0, t) - 0.5f) * trembleAmount * 0.12f;
            }
            Body.localPosition = Offset + new Vector3(0, Hover, 0) + transform.InverseTransformVector(tremble);
            // Hovered pieces breathe slightly.
            float bob = Hover > 0.01f ? Mathf.Sin(Time.unscaledTime * 3f + idleSeed) * 0.012f : 0f;
            Body.localPosition += new Vector3(0, bob, 0);
            Body.localRotation = Quaternion.Euler(Lean + (trembleAmount > 0 ? new Vector3(tremble.z, 0, -tremble.x) * 60f : Vector3.zero));
            Body.localScale = Squash;

            if (block == null) return;
            if (Mathf.Abs(shownFlash - Flash) > 0.002f || Mathf.Abs(shownGlow - Glow) > 0.002f ||
                Mathf.Abs(shownDissolve - Dissolve) > 0.001f || Mathf.Abs(shownCrack - Crack) > 0.002f || shownGlowColour != glowColour)
            {
                block.SetFloat("_Flash", Flash);
                block.SetFloat("_Glow", Glow);
                block.SetFloat("_Dissolve", Dissolve);
                block.SetFloat("_Crack", Crack);
                block.SetColor("_GlowColor", glowColour);
                Renderer.SetPropertyBlock(block);
                shownFlash = Flash; shownGlow = Glow; shownDissolve = Dissolve; shownCrack = Crack; shownGlowColour = glowColour;
            }
        }

        public Vector4 NoiseOffset => block.GetVector("_NoiseOffset");

        public Vector3 BodyCentre => Body.position + Vector3.up * Height * 0.5f;

        public float Height
        {
            get
            {
                switch (Type)
                {
                    case PieceType.Pawn: return 0.77f;
                    case PieceType.Knight: return 0.9f;
                    case PieceType.Bishop: return 1.1f;
                    case PieceType.Rook: return 0.9f;
                    case PieceType.Queen: return 1.3f;
                    default: return 1.47f;
                }
            }
        }
    }
}

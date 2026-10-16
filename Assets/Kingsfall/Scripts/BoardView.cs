using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Kingsfall.Chess;

namespace Kingsfall
{
    public enum MarkerKind { None, Move, Capture, Selected, LastMove, Hover }

    /// <summary>Owns the piece objects and the square markers.</summary>
    public class BoardView : MonoBehaviour
    {
        public static BoardView I { get; private set; }

        public readonly PieceView[] Pieces = new PieceView[64];
        Transform pieceRoot;

        readonly MeshRenderer[] markers = new MeshRenderer[64];
        readonly MeshRenderer[] lastMarkers = new MeshRenderer[64];
        readonly MarkerKind[] kinds = new MarkerKind[64];
        MaterialPropertyBlock mpb;
        Light checkLight;
        PieceView checkedKing;
        int hoverSquare = -1;

        static KingsfallLibrary Lib => KingsfallLibrary.I;

        public static Vector3 SquareToWorld(int sq) => new Vector3(Board.File(sq) - 3.5f, 0f, Board.Rank(sq) - 3.5f);

        public static int WorldToSquare(Vector3 p)
        {
            int f = Mathf.FloorToInt(p.x + 4f), r = Mathf.FloorToInt(p.z + 4f);
            if (f < 0 || f > 7 || r < 0 || r > 7) return -1;
            return r * 8 + f;
        }

        void Awake()
        {
            I = this;
            mpb = new MaterialPropertyBlock();
            pieceRoot = new GameObject("Pieces").transform;
            pieceRoot.SetParent(transform, false);
            var markerRoot = new GameObject("Markers").transform;
            markerRoot.SetParent(transform, false);
            for (int s = 0; s < 64; s++)
            {
                markers[s] = MakeMarker(markerRoot, s, Lib.marker, 0.016f);
                lastMarkers[s] = MakeMarker(markerRoot, s, Lib.lastMove, 0.012f);
            }
            var cl = new GameObject("CheckLight");
            cl.transform.SetParent(transform, false);
            checkLight = cl.AddComponent<Light>();
            checkLight.type = LightType.Point;
            checkLight.color = new Color(1f, 0.15f, 0.08f);
            checkLight.range = 3.5f;
            checkLight.intensity = 0f;
            checkLight.shadows = LightShadows.None;
        }

        MeshRenderer MakeMarker(Transform parent, int s, Material mat, float y)
        {
            var go = new GameObject("Marker " + Board.SquareName(s));
            go.transform.SetParent(parent, false);
            go.transform.position = SquareToWorld(s) + Vector3.up * y;
            go.AddComponent<MeshFilter>().sharedMesh = Lib.markerDisc;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.enabled = false;
            return mr;
        }

        public void Spawn(Board b)
        {
            for (int s = 0; s < 64; s++)
            {
                if (Pieces[s]) Destroy(Pieces[s].gameObject);
                Pieces[s] = null;
            }
            for (int s = 0; s < 64; s++)
            {
                int code = b.Sq[s];
                if (code == 0) continue;
                Pieces[s] = PieceView.Create(Board.TypeOf(code), Board.ColourOf(code), s, pieceRoot);
            }
            ClearMarkers();
            SetLastMove(-1, -1);
            SetCheck(-1);
        }

        public PieceView Spawn(PieceType type, int colour, int square)
        {
            var pv = PieceView.Create(type, colour, square, pieceRoot);
            Pieces[square] = pv;
            return pv;
        }

        public IEnumerable<PieceView> All()
        {
            foreach (var p in Pieces) if (p) yield return p;
        }

        // ------------------------------------------------------------ markers

        public void ClearMarkers()
        {
            for (int s = 0; s < 64; s++) { kinds[s] = MarkerKind.None; markers[s].enabled = false; }
        }

        public void SetMarker(int s, MarkerKind kind)
        {
            kinds[s] = kind;
            var mr = markers[s];
            switch (kind)
            {
                case MarkerKind.None: mr.enabled = false; return;
                case MarkerKind.Move: mr.sharedMaterial = Lib.marker; mr.transform.localScale = Vector3.one * 0.42f; break;
                case MarkerKind.Capture: mr.sharedMaterial = Lib.captureMarker; mr.transform.localScale = Vector3.one * 0.98f; break;
                case MarkerKind.Selected: mr.sharedMaterial = Lib.selectMarker; mr.transform.localScale = Vector3.one * 0.98f; break;
                case MarkerKind.Hover: mr.sharedMaterial = Lib.lastMove; mr.transform.localScale = Vector3.one * 0.96f; break;
            }
            mr.enabled = true;
        }

        public void SetLastMove(int from, int to)
        {
            for (int s = 0; s < 64; s++) lastMarkers[s].enabled = s == from || s == to;
        }

        public void SetHover(int s)
        {
            if (hoverSquare == s) return;
            if (hoverSquare >= 0 && kinds[hoverSquare] == MarkerKind.Hover) SetMarker(hoverSquare, MarkerKind.None);
            hoverSquare = s;
            if (s >= 0 && kinds[s] == MarkerKind.None) SetMarker(s, MarkerKind.Hover);
        }

        public void SetCheck(int kingSquare)
        {
            if (checkedKing) { checkedKing.Glow = 0f; checkedKing.SetGlowColour(new Color(1.2f, 0.9f, 0.3f)); }
            checkedKing = kingSquare >= 0 ? Pieces[kingSquare] : null;
            if (checkedKing) checkedKing.SetGlowColour(new Color(4f, 0.4f, 0.15f));
        }

        void Update()
        {
            float t = Time.unscaledTime;
            for (int s = 0; s < 64; s++)
            {
                if (!markers[s].enabled) continue;
                float pulse;
                switch (kinds[s])
                {
                    case MarkerKind.Move: pulse = 0.75f + 0.25f * Mathf.Sin(t * 4f + s * 0.4f); break;
                    case MarkerKind.Capture: pulse = 0.7f + 0.3f * Mathf.Sin(t * 7f); break;
                    case MarkerKind.Selected: pulse = 0.85f + 0.15f * Mathf.Sin(t * 3f); break;
                    default: pulse = 0.5f; break;
                }
                mpb.SetColor("_Color", markers[s].sharedMaterial.GetColor("_Color") * pulse);
                markers[s].SetPropertyBlock(mpb);
            }
            if (checkedKing && checkedKing.gameObject.activeInHierarchy)
            {
                float p = 0.5f + 0.5f * Mathf.Sin(t * 5f);
                checkedKing.Glow = 0.6f + 1.2f * p;
                checkLight.transform.position = checkedKing.transform.position + Vector3.up * 0.9f;
                checkLight.intensity = 2f + 4f * p;
            }
            else checkLight.intensity = 0f;
        }

        // ------------------------------------------------------------ picking

        public int PickSquare(Camera cam, Vector3 mouse)
        {
            Ray ray = cam.ScreenPointToRay(mouse);
            // Prefer pieces: test against a vertical capsule per piece so tall pieces are easy to click.
            float best = float.MaxValue;
            int bestSq = -1;
            for (int s = 0; s < 64; s++)
            {
                var p = Pieces[s];
                if (!p || !p.gameObject.activeInHierarchy) continue;
                Vector3 a = p.transform.position + Vector3.up * 0.1f, b = p.transform.position + Vector3.up * (p.Height - 0.1f);
                float d = RayCapsuleDistance(ray, a, b, out float along);
                if (d < 0.3f && along < best) { best = along; bestSq = s; }
            }
            if (bestSq >= 0) return bestSq;
            if (Mathf.Abs(ray.direction.y) < 1e-4f) return -1;
            float tt = -ray.origin.y / ray.direction.y;
            if (tt < 0) return -1;
            return WorldToSquare(ray.origin + ray.direction * tt);
        }

        static float RayCapsuleDistance(Ray ray, Vector3 a, Vector3 b, out float along)
        {
            // closest points between ray and segment
            Vector3 u = ray.direction, v = b - a, w = ray.origin - a;
            float A = Vector3.Dot(u, u), B = Vector3.Dot(u, v), C = Vector3.Dot(v, v), D = Vector3.Dot(u, w), E = Vector3.Dot(v, w);
            float den = A * C - B * B;
            float sc = den > 1e-6f ? (B * E - C * D) / den : 0f;
            float tc = den > 1e-6f ? (A * E - B * D) / den : 0f;
            tc = Mathf.Clamp01(tc);
            sc = Mathf.Max(0f, (Vector3.Dot(u, a + v * tc - ray.origin)) / A);
            along = sc;
            return (ray.origin + u * sc - (a + v * tc)).magnitude;
        }
    }
}

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Kingsfall.Chess;

namespace Kingsfall
{
    /// <summary>
    /// The grandmaster who appears beside the board after a game: rises from the floor in a pillar
    /// of light, gestures in time with his speech, and draws glowing arrows on the board.
    /// </summary>
    public class Grandmaster : MonoBehaviour
    {
        public static Grandmaster I { get; private set; }

        const float FloorY = -0.9f;
        const float Scale = 2.15f;

        public bool Present { get; private set; }

        Transform root, body;
        MeshRenderer bodyRenderer, skinRenderer;
        MaterialPropertyBlock block;
        Transform orb, eyeL, eyeR;
        Light orbLight;
        Material orbMat, eyeMat, arrowMat;
        float rise; // 0 hidden .. 1 standing
        float gesture;
        readonly List<(GameObject go, Material mat, Color colour, float born)> arrows = new List<(GameObject, Material, Color, float)>();
        int side;

        static KingsfallLibrary Lib => KingsfallLibrary.I;

        void Awake() { I = this; }

        void Build()
        {
            if (root) return;
            root = new GameObject("Grandmaster").transform;
            body = new GameObject("Body").transform;
            body.SetParent(root, false);
            body.localScale = Vector3.one * Scale;
            body.gameObject.AddComponent<MeshFilter>().sharedMesh = Lib.grandmasterMesh;
            bodyRenderer = body.gameObject.AddComponent<MeshRenderer>();
            bodyRenderer.sharedMaterial = Lib.sageRobe ? Lib.sageRobe : Lib.whitePiece;
            bodyRenderer.shadowCastingMode = ShadowCastingMode.On;
            bodyRenderer.reflectionProbeUsage = ReflectionProbeUsage.BlendProbes;
            var skin = new GameObject("Face and Beard");
            skin.transform.SetParent(body, false);
            skin.AddComponent<MeshFilter>().sharedMesh = Lib.grandmasterSkinMesh;
            skinRenderer = skin.AddComponent<MeshRenderer>();
            skinRenderer.sharedMaterial = Lib.whitePiece;
            skinRenderer.shadowCastingMode = ShadowCastingMode.On;
            skinRenderer.reflectionProbeUsage = ReflectionProbeUsage.BlendProbes;
            block = new MaterialPropertyBlock();
            block.SetVector("_NoiseOffset", new Vector4(13.7f, 4.2f, 27.1f, 0));
            block.SetColor("_GlowColor", new Color(0.5f, 0.75f, 1.6f));
            bodyRenderer.SetPropertyBlock(block);
            skinRenderer.SetPropertyBlock(block);

            orbMat = new Material(Lib.orb);
            eyeMat = new Material(Lib.orb);
            orb = Glow("Staff Orb", new Vector3(0.42f, 1.95f, 0.25f), 0.085f, orbMat);
            eyeL = Glow("Eye", new Vector3(-0.05f, 1.432f, 0.158f), 0.014f, eyeMat);
            eyeR = Glow("Eye", new Vector3(0.05f, 1.432f, 0.158f), 0.014f, eyeMat);
            orbLight = new GameObject("Orb Light").AddComponent<Light>();
            orbLight.transform.SetParent(orb, false);
            orbLight.type = LightType.Point;
            orbLight.range = 6f;
            orbLight.color = new Color(0.55f, 0.75f, 1f);
            orbLight.shadows = LightShadows.None;

            arrowMat = new Material(Lib.marker);
            arrowMat.SetTexture("_MainTex", Texture2D.whiteTexture);
            root.gameObject.SetActive(false);
        }

        Transform Glow(string name, Vector3 local, float radius, Material mat)
        {
            var go = new GameObject(name);
            go.transform.SetParent(body, false);
            go.transform.localPosition = local;
            go.transform.localScale = Vector3.one * radius * 2f;
            go.AddComponent<MeshFilter>().sharedMesh = Lib.sphere;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            return go.transform;
        }

        /// <summary>World position he stands at for a review seen from `colour`'s side.</summary>
        public static Vector3 StandPoint(int colour)
        {
            var p = new Vector3(6.1f, FloorY, 0.4f);
            return colour == Board.White ? p : -p + new Vector3(0, 2f * FloorY, 0);
        }

        public static Vector3 HeadPosition(int colour) => StandPoint(colour) + Vector3.up * (1.45f * Scale);

        public IEnumerator Appear(int colour)
        {
            Build();
            side = colour;
            Present = true;
            var at = StandPoint(colour);
            root.position = at;
            // face halfway between the board centre and the player
            Vector3 player = colour == Board.White ? new Vector3(0, 0, -12f) : new Vector3(0, 0, 12f);
            Vector3 look = Vector3.Lerp(new Vector3(0, at.y, 0), new Vector3(player.x, at.y, player.z), 0.55f) - at;
            root.rotation = Quaternion.LookRotation(new Vector3(look.x, 0, look.z));
            root.gameObject.SetActive(true);
            rise = 0f;

            SoundBank.I.Play(Sfx.Gong, 0.65f, 0.8f, 0f);
            SoundBank.I.Play(Sfx.Promote, 0.55f, 0.75f, 0f);
            StartCoroutine(Fx.I.LightPillar(at, new Color(0.35f, 0.5f, 1.1f), 2.6f, 1.0f));
            Fx.I.Dust(at + Vector3.up * 0.05f, 1.1f, 70, 2.2f);
            yield return Tween.Run(1.6f, k => rise = k, Ease.OutCubic);
            Fx.I.Sparks(HeadPosition(colour), Vector3.up, 40, 3f, new Color(0.6f, 0.8f, 2f), 160f);
        }

        public IEnumerator Vanish()
        {
            if (!Present) yield break;
            ClearArrows();
            SoundBank.I.Play(Sfx.Magic, 0.6f, 0.6f, 0f);
            StartCoroutine(Fx.I.LightPillar(root.position, new Color(0.35f, 0.5f, 1.1f), 1.6f, 1.0f));
            yield return Tween.Run(1.1f, k => rise = 1f - k, Ease.InCubic);
            Hide();
        }

        public void Hide()
        {
            Present = false;
            rise = 0f;
            ClearArrows();
            if (root) root.gameObject.SetActive(false);
        }

        void Update()
        {
            if (!root || !root.gameObject.activeSelf) return;
            float t = Time.unscaledTime;
            float level = Speech.I ? Speech.I.Level : 0f;
            gesture = Mathf.Lerp(gesture, level, 1f - Mathf.Exp(-Time.unscaledDeltaTime * 8f));

            // rise from below the floor; sway while idle; nod and lean in while speaking
            float sink = (1f - rise) * Scale * 2.1f;
            body.localPosition = new Vector3(0, -sink + Mathf.Sin(t * 1.3f) * 0.025f + gesture * 0.05f, 0);
            body.localRotation = Quaternion.Euler(
                3f * Mathf.Sin(t * 0.9f) + gesture * (5f + 4f * Mathf.Sin(t * 7.5f)),
                4f * Mathf.Sin(t * 0.55f),
                2f * Mathf.Sin(t * 0.7f) + gesture * 2.5f * Mathf.Sin(t * 5.1f));

            float pulse = 0.85f + 0.15f * Mathf.Sin(t * 2.4f) + gesture * 1.4f;
            orbMat.SetColor("_Color", new Color(0.5f, 0.8f, 2.4f) * pulse * rise);
            eyeMat.SetColor("_Color", new Color(0.7f, 0.9f, 2.6f) * (0.9f + gesture * 1.5f) * rise);
            orb.localScale = Vector3.one * 0.17f * (1f + gesture * 0.25f);
            orbLight.intensity = (6f + gesture * 14f) * rise;
            block.SetFloat("_Glow", 0.15f + gesture * 0.35f);
            bodyRenderer.SetPropertyBlock(block);
            skinRenderer.SetPropertyBlock(block);

            for (int i = 0; i < arrows.Count; i++)
            {
                var a = arrows[i];
                if (a.mat) a.mat.SetColor("_Color", a.colour * (0.8f + 0.2f * Mathf.Sin(t * 4f + i)) * Mathf.Clamp01((t - a.born) * 3f));
            }
        }

        // ------------------------------------------------------------ arrows

        public static Color ArrowColour(ArrowKind k) => k switch
        {
            ArrowKind.Played => new Color(2.6f, 0.35f, 0.2f),
            ArrowKind.Threat => new Color(2.6f, 1.1f, 0.15f),
            ArrowKind.Better => new Color(0.35f, 2.4f, 0.55f),
            _ => new Color(0.5f, 1.4f, 2.8f),
        };

        public void ShowArrows(IEnumerable<ReviewArrow> list)
        {
            Build();
            ClearArrows();
            foreach (var a in list)
            {
                var go = new GameObject("Review Arrow");
                go.AddComponent<MeshFilter>().sharedMesh = ArrowMesh(BoardView.SquareToWorld(a.From), BoardView.SquareToWorld(a.To));
                var mr = go.AddComponent<MeshRenderer>();
                var mat = new Material(arrowMat);
                mat.SetColor("_Color", Color.black);
                mr.sharedMaterial = mat;
                mr.shadowCastingMode = ShadowCastingMode.Off;
                arrows.Add((go, mat, ArrowColour(a.Kind), Time.unscaledTime));
            }
        }

        public void ClearArrows()
        {
            foreach (var a in arrows)
            {
                if (a.go) { Destroy(a.go.GetComponent<MeshFilter>().sharedMesh); Destroy(a.go); }
                if (a.mat) Destroy(a.mat);
            }
            arrows.Clear();
        }

        static Mesh ArrowMesh(Vector3 from, Vector3 to)
        {
            const float y = 0.05f, shaft = 0.12f, head = 0.33f, headLen = 0.46f, inset = 0.16f;
            Vector3 dir = to - from; dir.y = 0;
            float len = dir.magnitude;
            dir /= Mathf.Max(len, 1e-4f);
            Vector3 side = Vector3.Cross(Vector3.up, dir);
            Vector3 a = from + dir * inset, tip = to - dir * 0.08f;
            Vector3 neck = tip - dir * headLen;
            Vector3 up = Vector3.up * y;
            var v = new[]
            {
                a - side * shaft + up, a + side * shaft + up, neck - side * shaft + up, neck + side * shaft + up,
                neck - side * head + up, neck + side * head + up, tip + up,
            };
            var uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1) };
            var m = new Mesh { name = "Arrow" };
            m.vertices = v;
            m.uv = uv;
            m.triangles = new[] { 0, 2, 1, 1, 2, 3, 4, 6, 5 };
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }
    }
}

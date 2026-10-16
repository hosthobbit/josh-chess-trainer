using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Kingsfall
{
    /// <summary>Runtime-built visual effects: particle bursts, shattering, lightning, shockwaves, light flashes.</summary>
    public class Fx : MonoBehaviour
    {
        public static Fx I { get; private set; }
        static KingsfallLibrary Lib => KingsfallLibrary.I;

        PhysicsMaterial shardPhysics;
        readonly List<GameObject> live = new List<GameObject>();

        void Awake()
        {
            I = this;
            shardPhysics = new PhysicsMaterial("Shard") { bounciness = 0.22f, dynamicFriction = 0.55f, staticFriction = 0.7f, bounceCombine = PhysicsMaterialCombine.Average };
        }

        public void ClearAll()
        {
            foreach (var g in live) if (g) Destroy(g);
            live.Clear();
        }

        // ------------------------------------------------------------ particles

        public static ParticleSystem MakeSystem(string name, Material mat, Vector3 pos, Transform parent = null)
        {
            var go = new GameObject(name);
            if (parent) go.transform.SetParent(parent, false);
            go.transform.position = pos;
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            var em = ps.emission;
            em.rateOverTime = 0;
            return ps;
        }

        static void OneShot(ParticleSystem ps, int count)
        {
            var main = ps.main;
            main.stopAction = ParticleSystemStopAction.Destroy;
            main.loop = false;
            var em = ps.emission;
            em.rateOverTime = 0;
            em.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)count) });
            ps.Play();
        }

        static Gradient Fade(Color a, Color b, float alphaStart = 1f)
        {
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(a, 0f), new GradientColorKey(b, 1f) },
                      new[] { new GradientAlphaKey(alphaStart, 0f), new GradientAlphaKey(alphaStart, 0.4f), new GradientAlphaKey(0f, 1f) });
            return g;
        }

        public void Dust(Vector3 pos, float radius = 0.5f, int count = 40, float speed = 1.6f)
        {
            var ps = MakeSystem("Dust", Lib.dust, pos + Vector3.up * 0.05f);
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.9f, 2.0f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(speed * 0.4f, speed);
            main.startSize = new ParticleSystem.MinMaxCurve(0.25f, 0.6f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0, Mathf.PI * 2);
            main.startColor = new Color(0.55f, 0.5f, 0.45f, 0.55f);
            main.gravityModifier = -0.03f;
            var shape = ps.shape; shape.shapeType = ParticleSystemShapeType.Circle; shape.radius = radius; shape.rotation = new Vector3(-90, 0, 0);
            var vol = ps.velocityOverLifetime; vol.enabled = true; vol.space = ParticleSystemSimulationSpace.Local;
            vol.radial = new ParticleSystem.MinMaxCurve(speed * 0.8f);
            var limit = ps.limitVelocityOverLifetime; limit.enabled = true; limit.dampen = 0.12f; limit.limit = 0.2f;
            var sol = ps.sizeOverLifetime; sol.enabled = true; sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0, 0.6f, 1, 1.8f));
            var col = ps.colorOverLifetime; col.enabled = true; col.color = Fade(Color.white, new Color(0.7f, 0.65f, 0.6f), 1f);
            var rot = ps.rotationOverLifetime; rot.enabled = true; rot.z = new ParticleSystem.MinMaxCurve(-1f, 1f);
            OneShot(ps, count);
        }

        public void Sparks(Vector3 pos, Vector3 dir, int count = 60, float speed = 7f, Color? colour = null, float spread = 50f)
        {
            var ps = MakeSystem("Sparks", Lib.spark, pos);
            ps.transform.rotation = Quaternion.LookRotation(dir.sqrMagnitude > 0.001f ? dir : Vector3.up);
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.3f, 0.9f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(speed * 0.4f, speed);
            main.startSize = new ParticleSystem.MinMaxCurve(0.02f, 0.06f);
            main.startColor = colour ?? new Color(1f, 0.75f, 0.35f);
            main.gravityModifier = 1.2f;
            var shape = ps.shape; shape.shapeType = ParticleSystemShapeType.Cone; shape.angle = spread; shape.radius = 0.05f;
            var r = ps.GetComponent<ParticleSystemRenderer>(); r.renderMode = ParticleSystemRenderMode.Stretch; r.velocityScale = 0.05f; r.lengthScale = 2f;
            var col = ps.colorOverLifetime; col.enabled = true; col.color = Fade(Color.white, new Color(1f, 0.3f, 0.1f));
            var coll = ps.collision; coll.enabled = true; coll.type = ParticleSystemCollisionType.Planes;
            coll.bounce = 0.4f; coll.lifetimeLoss = 0.3f;
            // A plane at the board surface
            var plane = new GameObject("SparkPlane").transform; plane.SetParent(ps.transform, true); plane.position = new Vector3(pos.x, 0.01f, pos.z); plane.rotation = Quaternion.identity;
            coll.SetPlane(0, plane);
            OneShot(ps, count);
        }

        public void Embers(Vector3 pos, int count = 40, Color? colour = null, float radius = 0.3f, float rise = 1.5f)
        {
            var ps = MakeSystem("Embers", Lib.ember, pos);
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.0f, 2.4f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.3f, 1.6f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.03f, 0.09f);
            main.startColor = colour ?? new Color(1f, 0.55f, 0.2f);
            main.gravityModifier = -0.15f * rise;
            var shape = ps.shape; shape.shapeType = ParticleSystemShapeType.Sphere; shape.radius = radius;
            var noise = ps.noise; noise.enabled = true; noise.strength = 0.6f; noise.frequency = 1.4f; noise.scrollSpeed = 0.6f;
            var col = ps.colorOverLifetime; col.enabled = true; col.color = Fade(Color.white, new Color(1f, 0.2f, 0.05f));
            var sol = ps.sizeOverLifetime; sol.enabled = true; sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0, 1, 1, 0));
            OneShot(ps, count);
        }

        public void Smoke(Vector3 pos, int count = 18, float size = 0.8f)
        {
            var ps = MakeSystem("Smoke", Lib.smoke, pos);
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.5f, 3f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.2f, 0.8f);
            main.startSize = new ParticleSystem.MinMaxCurve(size * 0.6f, size);
            main.startRotation = new ParticleSystem.MinMaxCurve(0, Mathf.PI * 2);
            main.gravityModifier = -0.05f;
            var shape = ps.shape; shape.shapeType = ParticleSystemShapeType.Sphere; shape.radius = 0.3f;
            var sol = ps.sizeOverLifetime; sol.enabled = true; sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0, 0.7f, 1, 2.2f));
            var col = ps.colorOverLifetime; col.enabled = true; col.color = Fade(Color.white, Color.white, 0.8f);
            var rot = ps.rotationOverLifetime; rot.enabled = true; rot.z = new ParticleSystem.MinMaxCurve(-0.6f, 0.6f);
            OneShot(ps, count);
        }

        /// <summary>Glowing motes streaming off a piece's surface (used while dissolving).</summary>
        public ParticleSystem SoulStream(PieceView piece, Color colour, float duration)
        {
            var ps = MakeSystem("Soul", Lib.ember, piece.Body.position, null);
            var main = ps.main;
            main.loop = true;
            main.duration = duration;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.8f, 1.8f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.1f, 0.5f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.025f, 0.07f);
            main.startColor = colour;
            main.gravityModifier = -0.35f;
            main.stopAction = ParticleSystemStopAction.Destroy;
            var shape = ps.shape; shape.shapeType = ParticleSystemShapeType.MeshRenderer; shape.meshRenderer = piece.Renderer; shape.useMeshColors = false;
            var noise = ps.noise; noise.enabled = true; noise.strength = 0.5f; noise.frequency = 2f;
            var col = ps.colorOverLifetime; col.enabled = true; col.color = Fade(Color.white, colour);
            var em = ps.emission; em.rateOverTime = 260f;
            ps.Play();
            return ps;
        }

        // ------------------------------------------------------------ lights & meshes

        public void FlashLight(Vector3 pos, Color colour, float intensity, float range, float duration)
        {
            StartCoroutine(FlashRoutine(pos, colour, intensity, range, duration));
        }

        IEnumerator FlashRoutine(Vector3 pos, Color colour, float intensity, float range, float duration)
        {
            var go = new GameObject("Flash");
            go.transform.position = pos;
            var l = go.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = colour;
            l.range = range;
            l.intensity = intensity;
            l.shadows = LightShadows.None;
            float t = 0;
            while (t < duration)
            {
                l.intensity = intensity * Mathf.Pow(1f - t / duration, 2f);
                yield return null;
                t += Time.unscaledDeltaTime;
            }
            Destroy(go);
        }

        public void Shockwave(Vector3 pos, float radius, Color colour, float duration = 0.6f)
        {
            StartCoroutine(ShockRoutine(pos, radius, colour, duration));
        }

        IEnumerator ShockRoutine(Vector3 pos, float radius, Color colour, float duration)
        {
            var go = new GameObject("Shockwave");
            go.transform.position = new Vector3(pos.x, 0.02f, pos.z);
            go.AddComponent<MeshFilter>().sharedMesh = Lib.ringMesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = Lib.ring;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            var mpb = new MaterialPropertyBlock();
            float t = 0;
            while (t < duration)
            {
                float k = t / duration;
                go.transform.localScale = Vector3.one * Mathf.Lerp(0.2f, radius, Ease.OutCubic(k));
                mpb.SetColor("_Color", colour * (1f - k) * (1f - k));
                mr.SetPropertyBlock(mpb);
                yield return null;
                t += Time.deltaTime;
            }
            Destroy(go);
        }

        /// <summary>Jagged flickering bolt from a to b.</summary>
        public IEnumerator Lightning(Vector3 a, Vector3 b, float duration, Color colour, float width = 0.12f, int branches = 3)
        {
            var bolts = new List<LineRenderer>();
            for (int i = 0; i < 1 + branches; i++)
            {
                var go = new GameObject("Bolt");
                var lr = go.AddComponent<LineRenderer>();
                lr.sharedMaterial = Lib.lightning;
                lr.widthMultiplier = i == 0 ? width : width * 0.4f;
                lr.numCapVertices = 2;
                lr.shadowCastingMode = ShadowCastingMode.Off;
                lr.textureMode = LineTextureMode.Stretch;
                lr.alignment = LineAlignment.View;
                bolts.Add(lr);
            }
            var lightGo = new GameObject("BoltLight");
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Point; light.color = colour; light.range = 12f; light.shadows = LightShadows.None;

            float t = 0;
            float nextJitter = 0;
            while (t < duration)
            {
                if (t >= nextJitter)
                {
                    nextJitter = t + 0.045f;
                    BuildBolt(bolts[0], a, b, 18, 0.35f);
                    for (int i = 1; i < bolts.Count; i++)
                    {
                        Vector3 start = Vector3.Lerp(a, b, Random.Range(0.2f, 0.7f));
                        Vector3 end = start + (b - a).normalized * Random.Range(0.6f, 1.5f) + Random.insideUnitSphere * 1.2f;
                        BuildBolt(bolts[i], start, end, 8, 0.25f);
                    }
                    float flicker = Random.Range(0.5f, 1.3f);
                    foreach (var lr in bolts) { lr.startColor = colour * flicker; lr.endColor = colour * flicker; }
                    lightGo.transform.position = Vector3.Lerp(a, b, 0.75f);
                    light.intensity = 40f * flicker;
                }
                yield return null;
                t += Time.deltaTime;
            }
            foreach (var lr in bolts) Destroy(lr.gameObject);
            Destroy(lightGo);
        }

        static void BuildBolt(LineRenderer lr, Vector3 a, Vector3 b, int segments, float jag)
        {
            lr.positionCount = segments + 1;
            Vector3 dir = b - a;
            Vector3 side = Vector3.Cross(dir.normalized, Vector3.up);
            if (side.sqrMagnitude < 0.01f) side = Vector3.right;
            Vector3 side2 = Vector3.Cross(dir.normalized, side);
            for (int i = 0; i <= segments; i++)
            {
                float k = i / (float)segments;
                Vector3 p = a + dir * k;
                if (i != 0 && i != segments)
                {
                    float amp = jag * Mathf.Sin(k * Mathf.PI);
                    p += side * Random.Range(-amp, amp) + side2 * Random.Range(-amp, amp);
                }
                lr.SetPosition(i, p);
            }
        }

        /// <summary>Vertical pillar of light (bishop smite / promotion).</summary>
        public IEnumerator LightPillar(Vector3 pos, Color colour, float duration, float radius = 0.45f, float height = 14f)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Destroy(go.GetComponent<Collider>());
            go.name = "LightPillar";
            go.transform.position = new Vector3(pos.x, height * 0.5f, pos.z);
            var mr = go.GetComponent<MeshRenderer>();
            mr.sharedMaterial = Lib.beam;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            var mpb = new MaterialPropertyBlock();
            float t = 0;
            while (t < duration)
            {
                float k = t / duration;
                float open = k < 0.15f ? Ease.OutBack(k / 0.15f) : k > 0.75f ? 1f - Ease.InQuad((k - 0.75f) / 0.25f) : 1f;
                float w = radius * open * (1f + 0.06f * Mathf.Sin(Time.time * 40f));
                go.transform.localScale = new Vector3(w * 2f, height * 0.5f, w * 2f);
                mpb.SetColor("_Color", colour * Mathf.Max(open, 0f));
                mr.SetPropertyBlock(mpb);
                yield return null;
                t += Time.deltaTime;
            }
            Destroy(go);
        }

        /// <summary>A glowing projectile with a trail flying a curved path; returns when it lands.</summary>
        public IEnumerator Orb(Vector3 from, Vector3 to, Vector3 bend, float duration, Color colour, float size = 0.16f)
        {
            var go = new GameObject("Orb");
            go.transform.position = from;
            var core = new GameObject("Core");
            core.transform.SetParent(go.transform, false);
            core.AddComponent<MeshFilter>().sharedMesh = Lib.sphere;
            var mr = core.AddComponent<MeshRenderer>();
            mr.sharedMaterial = Lib.orb;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            var mpb = new MaterialPropertyBlock(); mpb.SetColor("_Color", colour * 1.4f); mr.SetPropertyBlock(mpb);
            core.transform.localScale = Vector3.one * size;
            var trail = go.AddComponent<TrailRenderer>();
            trail.sharedMaterial = Lib.orb;
            trail.time = 0.35f;
            trail.widthMultiplier = size * 0.9f;
            trail.widthCurve = AnimationCurve.Linear(0, 1, 1, 0);
            trail.startColor = colour * 1.5f;
            trail.endColor = new Color(colour.r, colour.g, colour.b, 0f);
            trail.shadowCastingMode = ShadowCastingMode.Off;
            var light = go.AddComponent<Light>();
            light.type = LightType.Point; light.color = colour; light.range = 4f; light.intensity = 8f; light.shadows = LightShadows.None;

            Vector3 ctrl = (from + to) * 0.5f + bend;
            float t = 0;
            while (t < duration)
            {
                float k = Ease.InQuad(t / duration);
                Vector3 p = (1 - k) * (1 - k) * from + 2 * (1 - k) * k * ctrl + k * k * to;
                go.transform.position = p;
                core.transform.localScale = Vector3.one * size * (1f + 0.25f * Mathf.Sin(Time.time * 50f));
                yield return null;
                t += Time.deltaTime;
            }
            go.transform.position = to;
            Destroy(core);
            Destroy(light);
            trail.emitting = false;
            Destroy(go, 0.5f);
        }

        // ------------------------------------------------------------ shattering

        /// <summary>Break a piece into its pre-cut fragments with real physics.</summary>
        public void Shatter(PieceView piece, Vector3 origin, Vector3 push, float force, float upward = 1.5f, float spin = 8f, float life = 4.5f)
        {
            var set = Lib.shards[(int)piece.Type];
            if (set == null || set.meshes == null || set.meshes.Length == 0) { piece.gameObject.SetActive(false); return; }
            var mat = piece.Renderer.sharedMaterial;
            Vector4 baseOffset = piece.NoiseOffset;
            Transform body = piece.Body;
            Vector3 bodyScale = body.lossyScale;

            for (int i = 0; i < set.meshes.Length; i++)
            {
                var m = set.meshes[i];
                if (!m) continue;
                var go = new GameObject("Shard");
                Vector3 localOffset = set.offsets[i];
                go.transform.SetPositionAndRotation(body.TransformPoint(localOffset), body.rotation);
                go.transform.localScale = bodyScale;
                go.AddComponent<MeshFilter>().sharedMesh = m;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = mat;
                mr.shadowCastingMode = ShadowCastingMode.On;
                var mpb = new MaterialPropertyBlock();
                mpb.SetVector("_NoiseOffset", baseOffset + (Vector4)localOffset);
                mr.SetPropertyBlock(mpb);

                var col = go.AddComponent<MeshCollider>();
                col.sharedMesh = m;
                col.convex = true;
                col.sharedMaterial = shardPhysics;
                var rb = go.AddComponent<Rigidbody>();
                rb.mass = 0.2f;
                rb.interpolation = RigidbodyInterpolation.Interpolate;
                rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
                rb.linearDamping = 0.05f;
                rb.angularDamping = 0.2f;

                Vector3 away = go.transform.position - origin;
                away.y = Mathf.Max(away.y, 0f);
                Vector3 v = away.normalized * force * Random.Range(0.6f, 1.2f) + push + Vector3.up * upward * Random.Range(0.5f, 1.3f);
                rb.linearVelocity = v;
                rb.angularVelocity = Random.insideUnitSphere * spin;

                var life2 = go.AddComponent<ShardLife>();
                life2.Init(mpb, mr, mat.GetColor("_InteriorColor"), life + Random.Range(-0.6f, 0.8f), baseOffset + (Vector4)localOffset);
                live.Add(go);
            }
            live.RemoveAll(g => g == null);
            piece.gameObject.SetActive(false);
        }
    }

    /// <summary>Cools a fragment's glowing interior, then sinks and removes it.</summary>
    public class ShardLife : MonoBehaviour
    {
        MaterialPropertyBlock mpb;
        MeshRenderer mr;
        Color hot;
        float life, age;
        Vector3 startScale;
        Vector4 offset;

        public void Init(MaterialPropertyBlock block, MeshRenderer renderer, Color interior, float lifetime, Vector4 noiseOffset)
        {
            mpb = block; mr = renderer; hot = interior; life = lifetime; offset = noiseOffset;
            startScale = transform.localScale;
        }

        void Update()
        {
            age += Time.deltaTime;
            float heat = Mathf.Clamp01(1f - age / (life * 0.8f));
            mpb.SetColor("_InteriorColor", hot * (0.08f + 0.9f * heat * heat));
            mpb.SetVector("_NoiseOffset", offset);
            float fade = Mathf.Clamp01((age - life) / 0.8f);
            if (fade > 0f)
            {
                mpb.SetFloat("_Dissolve", fade);
                mpb.SetColor("_DissolveEdge", hot * 0.8f);
            }
            mr.SetPropertyBlock(mpb);
            if (fade >= 1f) Destroy(gameObject);
            if (transform.position.y < -6f) Destroy(gameObject);
        }
    }
}

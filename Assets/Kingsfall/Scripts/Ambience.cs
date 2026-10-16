using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;

namespace Kingsfall
{
    /// <summary>Keeps the throne hall alive: fire, flicker, embers, motes, rune glow, lighting probes.</summary>
    public class Ambience : MonoBehaviour
    {
        public Transform[] Fires;
        public Light[] FireLights;
        public Transform Rune;
        public ReflectionProbe Probe;
        public Color AmbientSky = new Color(0.16f, 0.17f, 0.24f);
        public Color AmbientEquator = new Color(0.09f, 0.07f, 0.06f);
        public Color AmbientGround = new Color(0.03f, 0.025f, 0.02f);

        float[] baseIntensity;
        float[] seeds;
        MaterialPropertyBlock runeBlock;
        MeshRenderer runeRenderer;
        Color runeColour;

        void Start()
        {
            var lib = KingsfallLibrary.I;

            // Ambient probe built explicitly so it doesn't depend on baked lighting.
            var sh = new SphericalHarmonicsL2();
            sh.AddAmbientLight((AmbientEquator + AmbientGround) * 0.5f);
            sh.AddDirectionalLight(Vector3.up, AmbientSky * 1.6f, 1f);
            sh.AddDirectionalLight(Vector3.down, AmbientGround, 0.5f);
            RenderSettings.ambientMode = AmbientMode.Custom;
            RenderSettings.ambientProbe = sh;

            baseIntensity = new float[FireLights.Length];
            seeds = new float[FireLights.Length];
            for (int i = 0; i < FireLights.Length; i++) { baseIntensity[i] = FireLights[i].intensity; seeds[i] = Random.value * 100f; }

            foreach (var f in Fires) BuildFire(f, lib);
            BuildMotes(lib);

            if (Rune)
            {
                runeRenderer = Rune.GetComponent<MeshRenderer>();
                runeBlock = new MaterialPropertyBlock();
                runeColour = runeRenderer.sharedMaterial.GetColor("_Color");
            }
            if (Probe) StartCoroutine(RefreshProbe());
        }

        IEnumerator RefreshProbe()
        {
            // render once the pieces exist, then occasionally so the board keeps reflecting the fires
            yield return new WaitForSecondsRealtime(0.4f);
            while (true)
            {
                Probe.RenderProbe();
                yield return new WaitForSecondsRealtime(4f);
            }
        }

        void BuildFire(Transform anchor, KingsfallLibrary lib)
        {
            // core flames
            var flame = Fx.MakeSystem("Flame", lib.fire, anchor.position, anchor);
            var main = flame.main;
            main.loop = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.45f, 0.9f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1.0f, 2.0f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.5f, 1.0f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0, Mathf.PI * 2);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.55f, 0.18f), new Color(1f, 0.35f, 0.08f));
            main.maxParticles = 300;
            var em = flame.emission; em.rateOverTime = 110;
            var shape = flame.shape; shape.shapeType = ParticleSystemShapeType.Circle; shape.radius = 0.25f; shape.rotation = new Vector3(-90, 0, 0);
            var sol = flame.sizeOverLifetime; sol.enabled = true;
            sol.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0, 0.5f), new Keyframe(0.3f, 1f), new Keyframe(1, 0.1f)));
            var col = flame.colorOverLifetime; col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(new Color(1f, 0.9f, 0.6f), 0), new GradientColorKey(new Color(1f, 0.45f, 0.1f), 0.4f), new GradientColorKey(new Color(0.6f, 0.1f, 0.02f), 1f) },
                      new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(1, 0.15f), new GradientAlphaKey(0.6f, 0.6f), new GradientAlphaKey(0, 1) });
            col.color = g;
            var noise = flame.noise; noise.enabled = true; noise.strength = 0.35f; noise.frequency = 1.6f; noise.scrollSpeed = 1.2f;
            var rot = flame.rotationOverLifetime; rot.enabled = true; rot.z = new ParticleSystem.MinMaxCurve(-1.5f, 1.5f);
            flame.Play();

            // embers
            var embers = Fx.MakeSystem("FireEmbers", lib.ember, anchor.position + Vector3.up * 0.2f, anchor);
            var em2 = embers.main;
            em2.loop = true;
            em2.startLifetime = new ParticleSystem.MinMaxCurve(1.5f, 3.5f);
            em2.startSpeed = new ParticleSystem.MinMaxCurve(0.5f, 1.5f);
            em2.startSize = new ParticleSystem.MinMaxCurve(0.02f, 0.05f);
            em2.startColor = new Color(1f, 0.55f, 0.2f);
            em2.gravityModifier = -0.08f;
            var e2 = embers.emission; e2.rateOverTime = 9;
            var s2 = embers.shape; s2.shapeType = ParticleSystemShapeType.Circle; s2.radius = 0.25f; s2.rotation = new Vector3(-90, 0, 0);
            var n2 = embers.noise; n2.enabled = true; n2.strength = 0.8f; n2.frequency = 0.8f;
            var c2 = embers.colorOverLifetime; c2.enabled = true;
            var g2 = new Gradient();
            g2.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(new Color(1f, 0.3f, 0.05f), 1) },
                       new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(1, 0.7f), new GradientAlphaKey(0, 1) });
            c2.color = g2;
            embers.Play();

            // smoke
            var smoke = Fx.MakeSystem("FireSmoke", lib.smoke, anchor.position + Vector3.up * 0.7f, anchor);
            var sm = smoke.main;
            sm.loop = true;
            sm.startLifetime = new ParticleSystem.MinMaxCurve(2.5f, 4f);
            sm.startSpeed = new ParticleSystem.MinMaxCurve(0.4f, 0.8f);
            sm.startSize = new ParticleSystem.MinMaxCurve(0.6f, 1.2f);
            sm.startRotation = new ParticleSystem.MinMaxCurve(0, Mathf.PI * 2);
            var se = smoke.emission; se.rateOverTime = 5;
            var ss = smoke.shape; ss.shapeType = ParticleSystemShapeType.Circle; ss.radius = 0.2f; ss.rotation = new Vector3(-90, 0, 0);
            var ssol = smoke.sizeOverLifetime; ssol.enabled = true; ssol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0, 0.6f, 1, 2.5f));
            var sc = smoke.colorOverLifetime; sc.enabled = true;
            var g3 = new Gradient();
            g3.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                       new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(0.35f, 0.2f), new GradientAlphaKey(0, 1) });
            sc.color = g3;
            smoke.Play();
        }

        void BuildMotes(KingsfallLibrary lib)
        {
            var motes = Fx.MakeSystem("Motes", lib.ember, new Vector3(0, 3f, 0), transform);
            var main = motes.main;
            main.loop = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(6f, 12f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.02f, 0.12f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.012f, 0.03f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.75f, 0.45f, 0.6f), new Color(0.6f, 0.7f, 1f, 0.4f));
            main.maxParticles = 600;
            main.prewarm = true;
            var em = motes.emission; em.rateOverTime = 45;
            var shape = motes.shape; shape.shapeType = ParticleSystemShapeType.Box; shape.scale = new Vector3(22, 6, 22);
            var noise = motes.noise; noise.enabled = true; noise.strength = 0.15f; noise.frequency = 0.25f; noise.scrollSpeed = 0.1f;
            var col = motes.colorOverLifetime; col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                      new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(1, 0.3f), new GradientAlphaKey(1, 0.7f), new GradientAlphaKey(0, 1) });
            col.color = g;
            motes.Play();
        }

        void Update()
        {
            float t = Time.time;
            for (int i = 0; i < FireLights.Length; i++)
            {
                float s = seeds[i];
                float flicker = 0.78f + 0.14f * Mathf.PerlinNoise(t * 3.1f, s) + 0.1f * Mathf.PerlinNoise(t * 11f, s + 7f);
                FireLights[i].intensity = baseIntensity[i] * flicker;
            }
            if (Rune)
            {
                Rune.rotation = Quaternion.Euler(0, t * 2.5f, 0);
                runeBlock.SetColor("_Color", runeColour * (0.8f + 0.2f * Mathf.Sin(t * 0.7f)));
                runeRenderer.SetPropertyBlock(runeBlock);
            }
        }
    }
}

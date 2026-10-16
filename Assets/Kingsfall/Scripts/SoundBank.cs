using System;
using System.Collections.Generic;
using UnityEngine;

namespace Kingsfall
{
    public enum Sfx { Move, Land, Select, Whoosh, Impact, Shatter, Zap, Magic, Blast, Crumble, Check, Gong, Promote, Clank, Charge }

    /// <summary>All audio is synthesised at startup — no audio files needed.</summary>
    public class SoundBank : MonoBehaviour
    {
        public static SoundBank I { get; private set; }
        const int Rate = 44100;

        readonly Dictionary<Sfx, AudioClip> clips = new Dictionary<Sfx, AudioClip>();
        readonly List<AudioSource> pool = new List<AudioSource>();
        AudioSource music, fireBed;
        System.Random rng = new System.Random(7);

        public float MasterVolume = 1f;
        public float MusicVolume = 0.55f;

        void Awake()
        {
            I = this;
            for (int i = 0; i < 20; i++)
            {
                var s = gameObject.AddComponent<AudioSource>();
                s.playOnAwake = false;
                s.spatialBlend = 0f;
                pool.Add(s);
            }
            clips[Sfx.Move] = Make("move", 0.45f, MoveSound);
            clips[Sfx.Land] = Make("land", 0.5f, LandSound);
            clips[Sfx.Select] = Make("select", 0.6f, SelectSound);
            clips[Sfx.Whoosh] = Make("whoosh", 0.55f, WhooshSound);
            clips[Sfx.Impact] = Make("impact", 1.2f, ImpactSound);
            clips[Sfx.Shatter] = Make("shatter", 1.6f, ShatterSound);
            clips[Sfx.Zap] = Make("zap", 1.2f, ZapSound);
            clips[Sfx.Magic] = Make("magic", 1.0f, MagicSound);
            clips[Sfx.Blast] = Make("blast", 2.0f, BlastSound);
            clips[Sfx.Crumble] = Make("crumble", 1.8f, CrumbleSound);
            clips[Sfx.Check] = Make("check", 2.5f, BellSound);
            clips[Sfx.Gong] = Make("gong", 5f, GongSound);
            clips[Sfx.Promote] = Make("promote", 2.6f, ChoirSound);
            clips[Sfx.Clank] = Make("clank", 0.7f, ClankSound);
            clips[Sfx.Charge] = Make("charge", 1.0f, ChargeSound);

            fireBed = gameObject.AddComponent<AudioSource>();
            fireBed.clip = Make("fire", 8f, FireLoop);
            fireBed.loop = true; fireBed.volume = 0.22f; fireBed.Play();

            music = gameObject.AddComponent<AudioSource>();
            music.clip = Make("music", 32f, MusicLoop);
            music.loop = true; music.volume = MusicVolume; music.Play();
        }

        void Update()
        {
            AudioListener.volume = MasterVolume;
            if (music) music.volume = MusicVolume;
        }

        public void Play(Sfx s, float volume = 1f, float pitch = 1f, float pitchJitter = 0.06f)
        {
            if (!clips.TryGetValue(s, out var clip)) return;
            foreach (var src in pool)
            {
                if (src.isPlaying) continue;
                src.clip = clip;
                src.volume = volume;
                src.pitch = pitch * (1f + UnityEngine.Random.Range(-pitchJitter, pitchJitter));
                src.Play();
                return;
            }
        }

        // ------------------------------------------------------------ synthesis helpers

        AudioClip Make(string name, float seconds, Action<float[]> synth)
        {
            int n = Mathf.CeilToInt(seconds * Rate);
            var data = new float[n];
            synth(data);
            // soft clip + tiny fade to avoid clicks
            for (int i = 0; i < n; i++) data[i] = (float)Math.Tanh(data[i]);
            int fade = Mathf.Min(200, n / 4);
            for (int i = 0; i < fade; i++) { data[i] *= i / (float)fade; data[n - 1 - i] *= i / (float)fade; }
            var clip = AudioClip.Create(name, n, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        float Noise() => (float)(rng.NextDouble() * 2.0 - 1.0);
        static float T(int i) => i / (float)Rate;
        static float Env(float t, float attack, float decay) => t < attack ? t / attack : Mathf.Exp(-(t - attack) / decay);

        struct OnePole
        {
            float y;
            public float Low(float x, float cutoff)
            {
                float a = 1f - Mathf.Exp(-2f * Mathf.PI * cutoff / Rate);
                y += a * (x - y);
                return y;
            }
        }

        // ------------------------------------------------------------ voices

        void MoveSound(float[] d)
        {
            var lp = new OnePole(); var lp2 = new OnePole();
            for (int i = 0; i < d.Length; i++)
            {
                float t = T(i);
                float scrape = lp.Low(Noise(), 900f) - lp2.Low(Noise(), 200f) * 0.5f;
                d[i] = scrape * Env(t, 0.04f, 0.12f) * 0.9f;
            }
        }

        void LandSound(float[] d)
        {
            var lp = new OnePole();
            for (int i = 0; i < d.Length; i++)
            {
                float t = T(i);
                float thump = Mathf.Sin(2 * Mathf.PI * (95f * t - 60f * t * t)) * Env(t, 0.002f, 0.07f);
                float click = lp.Low(Noise(), 3000f) * Env(t, 0.001f, 0.015f);
                d[i] = thump * 0.9f + click * 0.6f;
            }
        }

        void SelectSound(float[] d)
        {
            for (int i = 0; i < d.Length; i++)
            {
                float t = T(i);
                float e = Env(t, 0.004f, 0.18f);
                d[i] = (Mathf.Sin(2 * Mathf.PI * 880f * t) * 0.35f + Mathf.Sin(2 * Mathf.PI * 1320f * t) * 0.2f + Mathf.Sin(2 * Mathf.PI * 2640f * t) * 0.06f) * e;
            }
        }

        void WhooshSound(float[] d)
        {
            var lp = new OnePole(); var hp = new OnePole();
            for (int i = 0; i < d.Length; i++)
            {
                float t = T(i);
                float k = t / 0.55f;
                float cutoff = Mathf.Lerp(300f, 2600f, Mathf.Sin(k * Mathf.PI));
                float n = lp.Low(Noise(), cutoff);
                n -= hp.Low(n, 150f);
                d[i] = n * Mathf.Sin(k * Mathf.PI) * 1.6f;
            }
        }

        void ImpactSound(float[] d)
        {
            var lp = new OnePole(); var lp2 = new OnePole();
            for (int i = 0; i < d.Length; i++)
            {
                float t = T(i);
                float boom = Mathf.Sin(2 * Mathf.PI * (62f * t - 14f * t * t)) * Env(t, 0.003f, 0.25f);
                float body = lp.Low(Noise(), 1400f) * Env(t, 0.001f, 0.08f);
                float crack = (Noise() - lp2.Low(Noise(), 2000f)) * Env(t, 0.0005f, 0.02f);
                d[i] = boom * 1.4f + body * 1.2f + crack * 0.8f;
            }
        }

        void ShatterSound(float[] d)
        {
            var lp = new OnePole();
            // stone/glass grains
            for (int g = 0; g < 140; g++)
            {
                int start = (int)(Mathf.Pow((float)rng.NextDouble(), 2.2f) * d.Length * 0.8f);
                int len = (int)(Rate * (0.004f + rng.NextDouble() * 0.03f));
                float freq = 1500f + (float)rng.NextDouble() * 6000f;
                float amp = (0.25f + (float)rng.NextDouble() * 0.6f) * (1f - start / (float)d.Length);
                for (int j = 0; j < len && start + j < d.Length; j++)
                {
                    float tt = j / (float)Rate;
                    d[start + j] += Mathf.Sin(2 * Mathf.PI * freq * tt) * Mathf.Exp(-tt * 180f) * amp + Noise() * Mathf.Exp(-tt * 250f) * amp * 0.5f;
                }
            }
            for (int i = 0; i < d.Length; i++)
            {
                float t = T(i);
                d[i] += Mathf.Sin(2 * Mathf.PI * (55f * t)) * Env(t, 0.002f, 0.3f) * 1.1f + lp.Low(Noise(), 700f) * Env(t, 0.002f, 0.35f) * 1.2f;
            }
        }

        void ZapSound(float[] d)
        {
            var lp = new OnePole();
            float gate = 1f;
            for (int i = 0; i < d.Length; i++)
            {
                float t = T(i);
                if (i % 600 == 0) gate = rng.NextDouble() < 0.6 ? 1f : 0.15f;
                float buzz = Mathf.Sign(Mathf.Sin(2 * Mathf.PI * 120f * t)) * 0.25f;
                float crackle = (Noise() - lp.Low(Noise(), 1200f)) * gate;
                float e = t < 0.02f ? t / 0.02f : Mathf.Exp(-(t - 0.02f) / 0.45f);
                float boom = Mathf.Sin(2 * Mathf.PI * 48f * t) * Env(t, 0.005f, 0.3f);
                d[i] = (crackle * 1.3f + buzz * gate) * e + boom * 0.9f;
            }
        }

        void MagicSound(float[] d)
        {
            for (int i = 0; i < d.Length; i++)
            {
                float t = T(i);
                float k = t / 1f;
                float f = Mathf.Lerp(330f, 1100f, k * k);
                float vib = 1f + 0.01f * Mathf.Sin(2 * Mathf.PI * 7f * t);
                float s = Mathf.Sin(2 * Mathf.PI * f * vib * t) * 0.3f + Mathf.Sin(2 * Mathf.PI * f * 1.5f * t) * 0.18f + Mathf.Sin(2 * Mathf.PI * f * 2.01f * t) * 0.1f;
                d[i] = s * Mathf.Sin(k * Mathf.PI) * 0.9f + Noise() * 0.03f * Mathf.Sin(k * Mathf.PI);
            }
        }

        void BlastSound(float[] d)
        {
            var lp = new OnePole(); var lp2 = new OnePole();
            for (int i = 0; i < d.Length; i++)
            {
                float t = T(i);
                float rumble = lp.Low(Noise(), Mathf.Lerp(2500f, 200f, Mathf.Clamp01(t / 1.2f))) * Env(t, 0.002f, 0.55f) * 2.2f;
                float boom = Mathf.Sin(2 * Mathf.PI * (45f * t - 8f * t * t)) * Env(t, 0.004f, 0.6f) * 1.6f;
                float hiss = (Noise() - lp2.Low(Noise(), 3000f)) * Env(t, 0.001f, 0.08f) * 0.6f;
                d[i] = rumble + boom + hiss;
            }
        }

        void CrumbleSound(float[] d)
        {
            for (int g = 0; g < 220; g++)
            {
                int start = (int)((float)rng.NextDouble() * d.Length * 0.85f);
                float freq = 2200f + (float)rng.NextDouble() * 5000f * (1f - start / (float)d.Length);
                float amp = 0.15f + (float)rng.NextDouble() * 0.25f;
                int len = (int)(Rate * 0.06f);
                for (int j = 0; j < len && start + j < d.Length; j++)
                {
                    float tt = j / (float)Rate;
                    d[start + j] += Mathf.Sin(2 * Mathf.PI * freq * tt) * Mathf.Exp(-tt * 70f) * amp;
                }
            }
            var lp = new OnePole();
            for (int i = 0; i < d.Length; i++)
            {
                float t = T(i);
                d[i] += lp.Low(Noise(), 500f) * Mathf.Sin(Mathf.Clamp01(t / 1.8f) * Mathf.PI) * 0.6f;
            }
        }

        void BellSound(float[] d)
        {
            float[] partials = { 1f, 2.0f, 2.4f, 3.0f, 4.2f, 5.4f };
            float[] amps = { 0.5f, 0.3f, 0.22f, 0.15f, 0.1f, 0.06f };
            for (int i = 0; i < d.Length; i++)
            {
                float t = T(i);
                float s = 0;
                for (int p = 0; p < partials.Length; p++)
                    s += Mathf.Sin(2 * Mathf.PI * 196f * partials[p] * t) * amps[p] * Mathf.Exp(-t * (0.9f + p * 0.7f));
                d[i] = s * Env(t, 0.003f, 10f);
            }
        }

        void GongSound(float[] d)
        {
            float[] partials = { 1f, 1.47f, 1.93f, 2.67f, 3.3f, 4.1f, 5.2f };
            var lp = new OnePole();
            for (int i = 0; i < d.Length; i++)
            {
                float t = T(i);
                float s = 0;
                for (int p = 0; p < partials.Length; p++)
                {
                    float wob = 1f + 0.003f * Mathf.Sin(2 * Mathf.PI * (0.7f + p * 0.3f) * t);
                    s += Mathf.Sin(2 * Mathf.PI * 82f * partials[p] * wob * t) * (0.5f / (p + 1)) * Mathf.Exp(-t * (0.4f + p * 0.25f));
                }
                s += lp.Low(Noise(), 400f) * Env(t, 0.002f, 0.2f) * 0.5f;
                d[i] = s * 1.3f;
            }
        }

        void ChoirSound(float[] d)
        {
            float[] chord = { 261.6f, 329.6f, 392f, 523.2f, 659.2f };
            for (int i = 0; i < d.Length; i++)
            {
                float t = T(i);
                float k = t / 2.6f;
                float e = Mathf.Sin(Mathf.Clamp01(k) * Mathf.PI);
                float s = 0;
                foreach (var f in chord)
                {
                    float vib = 1f + 0.006f * Mathf.Sin(2 * Mathf.PI * 5.5f * t + f);
                    s += (Mathf.Sin(2 * Mathf.PI * f * vib * t) + 0.3f * Mathf.Sin(2 * Mathf.PI * f * 2f * vib * t)) * 0.12f;
                    s += Mathf.Sin(2 * Mathf.PI * f * 1.004f * t) * 0.06f;
                }
                d[i] = s * e;
            }
        }

        void ClankSound(float[] d)
        {
            for (int i = 0; i < d.Length; i++)
            {
                float t = T(i);
                float s = Mathf.Sin(2 * Mathf.PI * 520f * t) * 0.3f + Mathf.Sin(2 * Mathf.PI * 1290f * t) * 0.25f + Mathf.Sin(2 * Mathf.PI * 2210f * t) * 0.15f;
                d[i] = s * Mathf.Exp(-t * 9f) + Noise() * Mathf.Exp(-t * 120f) * 0.6f;
            }
        }

        void ChargeSound(float[] d)
        {
            var lp = new OnePole();
            for (int i = 0; i < d.Length; i++)
            {
                float t = T(i);
                float k = t / 1f;
                float rumble = lp.Low(Noise(), 300f + 900f * k) * 1.4f;
                float hoof = Mathf.Sin(2 * Mathf.PI * 70f * t) * Mathf.Max(0, Mathf.Sin(2 * Mathf.PI * 7f * t)) * 0.5f;
                d[i] = (rumble + hoof) * Mathf.Sin(Mathf.Clamp01(k) * Mathf.PI);
            }
        }

        void FireLoop(float[] d)
        {
            var lp = new OnePole(); var lp2 = new OnePole();
            for (int i = 0; i < d.Length; i++)
            {
                float t = T(i);
                float roar = lp.Low(Noise(), 350f) * (0.7f + 0.3f * Mathf.Sin(2 * Mathf.PI * 0.25f * t));
                d[i] = roar * 0.8f;
            }
            for (int c = 0; c < 260; c++)
            {
                int start = rng.Next(d.Length - 2000);
                float amp = 0.2f + (float)rng.NextDouble() * 0.6f;
                for (int j = 0; j < 400; j++) d[start + j] += (Noise() - lp2.Low(Noise(), 1500f)) * Mathf.Exp(-j / 40f) * amp;
            }
            // make the loop seamless
            int x = Rate / 2;
            for (int i = 0; i < x; i++)
            {
                float a = i / (float)x;
                d[i] = d[i] * a + d[d.Length - x + i] * (1f - a);
            }
        }

        void MusicLoop(float[] d)
        {
            // Slow dark progression: Dm - Bb - Gm - A, low pad + distant choir + heartbeat drum.
            float[][] chords =
            {
                new[] { 73.42f, 110f, 146.83f, 174.61f, 220f },
                new[] { 58.27f, 116.54f, 146.83f, 174.61f, 233.08f },
                new[] { 49f, 98f, 146.83f, 196f, 233.08f },
                new[] { 55f, 110f, 138.59f, 164.81f, 220f },
            };
            float bar = d.Length / (float)chords.Length;
            var lpL = new OnePole();
            float[] phase = new float[12];
            for (int i = 0; i < d.Length; i++)
            {
                float t = T(i);
                int ci = (int)(i / bar);
                float local = (i - ci * bar) / bar;
                var ch = chords[ci % chords.Length];
                var next = chords[(ci + 1) % chords.Length];
                float xf = Mathf.SmoothStep(0, 1, Mathf.Clamp01((local - 0.88f) / 0.12f));
                float s = 0;
                for (int v = 0; v < ch.Length; v++)
                {
                    float f = Mathf.Lerp(ch[v], next[v], xf);
                    for (int det = 0; det < 2; det++)
                    {
                        int pi = v * 2 + det;
                        if (pi >= phase.Length) break;
                        phase[pi] += f * (det == 0 ? 0.997f : 1.003f) / Rate;
                        phase[pi] -= Mathf.Floor(phase[pi]);
                        s += (phase[pi] * 2f - 1f) * 0.07f;
                    }
                }
                float swell = 0.65f + 0.35f * Mathf.Sin(2 * Mathf.PI * t / 8f);
                float pad = lpL.Low(s, 380f + 220f * swell) * swell;
                // heartbeat drum every 2 seconds
                float beatT = t % 2f;
                float drum = Mathf.Sin(2 * Mathf.PI * (52f * beatT - 30f * beatT * beatT)) * Mathf.Exp(-beatT * 9f) * 0.5f;
                float beat2 = (t + 0.32f) % 2f;
                drum += Mathf.Sin(2 * Mathf.PI * 48f * beat2) * Mathf.Exp(-beat2 * 12f) * 0.25f * (beat2 < 0.5f ? 1 : 0);
                // choir shimmer on the top note
                float top = Mathf.Lerp(ch[ch.Length - 1], next[next.Length - 1], xf) * 2f;
                float choir = Mathf.Sin(2 * Mathf.PI * top * t * (1f + 0.004f * Mathf.Sin(2 * Mathf.PI * 5f * t))) * 0.035f * (0.5f + 0.5f * Mathf.Sin(2 * Mathf.PI * t / 16f));
                d[i] = pad * 1.4f + drum * 0.45f + choir;
            }
            int x = Rate;
            for (int i = 0; i < x; i++)
            {
                float a = i / (float)x;
                d[i] = d[i] * a + d[d.Length - x + i] * (1f - a);
            }
        }
    }
}

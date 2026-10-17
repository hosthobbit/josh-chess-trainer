using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using UnityEngine;
using Debug = UnityEngine.Debug;
#if UNITY_WEBGL && !UNITY_EDITOR
using System.Runtime.InteropServices;
#endif

namespace Kingsfall
{
    /// <summary>
    /// Runtime text-to-speech for the grandmaster. Desktop: the Piper neural voice copied into
    /// the build's StreamingAssets/Piper (Tools/piper in the editor), falling back to the system
    /// espeak-ng; lines are synthesised on a worker thread ahead of time. Browser: the browser's
    /// own speechSynthesis voice. With no voice at all the subtitles still show.
    /// </summary>
    public class Speech : MonoBehaviour
    {
        public static Speech I { get; private set; }
        public float Volume = 1f;

        /// <summary>Smoothed loudness of the current speech, 0..1, for mouth/gesture animation.</summary>
        public float Level { get; private set; }
        public bool Speaking => src && src.isPlaying;
        public string Engine { get; private set; } = "none";

        AudioSource src;
        readonly Dictionary<string, (float[] samples, int rate)> done = new Dictionary<string, (float[], int)>();
        readonly HashSet<string> failed = new HashSet<string>();
        readonly Queue<string> work = new Queue<string>();
        readonly Dictionary<string, AudioClip> clips = new Dictionary<string, AudioClip>();
        readonly object gate = new object();
        readonly float[] scope = new float[256];
        Thread worker;
        string piperDir, tempDir;
        bool checkedEngines;

        void Awake()
        {
            I = this;
            src = gameObject.AddComponent<AudioSource>();
            src.playOnAwake = false;
            src.spatialBlend = 0f;
            piperDir = Path.Combine(Application.streamingAssetsPath, "Piper");
#if UNITY_EDITOR
            if (!File.Exists(Path.Combine(piperDir, "piper"))) piperDir = Path.GetFullPath(Path.Combine(Application.dataPath, "../Tools/piper"));
#endif
#if UNITY_WEBGL && !UNITY_EDITOR
            Engine = WebSpeechAvailable() == 1 ? "browser" : "none";
            return;
#endif
            tempDir = Path.Combine(Application.temporaryCachePath, "speech");
            Directory.CreateDirectory(tempDir);
        }

        void Update()
        {
            src.volume = Volume;
            float target = 0f;
#if UNITY_WEBGL && !UNITY_EDITOR
            if (webSpeaking)
                target = 0.3f + 0.5f * Mathf.Clamp01(1f - WebSpeechSinceWord() / 260f) + 0.1f * Mathf.Sin(Time.unscaledTime * 17f);
#endif
            if (src.isPlaying)
            {
                src.GetOutputData(scope, 0);
                float sum = 0f;
                foreach (var s in scope) sum += s * s;
                target = Mathf.Clamp01(Mathf.Sqrt(sum / scope.Length) * 6f);
            }
            Level = Mathf.Lerp(Level, target, 1f - Mathf.Exp(-Time.unscaledDeltaTime * (target > Level ? 30f : 10f)));
        }

        /// <summary>Queue lines for synthesis so they are ready when needed.</summary>
        public void Prepare(IEnumerable<string> lines)
        {
            if (!EngineJob.Threaded) return;
            lock (gate)
            {
                foreach (var l in lines)
                    if (!string.IsNullOrWhiteSpace(l) && !done.ContainsKey(l) && !failed.Contains(l) && !work.Contains(l)) work.Enqueue(l);
                if (worker == null)
                {
                    worker = new Thread(Run) { IsBackground = true, Name = "Speech" };
                    worker.Start();
                }
                Monitor.Pulse(gate);
            }
        }

        int serial;

        /// <summary>Stops speech, including any line still waiting to be synthesised.</summary>
        public void Stop()
        {
            serial++;
            if (src) src.Stop();
#if UNITY_WEBGL && !UNITY_EDITOR
            if (webSpeaking || Engine == "browser") WebSpeechStop();
            webSpeaking = false;
#endif
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")] static extern int WebSpeechAvailable();
        [DllImport("__Internal")] static extern int WebSpeak(string text);
        [DllImport("__Internal")] static extern int WebSpeechState();
        [DllImport("__Internal")] static extern float WebSpeechSinceWord();
        [DllImport("__Internal")] static extern void WebSpeechStop();
        bool webSpeaking;

        IEnumerator SayInBrowser(string text, int id)
        {
            if (Engine != "browser" || Volume <= 0.01f || AudioListener.volume <= 0.01f || WebSpeak(text) == 0) yield break;
            float t = 0f;
            while (WebSpeechState() == 0 && t < 3f && id == serial) { t += Time.unscaledDeltaTime; yield return null; }
            if (WebSpeechState() != 1 || id != serial) yield break; // no voice actually started
            webSpeaking = true;
            LastSayHadAudio = true;
            while (WebSpeechState() == 1 && id == serial) yield return null;
            webSpeaking = false;
        }
#endif

        /// <summary>True if the last finished Say actually produced audio.</summary>
        public bool LastSayHadAudio { get; private set; }

        /// <summary>Speaks a line and returns when it has finished (or immediately if no voice is available).</summary>
        public IEnumerator Say(string text, float maxWait = 10f)
        {
            Stop();
            int id = serial;
            LastSayHadAudio = false;
            if (string.IsNullOrWhiteSpace(text)) yield break;
#if UNITY_WEBGL && !UNITY_EDITOR
            yield return SayInBrowser(text, id);
            yield break;
#endif
            Prepare(new[] { text });
            float t = 0f;
            while (t < maxWait)
            {
                lock (gate) if (done.ContainsKey(text) || failed.Contains(text)) break;
                t += Time.unscaledDeltaTime;
                yield return null;
            }
            if (id != serial) yield break;
            var clip = Clip(text);
            if (!clip) yield break;
            src.clip = clip;
            src.Play();
            LastSayHadAudio = true;
            while (src && src.isPlaying && src.clip == clip && id == serial) yield return null;
        }

        AudioClip Clip(string text)
        {
            if (clips.TryGetValue(text, out var c)) return c;
            (float[] samples, int rate) data;
            lock (gate) if (!done.TryGetValue(text, out data)) return null;
            c = AudioClip.Create("speech", data.samples.Length, 1, data.rate, false);
            c.SetData(data.samples, 0);
            clips[text] = c;
            return c;
        }

        // ------------------------------------------------------------ worker

        void Run()
        {
            while (true)
            {
                string line;
                lock (gate)
                {
                    while (work.Count == 0) Monitor.Wait(gate);
                    line = work.Dequeue();
                }
                (float[], int)? result = null;
                try { result = Synthesise(line); }
                catch (Exception e) { Debug.LogWarning("Speech: " + e.Message); }
                lock (gate)
                {
                    if (result.HasValue) done[line] = result.Value;
                    else failed.Add(line);
                }
            }
        }

        (float[], int)? Synthesise(string text)
        {
            string wav = Path.Combine(tempDir, $"line{Thread.CurrentThread.ManagedThreadId}_{Environment.TickCount & 0xffff}.wav");
            try
            {
                if (!checkedEngines)
                {
                    checkedEngines = true;
                    string piper = Path.Combine(piperDir, "piper");
                    if (File.Exists(piper)) RunProcess("chmod", $"+x \"{piper}\"", null, 5000);
                }
                if (Engine != "espeak" && TryPiper(text, wav)) { Engine = "piper"; return ReadWav(wav); }
                if (TryEspeak(text, wav)) { Engine = "espeak"; return ReadWav(wav); }
                return null;
            }
            finally { try { File.Delete(wav); } catch { } }
        }

        bool TryPiper(string text, string wav)
        {
            string exe = Path.Combine(piperDir, "piper");
            string model = Path.Combine(piperDir, "en_GB-alan-medium.onnx");
            if (!File.Exists(exe) || !File.Exists(model)) return false;
            var args = $"-m \"{model}\" --espeak_data \"{Path.Combine(piperDir, "espeak-ng-data")}\" -f \"{wav}\" -q --length_scale 1.08 --sentence_silence 0.35";
            return RunProcess(exe, args, text, 30000) && File.Exists(wav) && new FileInfo(wav).Length > 1000;
        }

        bool TryEspeak(string text, string wav)
        {
            foreach (var exe in new[] { "/usr/bin/espeak-ng", "/usr/local/bin/espeak-ng", "/usr/bin/espeak" })
            {
                if (!File.Exists(exe)) continue;
                string safe = text.Replace("\"", "'");
                if (RunProcess(exe, $"-v en-gb+m3 -s 150 -p 38 -w \"{wav}\" \"{safe}\"", null, 20000) && File.Exists(wav)) return true;
            }
            return false;
        }

        bool RunProcess(string exe, string args, string stdin, int timeoutMs)
        {
            var psi = new ProcessStartInfo(exe, args)
            {
                UseShellExecute = false,
                RedirectStandardInput = stdin != null,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = piperDir,
            };
            psi.EnvironmentVariables["LD_LIBRARY_PATH"] = piperDir;
            using (var p = Process.Start(psi))
            {
                if (p == null) return false;
                if (stdin != null)
                {
                    p.StandardInput.WriteLine(stdin);
                    p.StandardInput.Close();
                }
                // drain pipes so the child can't block on a full buffer
                p.OutputDataReceived += (s, e) => { };
                p.ErrorDataReceived += (s, e) => { };
                p.BeginOutputReadLine();
                p.BeginErrorReadLine();
                if (!p.WaitForExit(timeoutMs)) { try { p.Kill(); } catch { } return false; }
                return p.ExitCode == 0;
            }
        }

        static (float[], int)? ReadWav(string path)
        {
            var bytes = File.ReadAllBytes(path);
            if (bytes.Length < 44) return null;
            int channels = 1, rate = 22050, bits = 16;
            int pos = 12;
            while (pos + 8 <= bytes.Length)
            {
                string id = System.Text.Encoding.ASCII.GetString(bytes, pos, 4);
                int size = BitConverter.ToInt32(bytes, pos + 4);
                int body = pos + 8;
                if (id == "fmt ")
                {
                    channels = BitConverter.ToInt16(bytes, body + 2);
                    rate = BitConverter.ToInt32(bytes, body + 4);
                    bits = BitConverter.ToInt16(bytes, body + 14);
                }
                else if (id == "data")
                {
                    if (bits != 16) return null;
                    if (size <= 0 || body + size > bytes.Length) size = bytes.Length - body; // streamed WAVs may leave size unset
                    int frames = size / (2 * channels);
                    var samples = new float[frames];
                    for (int i = 0; i < frames; i++)
                        samples[i] = BitConverter.ToInt16(bytes, body + i * 2 * channels) / 32768f;
                    return (samples, rate);
                }
                pos = body + size + (size & 1);
            }
            return null;
        }
    }
}

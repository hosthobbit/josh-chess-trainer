using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

namespace Kingsfall
{
    /// <summary>The shared online leaderboard (games-api/chess-scores.php on hosthobbit.com).</summary>
    public class Leaderboard : MonoBehaviour
    {
        public static Leaderboard I { get; private set; }
        public const string Url = "https://hosthobbit.com/games-api/chess-scores.php";
        static readonly string[] Levels = { "Squire", "Knight", "Warlord" };

        [Serializable] public class Entry { public string name; public int score; public string difficulty; public string result; public int moves; public int accuracy; public string date; }
        [Serializable] class TopList { public Entry[] scores; }
        [Serializable] class SubmitReply { public bool ok; public int rank; public string error; }

        public List<Entry> Top { get; private set; } = new List<Entry>();
        public bool Loading { get; private set; }
        public string Error { get; private set; } = "";
        public bool Visible;
        public string Highlight = ""; // name of the player's own new entry

        void Awake() { I = this; }

        public void Show()
        {
            Visible = true;
            StartCoroutine(Fetch());
        }

        public IEnumerator Fetch()
        {
            Loading = true;
            Error = "";
            using (var req = UnityWebRequest.Get(Url + "?top=20"))
            {
                req.timeout = 15;
                yield return req.SendWebRequest();
                Loading = false;
                if (req.result != UnityWebRequest.Result.Success) { Error = "Couldn't reach the leaderboard. Check the internet connection."; yield break; }
                try { Top = new List<Entry>(JsonUtility.FromJson<TopList>(req.downloadHandler.text).scores ?? new Entry[0]); }
                catch { Error = "The leaderboard sent something odd back."; }
            }
        }

        /// <summary>Score for a game against the computer. Must match chess_score() in the PHP.</summary>
        public static int Compute(GameScore g)
        {
            if (!g.Won && !g.Drew) return 0;
            double b = new[] { 100.0, 300.0, 800.0 }[g.Difficulty];
            double factor = g.Won ? 1.0 : 1.0 / 3.0;
            double score = b * factor;
            score += b * factor * 0.5 * (g.Accuracy / 100.0);
            if (g.Won) score += b * 0.25 * Math.Max(0.0, Math.Min(1.0, (60 - g.Moves) / 40.0));
            score += 5 * g.Captured;
            double help = Math.Max(0.25, 1.0 - 0.10 * g.Hints - 0.15 * g.Undos);
            return (int)Math.Round(score * help, MidpointRounding.AwayFromZero);
        }

        public static string Breakdown(GameScore g)
        {
            if (!g.Won && !g.Drew) return "Win or draw against the computer to earn a score.";
            float b = new[] { 100f, 300f, 800f }[g.Difficulty];
            float factor = g.Won ? 1f : 1f / 3f;
            var parts = new List<string> { $"{(g.Won ? "Win" : "Draw")} vs {Levels[g.Difficulty]} {Mathf.RoundToInt(b * factor)}" };
            parts.Add($"accuracy {g.Accuracy}% +{Mathf.RoundToInt(b * factor * 0.5f * g.Accuracy / 100f)}");
            if (g.Won && g.Moves < 60) parts.Add($"quick win +{Mathf.RoundToInt(b * 0.25f * Mathf.Clamp01((60 - g.Moves) / 40f))}");
            if (g.Captured > 0) parts.Add($"captures +{5 * g.Captured}");
            int penalty = Mathf.RoundToInt((1f - Mathf.Max(0.25f, 1f - 0.10f * g.Hints - 0.15f * g.Undos)) * 100f);
            if (penalty > 0) parts.Add($"help used −{penalty}%");
            return string.Join("  ·  ", parts);
        }

        public IEnumerator Submit(string name, GameScore g, Action<bool, string> done)
        {
            var form = new WWWForm();
            form.AddField("name", name);
            form.AddField("score", Compute(g));
            form.AddField("difficulty", g.Difficulty);
            form.AddField("result", g.Won ? "win" : "draw");
            form.AddField("moves", g.Moves);
            form.AddField("accuracy", g.Accuracy);
            form.AddField("captured", g.Captured);
            form.AddField("hints", g.Hints);
            form.AddField("undos", g.Undos);
            using (var req = UnityWebRequest.Post(Url, form))
            {
                req.timeout = 15;
                yield return req.SendWebRequest();
                SubmitReply reply = null;
                try { reply = JsonUtility.FromJson<SubmitReply>(req.downloadHandler.text); } catch { }
                if (reply != null && reply.ok) { Highlight = name; done(true, $"You're number {reply.rank} on the leaderboard!"); }
                else if (reply != null && !string.IsNullOrEmpty(reply.error)) done(false, reply.error);
                else done(false, "Couldn't reach the leaderboard. Check the internet connection.");
            }
        }
    }

    public class GameScore
    {
        public bool Won, Drew;
        public int Difficulty, Moves, Accuracy, Captured, Hints, Undos;
    }
}

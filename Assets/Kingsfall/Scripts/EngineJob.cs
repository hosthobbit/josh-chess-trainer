using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Kingsfall
{
    /// <summary>
    /// Runs engine work off the main thread on desktop. Browsers (WebGL) have no threads, so there
    /// the job is queued and run on the main thread by Pump() on a later frame, which lets the
    /// "plotting…" banner draw before the search briefly holds the frame.
    /// </summary>
    public static class EngineJob
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        public const bool Threaded = false;
#else
        public const bool Threaded = true;
#endif
        static readonly Queue<Action> queue = new Queue<Action>();

        public static Task<T> Start<T>(Func<T> work)
        {
            if (Threaded) return Task.Run(work);
            var tcs = new TaskCompletionSource<T>();
            queue.Enqueue(() =>
            {
                try { tcs.SetResult(work()); }
                catch (Exception e) { tcs.SetException(e); }
            });
            return tcs.Task;
        }

        /// <summary>Main-thread fallback: runs one queued job. Returns true if it ran one.</summary>
        public static bool Pump()
        {
            if (queue.Count == 0) return false;
            queue.Dequeue()();
            return true;
        }

        public static void Clear() => queue.Clear();
        public static bool Idle => queue.Count == 0;
    }
}

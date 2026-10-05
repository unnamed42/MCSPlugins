using System;
using System.Collections.Concurrent;

namespace McsMCP.Server
{
    /// <summary>
    /// Dispatches actions to the Unity main thread
    /// Unity objects can only be accessed from the main thread
    /// </summary>
    public static class UnityMainThreadDispatcher
    {
        private static readonly ConcurrentQueue<Action> _executionQueue = new ConcurrentQueue<Action>();
        private static bool _initialized = false;

        /// <summary>
        /// Initialize the dispatcher
        /// </summary>
        public static void Initialize()
        {
            _initialized = true;
        }

        /// <summary>
        /// Drops the queue and clears the initialized flag. Called during mod teardown so that
        /// closures queued by the outgoing load (which capture its assembly) are not left to run
        /// after a hot reload, and so the old assembly is not kept alive by the queue.
        /// </summary>
        public static void Shutdown()
        {
            while (_executionQueue.TryDequeue(out _)) { }
            _initialized = false;
        }
        /// <summary>
        /// Queue an action to be executed on the Unity main thread
        /// </summary>
        public static void Enqueue(Action action)
        {
            if (action == null) return;

            // Dropping work queued after Shutdown() is deliberate, not merely defensive: the queue
            // is drained at teardown because its closures capture the OUTGOING load's assembly, and
            // accepting new items would pin that assembly alive and keep running its code.
            // Before Initialize() nothing can be draining the queue either, so the same guard
            // applies - and it is what makes _initialized a real check rather than a write-only flag.
            if (!_initialized) return;

            _executionQueue.Enqueue(action);
        }

        /// <summary>
        /// Process the queue - should be called from MelonMod.OnUpdate()
        /// </summary>
        public static void ProcessQueue()
        {
            // Process all queued actions
            int processed = 0;
            while (_executionQueue.TryDequeue(out var action) && processed < 100)
            {
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    McsMCPPlugin.Log?.LogError($"Error executing queued action: {ex}");
                }
                processed++;
            }
        }
    }
}

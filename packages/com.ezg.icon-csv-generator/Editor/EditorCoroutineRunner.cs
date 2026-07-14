#nullable enable

namespace Ezg.IconCsvGenerator.Editor
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using UnityEditor;

    /// <summary>
    /// Minimal coroutine runner for Editor-only code.
    /// Drives <see cref="IEnumerator"/> coroutines using <see cref="EditorApplication.update"/>
    /// so they work outside Play Mode (no MonoBehaviour required).
    ///
    /// Supports NESTED coroutines: when a coroutine does <c>yield return someOtherIEnumerator</c>
    /// (e.g. GenerateBatch → GenerateOne → GeminiImageClient.RequestImage), the inner coroutine is
    /// pumped to completion before the outer one resumes — matching Unity's StartCoroutine semantics.
    /// Any other yielded value (e.g. <c>yield return null</c>) simply waits one Editor tick.
    ///
    /// NOTE (domain reload): a script recompile clears the static state below; in-flight coroutines
    /// are dropped. IconGeneratorWindow.OnEnable recovers any rows left mid-flight.
    /// </summary>
    internal static class EditorCoroutineRunner
    {
        // Each running coroutine is a call-stack of IEnumerators (top = currently executing frame).
        private static readonly List<Stack<IEnumerator>> ActiveCoroutines = new();

        static EditorCoroutineRunner()
        {
            EditorApplication.update += Tick;
        }

        /// <summary>Enqueues a coroutine to be stepped on every Editor update tick.</summary>
        public static void StartCoroutine(IEnumerator routine)
        {
            if (routine == null) return;

            var stack = new Stack<IEnumerator>();
            stack.Push(routine);
            ActiveCoroutines.Add(stack);
        }

        private static void Tick()
        {
            if (ActiveCoroutines.Count == 0) return;

            // Iterate backwards so finished coroutines can be removed safely mid-loop.
            for (var i = ActiveCoroutines.Count - 1; i >= 0; i--)
            {
                if (!Step(ActiveCoroutines[i]))
                {
                    ActiveCoroutines.RemoveAt(i);
                }
            }
        }

        /// <summary>
        /// Advances one coroutine call-stack by a single step.
        /// Returns <c>false</c> when the entire stack has completed (the caller removes it).
        /// </summary>
        private static bool Step(Stack<IEnumerator> stack)
        {
            if (stack.Count == 0) return false;

            var current = stack.Peek();

            bool moved;
            try
            {
                moved = current.MoveNext();
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError($"[EditorCoroutineRunner] Coroutine threw an exception: {ex}");
                return false; // abandon the whole stack on error
            }

            if (!moved)
            {
                // Current frame finished — pop and let the parent (if any) resume next tick.
                stack.Pop();
                return stack.Count > 0;
            }

            // Nested-coroutine support: if the frame yielded another IEnumerator, push it so it
            // runs to completion before the parent resumes. Other yields (null, etc.) wait a tick.
            if (current.Current is IEnumerator nested)
            {
                stack.Push(nested);
            }

            return true;
        }
    }
}

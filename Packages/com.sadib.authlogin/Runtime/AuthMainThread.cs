using System;
using System.Collections.Generic;
using UnityEngine;

namespace SadibTools.AuthLogin
{
    /// <summary>
    /// Marshals native Android callbacks onto Unity's player loop thread.
    /// </summary>
    internal sealed class AuthMainThread : MonoBehaviour
    {
        private static AuthMainThread _instance;
        private readonly Queue<Action> _queue = new Queue<Action>();

        public static void Ensure(GameObject host)
        {
            if (_instance != null)
                return;

            _instance = host.GetComponent<AuthMainThread>();
            if (_instance == null)
                _instance = host.AddComponent<AuthMainThread>();
        }

        public static void Post(Action action)
        {
            if (action == null)
                return;

            if (_instance == null)
            {
                action();
                return;
            }

            lock (_instance._queue)
            {
                _instance._queue.Enqueue(action);
            }
        }

        private void Update()
        {
            lock (_queue)
            {
                while (_queue.Count > 0)
                    _queue.Dequeue()?.Invoke();
            }
        }

        private void OnDestroy()
        {
            if (_instance == this)
                _instance = null;
        }
    }
}

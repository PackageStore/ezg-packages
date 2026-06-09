using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace TigerForge
{
    [DisallowMultipleComponent]
    internal sealed class EventListenerComponent : MonoBehaviour
    {
        private readonly List<(string eventName, UnityAction callback)> _listeners = new();

        internal void Register(string eventName, UnityAction callback)
        {
            if (!_listeners.Contains((eventName, callback)))
                _listeners.Add((eventName, callback));
        }

        private void OnEnable()
        {
            foreach (var (name, cb) in _listeners)
                EventManager.StartListening(name, cb);
        }

        private void OnDisable()
        {
            foreach (var (name, cb) in _listeners)
                EventManager.StopListening(name, cb);
        }

        private void OnDestroy()
        {
            foreach (var (name, cb) in _listeners)
                EventManager.StopListening(name, cb);
            _listeners.Clear();
        }
    }
}

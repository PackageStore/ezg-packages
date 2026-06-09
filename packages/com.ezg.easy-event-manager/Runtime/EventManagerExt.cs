using UnityEngine;
using UnityEngine.Events;

namespace TigerForge
{
    public static class EventManagerExt
    {
        /// <summary>
        /// Registers a listener and automatically manages its lifecycle:
        /// unregisters on OnDisable, re-registers on OnEnable, fully cleans up on OnDestroy.
        /// Safe to call from Awake, Start, or OnEnable.
        /// </summary>
        public static void StartListening(this MonoBehaviour mb, string eventName, UnityAction callback)
        {
            EventManager.StartListening(eventName, callback);
            var tracker = mb.GetComponent<EventListenerComponent>()
                         ?? mb.gameObject.AddComponent<EventListenerComponent>();
            tracker.Register(eventName, callback);
        }
    }
}

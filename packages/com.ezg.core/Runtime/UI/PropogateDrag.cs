using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Ezg.Core.UI
{
    /// <summary>
    ///     Propagates drag events from a child element to the nearest parent scroll rect.
    /// </summary>
    public class PropogateDrag : MonoBehaviour, IPointerUpHandler
    {
        private bool isScroll;
        private ScrollRect scrollView;

        /// <summary>
        ///     Configures event forwarding to the parent scroll rect.
        /// </summary>
        private void Start()
        {
            scrollView = transform.GetComponentInParent<ScrollRect>();
            if (scrollView == null) return;
            var trigger = GetComponentInChildren<EventTrigger>();

            if (trigger == null) return;
            EventTrigger.Entry entryBegin = new(),
                entryDrag = new(),
                entryEnd = new(),
                entrypotential = new(),
                entryScroll = new();

            entryBegin.eventID = EventTriggerType.BeginDrag;
            entryBegin.callback.AddListener(data =>
            {
                scrollView.OnBeginDrag((PointerEventData)data);
                isScroll = true;
            });
            trigger.triggers.Add(entryBegin);

            entryDrag.eventID = EventTriggerType.Drag;
            entryDrag.callback.AddListener(data => { scrollView.OnDrag((PointerEventData)data); });
            trigger.triggers.Add(entryDrag);

            entryEnd.eventID = EventTriggerType.EndDrag;
            entryEnd.callback.AddListener(data => { scrollView.OnEndDrag((PointerEventData)data); });
            trigger.triggers.Add(entryEnd);

            entrypotential.eventID = EventTriggerType.InitializePotentialDrag;
            entrypotential.callback.AddListener(data =>
            {
                scrollView.OnInitializePotentialDrag((PointerEventData)data);
            });
            trigger.triggers.Add(entrypotential);

            entryScroll.eventID = EventTriggerType.Scroll;
            entryScroll.callback.AddListener(data =>
            {
                scrollView.OnScroll((PointerEventData)data);
                isScroll = true;
            });
            trigger.triggers.Add(entryScroll);
        }

        /// <inheritdoc />
        public async void OnPointerUp(PointerEventData eventData)
        {
            if (!isScroll) return;

            if (eventData.pointerPress.TryGetComponent<Button>(out var button))
            {
                button.interactable = false;
                await UniTask.DelayFrame(5);
                button.interactable = true;
            }

            isScroll = false;
        }
    }
}
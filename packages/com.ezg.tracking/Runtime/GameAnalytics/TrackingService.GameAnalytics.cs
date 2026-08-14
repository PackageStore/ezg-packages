using Cysharp.Threading.Tasks;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Ezg.Tracking
{
    /// <summary>
    ///     GameAnalytics half of the tracking engine — the product-analytics sink that runs in parallel with
    ///     Firebase.
    ///     <para>
    ///         This file is deliberately free of any reference to the GameAnalytics SDK. Delivery goes through
    ///         <see cref="IGameAnalyticsSink" />, implemented by the optional <c>Ezg.Tracking.GameAnalytics</c>
    ///         assembly, which only compiles when the <c>EZG_GAMEANALYTICS</c> scripting define is present. In a
    ///         project without GameAnalytics no sink registers, every call below is a cheap no-op, and the package
    ///         still compiles.
    ///     </para>
    ///     <para>
    ///         Two problems the SDK does not solve are handled here:
    ///         <list type="bullet">
    ///             <item>
    ///                 Events sent before the SDK finishes initializing are discarded by GameAnalytics with only a
    ///                 log line. They are queued here instead and replayed once the sink reports ready.
    ///             </item>
    ///             <item>
    ///                 Ids that break GameAnalytics' character/length rules are discarded the same way. Every id is
    ///                 repaired through <see cref="GameAnalyticsEventId" /> first.
    ///             </item>
    ///         </list>
    ///     </para>
    /// </summary>
    public static partial class TrackingService
    {
        #region Fields

        /// <summary>
        ///     Maximum number of events held while the sink is still initializing. Beyond this the oldest event is
        ///     dropped, so a sink that never becomes ready cannot grow memory without bound.
        /// </summary>
        private const int GA_QUEUE_CAPACITY = 128;

        private const float GA_READY_TIMEOUT_SECONDS = 120f;
        private const int GA_READY_POLL_MS = 250;

        private static readonly Queue<Action<IGameAnalyticsSink>> _gaQueue =
            new Queue<Action<IGameAnalyticsSink>>();

        private static bool _gaPumpRunning;

        private static string _gaUserId;

        /// <summary>
        ///     The active GameAnalytics transport, or null when the SDK is absent. Set through
        ///     <see cref="RegisterGameAnalyticsSink" />.
        /// </summary>
        public static IGameAnalyticsSink GameAnalyticsSink { get; private set; }

        #endregion

        #region Public Methods — Lifecycle

        /// <summary>
        ///     Whether GameAnalytics is present and finished initializing. While this is false, events are queued
        ///     rather than dropped.
        /// </summary>
        public static bool IsGameAnalyticsReady => GameAnalyticsSink != null && GameAnalyticsSink.IsReady;

        /// <summary>
        ///     Registers the transport that delivers events to GameAnalytics, and starts replaying anything queued
        ///     before it arrived. Called automatically by the optional <c>Ezg.Tracking.GameAnalytics</c> assembly;
        ///     projects only call this by hand when supplying their own sink (a fake in tests, for instance).
        /// </summary>
        /// <param name="sink">The transport to use, or null to detach the current one.</param>
        public static void RegisterGameAnalyticsSink(IGameAnalyticsSink sink)
        {
            GameAnalyticsSink = sink;

            if (sink == null)
            {
                return;
            }

            // Replay an id the game supplied before the sink existed — it has to reach the SDK before it
            // initializes, so it cannot wait in the event queue.
            if (_gaUserId != null)
            {
                Invoke(sink, s => s.SetUserId(_gaUserId));
            }

            if (sink.IsReady)
            {
                FlushGameAnalyticsQueue(sink);
                return;
            }

            PumpGameAnalyticsQueueAsync().Forget();
        }

        /// <summary>
        ///     Assigns the stable player id GameAnalytics attributes subsequent events to.
        ///     <para>
        ///         Unlike an event, this is <b>not</b> queued: GameAnalytics binds the custom id while it
        ///         initializes and ignores it afterwards, so waiting for the SDK to be ready would guarantee the id
        ///         arrives too late. Call it as early as the player id is known — ideally before the SDK starts.
        ///     </para>
        /// </summary>
        /// <param name="userId">The player id.</param>
        public static void SetGameAnalyticsUserId(string userId)
        {
            if (string.IsNullOrEmpty(userId) || userId == _gaUserId)
            {
                return;
            }

            _gaUserId = userId;

            IGameAnalyticsSink sink = GameAnalyticsSink;
            if (sink != null)
            {
                Invoke(sink, s => s.SetUserId(userId));
            }
        }

        #endregion

        #region Public Methods — Events

        /// <summary>
        ///     Sends a design event — the catch-all bucket for anything that is not a progression, resource,
        ///     business or ad event.
        /// </summary>
        /// <param name="eventId">
        ///     The event id, optionally hierarchical using ':' (e.g. <c>"tutorial:step:complete"</c>). Repaired
        ///     automatically if it breaks GameAnalytics' rules.
        /// </param>
        /// <param name="value">The optional numeric value attached to the event.</param>
        public static void SendGameAnalyticsDesign(string eventId, float? value = null)
        {
            if (!CanSendGameAnalytics)
            {
                return;
            }

            string safeId = GameAnalyticsEventId.Sanitize(eventId);
            if (safeId == null)
            {
                Debug.LogWarning($"[TrackingService] GameAnalytics design event skipped — unusable id '{eventId}'.");
                return;
            }

            Dispatch(sink => sink.SendDesign(safeId, value));
        }

        /// <summary>
        ///     Sends a design event whose id is built from parts. Parts that sanitize to nothing are dropped, so
        ///     optional segments can be passed straight through.
        /// </summary>
        /// <param name="value">The optional numeric value attached to the event.</param>
        /// <param name="parts">The event-id parts, in order.</param>
        public static void SendGameAnalyticsDesignParts(float? value, params string[] parts)
        {
            if (!CanSendGameAnalytics)
            {
                return;
            }

            string safeId = GameAnalyticsEventId.Join(parts);
            if (safeId == null)
            {
                Debug.LogWarning("[TrackingService] GameAnalytics design event skipped — no usable id part.");
                return;
            }

            Dispatch(sink => sink.SendDesign(safeId, value));
        }

        /// <summary>
        ///     Sends a progression event. This is the event Voodoo publishing reads for level start/complete/fail
        ///     funnels.
        /// </summary>
        /// <param name="status">Whether the step started, completed or failed.</param>
        /// <param name="progression01">The first (required) progression dimension, e.g. <c>"level_12"</c>.</param>
        /// <param name="progression02">The optional second dimension.</param>
        /// <param name="progression03">The optional third dimension.</param>
        /// <param name="score">
        ///     The optional score. GameAnalytics rejects a score on a <see cref="GaProgressionStatus.Start" />
        ///     event, so it is ignored there.
        /// </param>
        public static void SendGameAnalyticsProgression(GaProgressionStatus status, string progression01,
            string progression02 = null, string progression03 = null, int? score = null)
        {
            if (!CanSendGameAnalytics)
            {
                return;
            }

            string safe01 = GameAnalyticsEventId.SanitizePart(progression01);
            if (safe01 == null)
            {
                Debug.LogWarning(
                    $"[TrackingService] GameAnalytics progression event skipped — unusable progression01 '{progression01}'.");
                return;
            }

            string safe02 = GameAnalyticsEventId.SanitizePart(progression02);
            string safe03 = GameAnalyticsEventId.SanitizePart(progression03);

            // GameAnalytics only accepts a later dimension when the earlier one is present.
            if (safe02 == null)
            {
                safe03 = null;
            }

            // A score on a Start event fails validation and would discard the whole event.
            int? safeScore = status == GaProgressionStatus.Start ? null : score;

            Dispatch(sink => sink.SendProgression(status, safe01, safe02, safe03, safeScore));
        }

        /// <summary>
        ///     Sends a business event — a real-money purchase. This is what GameAnalytics' revenue reporting reads.
        /// </summary>
        /// <param name="currency">The ISO 4217 currency code, e.g. "USD".</param>
        /// <param name="amountInCents">
        ///     The price in the currency's <b>minor unit</b>. A $2.99 purchase is <c>299</c>, not <c>2.99</c>.
        /// </param>
        /// <param name="itemType">The product category, e.g. "gem_pack".</param>
        /// <param name="itemId">The store product identifier.</param>
        /// <param name="cartType">The place the purchase was triggered from, e.g. "shop".</param>
        public static void SendGameAnalyticsBusiness(string currency, int amountInCents, string itemType,
            string itemId, string cartType = null)
        {
            if (!CanSendGameAnalytics)
            {
                return;
            }

            if (string.IsNullOrEmpty(currency))
            {
                Debug.LogWarning("[TrackingService] GameAnalytics business event skipped — currency is empty.");
                return;
            }

            if (amountInCents <= 0)
            {
                Debug.LogWarning(
                    $"[TrackingService] GameAnalytics business event skipped — amount must be positive, got {amountInCents} (expected minor units, e.g. 299 for $2.99).");
                return;
            }

            string safeItemType = GameAnalyticsEventId.SanitizePart(itemType);
            string safeItemId = GameAnalyticsEventId.SanitizePart(itemId);
            if (safeItemType == null || safeItemId == null)
            {
                Debug.LogWarning(
                    $"[TrackingService] GameAnalytics business event skipped — unusable itemType '{itemType}' or itemId '{itemId}'.");
                return;
            }

            string safeCartType = GameAnalyticsEventId.SanitizePart(cartType);
            Dispatch(sink => sink.SendBusiness(currency, amountInCents, safeItemType, safeItemId, safeCartType));
        }

        /// <summary>
        ///     Sends a resource event — virtual currency granted to or spent by the player.
        ///     <para>
        ///         <b>Both <paramref name="currency" /> and <paramref name="itemType" /> must be declared in the
        ///         GameAnalytics settings asset before the SDK initializes</b>, otherwise the SDK discards the
        ///         event. See the package README.
        ///     </para>
        /// </summary>
        /// <param name="flow">Whether the resource was granted or spent.</param>
        /// <param name="currency">The virtual currency name, e.g. "Gold".</param>
        /// <param name="amount">The amount moved; must be greater than zero.</param>
        /// <param name="itemType">The item category, e.g. "Reward".</param>
        /// <param name="itemId">The item identifier, e.g. "daily_login".</param>
        public static void SendGameAnalyticsResource(GaResourceFlow flow, string currency, float amount,
            string itemType, string itemId)
        {
            if (!CanSendGameAnalytics)
            {
                return;
            }

            if (!(amount > 0f))
            {
                // GameAnalytics rejects zero/negative amounts; callers pass magnitude plus a flow direction.
                return;
            }

            string safeCurrency = GameAnalyticsEventId.SanitizePart(currency);
            string safeItemType = GameAnalyticsEventId.SanitizePart(itemType);
            string safeItemId = GameAnalyticsEventId.SanitizePart(itemId);

            if (safeCurrency == null || safeItemType == null || safeItemId == null)
            {
                Debug.LogWarning(
                    $"[TrackingService] GameAnalytics resource event skipped — unusable currency '{currency}', itemType '{itemType}' or itemId '{itemId}'.");
                return;
            }

            Dispatch(sink => sink.SendResource(flow, safeCurrency, amount, safeItemType, safeItemId));
        }

        /// <summary>
        ///     Sends an ad event.
        /// </summary>
        /// <param name="action">The ad lifecycle action.</param>
        /// <param name="adType">The ad format.</param>
        /// <param name="adSdkName">The mediation or network name, e.g. "applovin_max".</param>
        /// <param name="adPlacement">The in-game placement, e.g. "double_reward".</param>
        public static void SendGameAnalyticsAd(GaAdAction action, GaAdType adType, string adSdkName,
            string adPlacement)
        {
            if (!CanSendGameAnalytics)
            {
                return;
            }

            string safeSdk = GameAnalyticsEventId.SanitizePart(adSdkName);
            string safePlacement = GameAnalyticsEventId.SanitizePart(adPlacement);

            if (safeSdk == null || safePlacement == null)
            {
                Debug.LogWarning(
                    $"[TrackingService] GameAnalytics ad event skipped — unusable sdk '{adSdkName}' or placement '{adPlacement}'.");
                return;
            }

            Dispatch(sink => sink.SendAd(action, adType, safeSdk, safePlacement));
        }

        /// <summary>
        ///     Sends an error event.
        /// </summary>
        /// <param name="severity">The severity of the error.</param>
        /// <param name="message">The error message; truncated by the SDK past 8192 characters.</param>
        public static void SendGameAnalyticsError(GaErrorSeverity severity, string message)
        {
            if (!CanSendGameAnalytics || string.IsNullOrEmpty(message))
            {
                return;
            }

            Dispatch(sink => sink.SendError(severity, message));
        }

        #endregion

        #region Private Methods

        /// <summary>
        ///     Whether it is worth building an event at all.
        ///     <para>
        ///         Checked <b>before</b> any sanitizing so that a project without GameAnalytics pays nothing: no
        ///         string building, no closure, no queue growth. The sink is registered at
        ///         <see cref="UnityEngine.RuntimeInitializeLoadType.BeforeSceneLoad" />, i.e. before any scene code
        ///         runs, so a null sink here means the SDK is absent rather than late.
        ///     </para>
        /// </summary>
        private static bool CanSendGameAnalytics => IsTracking && GameAnalyticsSink != null;

        /// <summary>
        ///     Routes one already-validated call to the sink, or queues it while the sink is still initializing.
        /// </summary>
        private static void Dispatch(Action<IGameAnalyticsSink> call)
        {
            IGameAnalyticsSink sink = GameAnalyticsSink;

            if (sink == null || !IsTracking)
            {
                return;
            }

            if (sink.IsReady)
            {
                FlushGameAnalyticsQueue(sink);
                Invoke(sink, call);
                return;
            }

            Enqueue(call);
        }

        /// <summary>
        ///     Adds a call to the replay queue, dropping the oldest entry once the queue is full.
        /// </summary>
        private static void Enqueue(Action<IGameAnalyticsSink> call)
        {
            if (_gaQueue.Count >= GA_QUEUE_CAPACITY)
            {
                _gaQueue.Dequeue();
            }

            _gaQueue.Enqueue(call);
        }

        /// <summary>
        ///     Replays everything queued while the sink was initializing, in the order it was recorded.
        /// </summary>
        private static void FlushGameAnalyticsQueue(IGameAnalyticsSink sink)
        {
            while (_gaQueue.Count > 0)
            {
                Invoke(sink, _gaQueue.Dequeue());
            }
        }

        /// <summary>
        ///     Runs one sink call, keeping an SDK-side exception from propagating into gameplay code.
        /// </summary>
        private static void Invoke(IGameAnalyticsSink sink, Action<IGameAnalyticsSink> call)
        {
            try
            {
                call(sink);
            }
            catch (Exception e)
            {
                Debug.LogError($"[TrackingService] GameAnalytics event failed: {e}");
            }
        }

        /// <summary>
        ///     Waits for the sink to become ready and then replays the queue, so events recorded during startup are
        ///     not stranded when no further event arrives to trigger a flush. Gives up after
        ///     <see cref="GA_READY_TIMEOUT_SECONDS" /> — a sink that never initializes usually means the player
        ///     declined tracking consent.
        /// </summary>
        private static async UniTask PumpGameAnalyticsQueueAsync()
        {
            if (_gaPumpRunning)
            {
                return;
            }

            _gaPumpRunning = true;

            try
            {
                float deadline = Time.realtimeSinceStartup + GA_READY_TIMEOUT_SECONDS;

                while (Time.realtimeSinceStartup < deadline)
                {
                    IGameAnalyticsSink sink = GameAnalyticsSink;

                    if (sink == null)
                    {
                        return;
                    }

                    if (sink.IsReady)
                    {
                        FlushGameAnalyticsQueue(sink);
                        return;
                    }

                    await UniTask.Delay(GA_READY_POLL_MS, DelayType.UnscaledDeltaTime);
                }

                if (_gaQueue.Count > 0)
                {
                    Debug.LogWarning(
                        $"[TrackingService] GameAnalytics never became ready after {GA_READY_TIMEOUT_SECONDS:0}s — dropping {_gaQueue.Count} queued event(s).");
                    _gaQueue.Clear();
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[TrackingService] GameAnalytics queue pump failed: {e}");
            }
            finally
            {
                _gaPumpRunning = false;
            }
        }

        #endregion
    }
}

using GameAnalyticsSDK;
using UnityEngine;

namespace Ezg.Tracking.Sinks
{
    /// <summary>
    ///     Delivers <see cref="TrackingService" /> events to the GameAnalytics SDK.
    ///     <para>
    ///         This whole assembly only compiles when the <c>EZG_GAMEANALYTICS</c> scripting define is set, which
    ///         is how <c>com.ezg.tracking</c> stays usable in projects that do not ship GameAnalytics. It registers
    ///         itself automatically at startup, so a project never writes wiring code — installing the SDK and
    ///         setting the define is the entire integration.
    ///     </para>
    ///     <para>
    ///         Ids arriving here are already sanitized and validated by <see cref="TrackingService" />, so this
    ///         class is a straight translation layer: engine-neutral enums in, SDK calls out.
    ///     </para>
    /// </summary>
    public sealed class GameAnalyticsSink : IGameAnalyticsSink
    {
        #region Initialize

        /// <summary>
        ///     Registers this sink with <see cref="TrackingService" /> before any scene loads, so events raised
        ///     during startup are captured (queued by the engine) rather than lost.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void AutoRegister()
        {
            TrackingService.RegisterGameAnalyticsSink(new GameAnalyticsSink());
        }

        #endregion

        #region Public

        /// <inheritdoc />
        public bool IsReady => GameAnalytics.Initialized;

        /// <inheritdoc />
        public void SendDesign(string eventId, float? value)
        {
            if (value.HasValue)
            {
                GameAnalytics.NewDesignEvent(eventId, value.Value);
            }
            else
            {
                GameAnalytics.NewDesignEvent(eventId);
            }
        }

        /// <inheritdoc />
        public void SendProgression(GaProgressionStatus status, string progression01, string progression02,
            string progression03, int? score)
        {
            GAProgressionStatus gaStatus = ToGaStatus(status);

            if (progression02 == null)
            {
                if (score.HasValue)
                {
                    GameAnalytics.NewProgressionEvent(gaStatus, progression01, score.Value);
                }
                else
                {
                    GameAnalytics.NewProgressionEvent(gaStatus, progression01);
                }

                return;
            }

            if (progression03 == null)
            {
                if (score.HasValue)
                {
                    GameAnalytics.NewProgressionEvent(gaStatus, progression01, progression02, score.Value);
                }
                else
                {
                    GameAnalytics.NewProgressionEvent(gaStatus, progression01, progression02);
                }

                return;
            }

            if (score.HasValue)
            {
                GameAnalytics.NewProgressionEvent(gaStatus, progression01, progression02, progression03, score.Value);
            }
            else
            {
                GameAnalytics.NewProgressionEvent(gaStatus, progression01, progression02, progression03);
            }
        }

        /// <inheritdoc />
        public void SendBusiness(string currency, int amountInCents, string itemType, string itemId, string cartType)
        {
            // The SDK reads cartType but does not accept null for it.
            GameAnalytics.NewBusinessEvent(currency, amountInCents, itemType, itemId, cartType ?? string.Empty);
        }

        /// <inheritdoc />
        public void SendResource(GaResourceFlow flow, string currency, float amount, string itemType, string itemId)
        {
            GameAnalytics.NewResourceEvent(ToGaFlow(flow), currency, amount, itemType, itemId);
        }

        /// <inheritdoc />
        public void SendAd(GaAdAction action, GaAdType adType, string adSdkName, string adPlacement)
        {
            GameAnalytics.NewAdEvent(ToGaAdAction(action), ToGaAdType(adType), adSdkName, adPlacement);
        }

        /// <inheritdoc />
        public void SendError(GaErrorSeverity severity, string message)
        {
            GameAnalytics.NewErrorEvent(ToGaSeverity(severity), message);
        }

        /// <inheritdoc />
        public void SetUserId(string userId)
        {
            // GameAnalytics binds the custom id at initialization; setting it afterwards is ignored by the native
            // SDK. TrackingService therefore forwards this immediately instead of queueing it, and the value only
            // takes effect when the game supplies it before the SDK starts.
            GameAnalytics.SetCustomId(userId);
        }

        #endregion

        #region Private

        /// <summary>
        ///     Maps the engine's progression status onto the SDK's. Written out rather than cast so a future change
        ///     to either enum fails loudly at compile time instead of silently mislabelling data.
        /// </summary>
        private static GAProgressionStatus ToGaStatus(GaProgressionStatus status)
        {
            switch (status)
            {
                case GaProgressionStatus.Start: return GAProgressionStatus.Start;
                case GaProgressionStatus.Complete: return GAProgressionStatus.Complete;
                case GaProgressionStatus.Fail: return GAProgressionStatus.Fail;
                default: return GAProgressionStatus.Undefined;
            }
        }

        /// <summary>Maps the engine's resource flow direction onto the SDK's.</summary>
        private static GAResourceFlowType ToGaFlow(GaResourceFlow flow)
        {
            switch (flow)
            {
                case GaResourceFlow.Source: return GAResourceFlowType.Source;
                case GaResourceFlow.Sink: return GAResourceFlowType.Sink;
                default: return GAResourceFlowType.Undefined;
            }
        }

        /// <summary>Maps the engine's ad action onto the SDK's.</summary>
        private static GAAdAction ToGaAdAction(GaAdAction action)
        {
            switch (action)
            {
                case GaAdAction.Clicked: return GAAdAction.Clicked;
                case GaAdAction.Show: return GAAdAction.Show;
                case GaAdAction.FailedShow: return GAAdAction.FailedShow;
                case GaAdAction.RewardReceived: return GAAdAction.RewardReceived;
                case GaAdAction.Request: return GAAdAction.Request;
                case GaAdAction.Loaded: return GAAdAction.Loaded;
                default: return GAAdAction.Undefined;
            }
        }

        /// <summary>Maps the engine's ad format onto the SDK's.</summary>
        private static GAAdType ToGaAdType(GaAdType adType)
        {
            switch (adType)
            {
                case GaAdType.Video: return GAAdType.Video;
                case GaAdType.RewardedVideo: return GAAdType.RewardedVideo;
                case GaAdType.Playable: return GAAdType.Playable;
                case GaAdType.Interstitial: return GAAdType.Interstitial;
                case GaAdType.OfferWall: return GAAdType.OfferWall;
                case GaAdType.Banner: return GAAdType.Banner;
                case GaAdType.AppOpen: return GAAdType.AppOpen;
                default: return GAAdType.Undefined;
            }
        }

        /// <summary>Maps the engine's error severity onto the SDK's.</summary>
        private static GAErrorSeverity ToGaSeverity(GaErrorSeverity severity)
        {
            switch (severity)
            {
                case GaErrorSeverity.Debug: return GAErrorSeverity.Debug;
                case GaErrorSeverity.Info: return GAErrorSeverity.Info;
                case GaErrorSeverity.Warning: return GAErrorSeverity.Warning;
                case GaErrorSeverity.Error: return GAErrorSeverity.Error;
                case GaErrorSeverity.Critical: return GAErrorSeverity.Critical;
                default: return GAErrorSeverity.Undefined;
            }
        }

        #endregion
    }
}

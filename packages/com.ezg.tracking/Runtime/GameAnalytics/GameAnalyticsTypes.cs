namespace Ezg.Tracking
{
    /// <summary>
    ///     Progression status of a GameAnalytics progression event. Mirrors the SDK's own status enum so the
    ///     core engine never has to reference the GameAnalytics assembly.
    /// </summary>
    public enum GaProgressionStatus
    {
        /// <summary>The player started the progression step.</summary>
        Start = 1,

        /// <summary>The player finished the progression step successfully.</summary>
        Complete = 2,

        /// <summary>The player failed the progression step.</summary>
        Fail = 3
    }

    /// <summary>
    ///     Direction of a GameAnalytics resource event: resources granted to the player or taken from them.
    /// </summary>
    public enum GaResourceFlow
    {
        /// <summary>Resource granted to the player (earned, rewarded, purchased).</summary>
        Source = 1,

        /// <summary>Resource taken from the player (spent, consumed).</summary>
        Sink = 2
    }

    /// <summary>
    ///     Lifecycle action of a GameAnalytics ad event.
    /// </summary>
    public enum GaAdAction
    {
        /// <summary>The player clicked the ad.</summary>
        Clicked = 1,

        /// <summary>The ad was displayed.</summary>
        Show = 2,

        /// <summary>The ad failed to display.</summary>
        FailedShow = 3,

        /// <summary>The player earned the reward of a rewarded ad.</summary>
        RewardReceived = 4,

        /// <summary>An ad was requested from the network.</summary>
        Request = 5,

        /// <summary>An ad finished loading.</summary>
        Loaded = 6
    }

    /// <summary>
    ///     Format of the ad an <see cref="GaAdAction" /> refers to.
    /// </summary>
    public enum GaAdType
    {
        /// <summary>Non-rewarded video ad.</summary>
        Video = 1,

        /// <summary>Rewarded video ad.</summary>
        RewardedVideo = 2,

        /// <summary>Playable ad.</summary>
        Playable = 3,

        /// <summary>Interstitial ad.</summary>
        Interstitial = 4,

        /// <summary>Offer wall.</summary>
        OfferWall = 5,

        /// <summary>Banner ad.</summary>
        Banner = 6,

        /// <summary>App-open ad.</summary>
        AppOpen = 7
    }

    /// <summary>
    ///     Severity of a GameAnalytics error event.
    /// </summary>
    public enum GaErrorSeverity
    {
        /// <summary>Debug-level diagnostic.</summary>
        Debug = 1,

        /// <summary>Informational message.</summary>
        Info = 2,

        /// <summary>Recoverable problem.</summary>
        Warning = 3,

        /// <summary>Error that broke a feature.</summary>
        Error = 4,

        /// <summary>Failure that broke the session.</summary>
        Critical = 5
    }

    /// <summary>
    ///     Transport that actually delivers events to the GameAnalytics SDK.
    ///     <para>
    ///         The core <see cref="TrackingService" /> assembly deliberately does NOT reference GameAnalytics, so
    ///         that projects without the SDK still compile. The concrete implementation lives in the optional
    ///         <c>Ezg.Tracking.GameAnalytics</c> assembly, which only compiles when the
    ///         <c>EZG_GAMEANALYTICS</c> scripting define is present, and registers itself through
    ///         <see cref="TrackingService.RegisterGameAnalyticsSink" />.
    ///     </para>
    ///     <para>
    ///         Every string reaching a sink has already been sanitized by
    ///         <see cref="GameAnalyticsEventId" />, so implementations can forward values verbatim.
    ///     </para>
    /// </summary>
    public interface IGameAnalyticsSink
    {
        /// <summary>
        ///     Whether the underlying SDK finished initializing. Events sent before this turns true are queued by
        ///     <see cref="TrackingService" /> rather than dropped.
        /// </summary>
        bool IsReady { get; }

        /// <summary>Sends a design (catch-all) event.</summary>
        /// <param name="eventId">The sanitized, colon-separated event id.</param>
        /// <param name="value">The optional numeric value attached to the event.</param>
        void SendDesign(string eventId, float? value);

        /// <summary>Sends a progression event.</summary>
        /// <param name="status">Whether the step started, completed or failed.</param>
        /// <param name="progression01">The first (required) progression dimension.</param>
        /// <param name="progression02">The optional second progression dimension.</param>
        /// <param name="progression03">The optional third progression dimension.</param>
        /// <param name="score">The optional score achieved in the step.</param>
        void SendProgression(GaProgressionStatus status, string progression01, string progression02,
            string progression03, int? score);

        /// <summary>Sends a business (real-money purchase) event.</summary>
        /// <param name="currency">The ISO 4217 currency code, e.g. "USD".</param>
        /// <param name="amountInCents">The price in the currency's minor unit (cents).</param>
        /// <param name="itemType">The product category.</param>
        /// <param name="itemId">The product identifier.</param>
        /// <param name="cartType">The place the purchase was triggered from.</param>
        void SendBusiness(string currency, int amountInCents, string itemType, string itemId, string cartType);

        /// <summary>Sends a resource (virtual currency) event.</summary>
        /// <param name="flow">Whether the resource was granted or spent.</param>
        /// <param name="currency">The virtual currency name; must be declared in GameAnalytics settings.</param>
        /// <param name="amount">The amount moved; must be greater than zero.</param>
        /// <param name="itemType">The item category; must be declared in GameAnalytics settings.</param>
        /// <param name="itemId">The item identifier.</param>
        void SendResource(GaResourceFlow flow, string currency, float amount, string itemType, string itemId);

        /// <summary>Sends an ad event.</summary>
        /// <param name="action">The ad lifecycle action.</param>
        /// <param name="adType">The ad format.</param>
        /// <param name="adSdkName">The mediation or network name.</param>
        /// <param name="adPlacement">The in-game placement the ad was shown at.</param>
        void SendAd(GaAdAction action, GaAdType adType, string adSdkName, string adPlacement);

        /// <summary>Sends an error event.</summary>
        /// <param name="severity">The severity of the error.</param>
        /// <param name="message">The error message.</param>
        void SendError(GaErrorSeverity severity, string message);

        /// <summary>Assigns the stable player id used to attribute subsequent events.</summary>
        /// <param name="userId">The player id.</param>
        void SetUserId(string userId);
    }
}

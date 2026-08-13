using System.Collections.Generic;
using System.Text;

namespace Ezg.Tracking
{
    /// <summary>
    ///     Repairs strings so they satisfy GameAnalytics' event-id rules before they reach the SDK.
    ///     <para>
    ///         GameAnalytics validates every event id and <b>silently discards</b> the whole event when a rule is
    ///         broken — it only writes a line to the player log. Game code naturally produces ids that break the
    ///         rules (item names with accents, ids longer than the limit, level names containing ':'), so the
    ///         engine repairs ids up-front instead of losing the event.
    ///     </para>
    ///     <para>
    ///         The rules mirrored here come from the SDK's own validator:
    ///         an id is 1 to <see cref="MAX_PARTS" /> parts separated by ':', each part 1 to
    ///         <see cref="MAX_PART_LENGTH" /> characters, and only <c>A-Z a-z 0-9</c>, space and
    ///         <c>- _ . ( ) ! ?</c> are accepted.
    ///     </para>
    /// </summary>
    public static class GameAnalyticsEventId
    {
        #region Fields

        /// <summary>Maximum number of characters allowed in a single event-id part.</summary>
        public const int MAX_PART_LENGTH = 64;

        /// <summary>Maximum number of ':'-separated parts allowed in an event id.</summary>
        public const int MAX_PARTS = 5;

        /// <summary>Character GameAnalytics uses to separate event-id parts.</summary>
        public const char PART_SEPARATOR = ':';

        private const char REPLACEMENT = '_';

        #endregion

        #region Public Methods

        /// <summary>
        ///     Sanitizes a single event-id part: rejected characters become '_', and the result is truncated to
        ///     <see cref="MAX_PART_LENGTH" />.
        /// </summary>
        /// <param name="part">The raw part.</param>
        /// <returns>The sanitized part, or null when <paramref name="part" /> holds nothing usable.</returns>
        public static string SanitizePart(string part)
        {
            if (string.IsNullOrEmpty(part))
            {
                return null;
            }

            int length = part.Length < MAX_PART_LENGTH ? part.Length : MAX_PART_LENGTH;
            var builder = new StringBuilder(length);

            for (int i = 0; i < length; i++)
            {
                char c = part[i];
                builder.Append(IsAllowed(c) ? c : REPLACEMENT);
            }

            string result = builder.ToString().Trim();
            return result.Length == 0 ? null : result;
        }

        /// <summary>
        ///     Sanitizes a full event id: every ':'-separated part is repaired, empty parts are dropped, and at
        ///     most <see cref="MAX_PARTS" /> parts are kept.
        /// </summary>
        /// <param name="eventId">The raw event id.</param>
        /// <returns>The sanitized event id, or null when nothing usable remains.</returns>
        public static string Sanitize(string eventId)
        {
            if (string.IsNullOrEmpty(eventId))
            {
                return null;
            }

            string[] rawParts = eventId.Split(PART_SEPARATOR);
            return Join(rawParts);
        }

        /// <summary>
        ///     Sanitizes each part and joins the usable ones with ':'. Parts that sanitize to nothing are skipped,
        ///     so callers can pass optional segments without building the string themselves.
        /// </summary>
        /// <param name="parts">The raw parts, in order.</param>
        /// <returns>The joined event id, or null when no part is usable.</returns>
        public static string Join(params string[] parts)
        {
            if (parts == null || parts.Length == 0)
            {
                return null;
            }

            return JoinInternal(parts);
        }

        /// <summary>
        ///     Sanitizes each part of a sequence and joins the usable ones with ':'.
        /// </summary>
        /// <param name="parts">The raw parts, in order.</param>
        /// <returns>The joined event id, or null when no part is usable.</returns>
        public static string Join(IEnumerable<string> parts)
        {
            return parts == null ? null : JoinInternal(parts);
        }

        #endregion

        #region Private Methods

        /// <summary>
        ///     Shared join body: sanitize, drop unusable parts, stop at <see cref="MAX_PARTS" />.
        /// </summary>
        private static string JoinInternal(IEnumerable<string> parts)
        {
            var builder = new StringBuilder();
            int kept = 0;

            foreach (string rawPart in parts)
            {
                if (kept >= MAX_PARTS)
                {
                    break;
                }

                string part = SanitizePart(rawPart);
                if (part == null)
                {
                    continue;
                }

                if (kept > 0)
                {
                    builder.Append(PART_SEPARATOR);
                }

                builder.Append(part);
                kept++;
            }

            return kept == 0 ? null : builder.ToString();
        }

        /// <summary>
        ///     Whether GameAnalytics accepts a character inside an event-id part. Deliberately ASCII-only:
        ///     <c>char.IsLetterOrDigit</c> would let accented characters through, which the SDK rejects.
        /// </summary>
        private static bool IsAllowed(char c)
        {
            return (c >= 'A' && c <= 'Z')
                   || (c >= 'a' && c <= 'z')
                   || (c >= '0' && c <= '9')
                   || c == ' '
                   || c == '-'
                   || c == '_'
                   || c == '.'
                   || c == '('
                   || c == ')'
                   || c == '!'
                   || c == '?';
        }

        #endregion
    }
}

using System.Security.Cryptography;
using System.Text;

namespace Ezg.CheatGate
{
    /// <summary>
    ///     The id a test phone is listed under: the first 12 hex digits (upper case) of
    ///     SHA-256("ezg-cheat-gate:" + SystemInfo.deviceUniqueIdentifier), shown as XXXX-XXXX-XXXX. The raw identifier never
    ///     leaves the phone. Same definition as ezg-scopedregister/cheat-gate/src/devices.js -- change both or neither.
    ///     <para>
    ///         Android 8+: SystemInfo.deviceUniqueIdentifier follows ANDROID_ID, which differs per app signing key, so one
    ///         phone usually has a different id in each game. iOS: identifierForVendor, shared by the games of one developer
    ///         account until all of them are uninstalled. Either way: list the id each game shows.
    ///     </para>
    /// </summary>
    public static class CheatGateDeviceId
    {
        public const string Salt = "ezg-cheat-gate:";
        public const int Length = 12;
        public const string NotAvailable = "N/A";

        /// <summary>Unity's SystemInfo.unsupportedIdentifier.</summary>
        public const string UnsupportedIdentifier = "n/a";

        /// <summary>The id of a raw device identifier; "" when the platform has none (never matches a list).</summary>
        public static string FromIdentifier(string rawIdentifier)
        {
            if (string.IsNullOrWhiteSpace(rawIdentifier) || rawIdentifier == UnsupportedIdentifier) return string.Empty;
            byte[] hash;
            using (var sha = SHA256.Create())
            {
                hash = sha.ComputeHash(Encoding.UTF8.GetBytes(Salt + rawIdentifier));
            }

            var sb = new StringBuilder(Length);
            for (var i = 0; i < Length / 2; i++) sb.Append(hash[i].ToString("X2"));
            return sb.ToString();
        }

        /// <summary>12 upper-case hex digits from "7f3a-9c21-b04e" / "7F3A 9C21 B04E"; "" when it is not an id.</summary>
        public static string Normalize(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            var sb = new StringBuilder(Length);
            foreach (var c in value)
            {
                if (c == '-' || char.IsWhiteSpace(c)) continue;
                var upper = char.ToUpperInvariant(c);
                if (!((upper >= '0' && upper <= '9') || (upper >= 'A' && upper <= 'F'))) return string.Empty;
                if (sb.Length == Length) return string.Empty;
                sb.Append(upper);
            }

            return sb.Length == Length ? sb.ToString() : string.Empty;
        }

        /// <summary>XXXX-XXXX-XXXX, or "N/A" when <paramref name="id" /> is not an id.</summary>
        public static string Display(string id)
        {
            var normalized = Normalize(id);
            if (normalized.Length == 0) return NotAvailable;
            return normalized.Substring(0, 4) + "-" + normalized.Substring(4, 4) + "-" + normalized.Substring(8, 4);
        }
    }
}

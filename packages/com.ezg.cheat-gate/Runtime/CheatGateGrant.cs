using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

namespace Ezg.CheatGate
{
    /// <summary>What a valid grant says.</summary>
    public sealed class CheatGateClaims
    {
        public int Version;
        public string KeyId;
        public string Project;
        public string DeviceId;

        /// <summary>The nonce of the request the grant answered; null when the request had none.</summary>
        public string Nonce;

        public long IssuedAt;
        public long ExpiresAt;
    }

    /// <summary>
    ///     Grants issued by the ezg-cheat-gate Worker (ezg-scopedregister/cheat-gate/src/grant.js):
    ///     <c>&lt;payload&gt;.&lt;signature&gt;</c>, both base64url; payload =
    ///     <c>{"v":1,"kid":"k1","p":"SM007","d":"7F3A9C21B04E","n":"&lt;nonce&gt;","iat":...,"exp":...}</c>; signature = RSASSA-PKCS1-v1_5 /
    ///     SHA-256 over the ASCII bytes of the payload text. The signature is checked BEFORE the payload is read.
    /// </summary>
    public static class CheatGateGrant
    {
        public const int Version = 1;

        /// <summary>A phone clock this far behind the server still accepts a grant issued "in its future".</summary>
        public const long ClockSkewSeconds = 600;

        private const int MaxGrantLength = 4096;

        /// <summary>
        ///     Signature by one of <paramref name="keys" />, version, project and device. Time is NOT checked here
        ///     (<see cref="IsCurrent" />): a grant fresh from the server is trusted whatever the phone's clock says.
        /// </summary>
        public static bool TryVerify(string grant, IReadOnlyList<CheatGateKeys.Key> keys, string project, string deviceId, out CheatGateClaims claims)
        {
            claims = null;
            if (string.IsNullOrEmpty(grant) || grant.Length > MaxGrantLength || keys == null) return false;
            if (string.IsNullOrEmpty(project) || string.IsNullOrEmpty(deviceId)) return false;
            var dot = grant.IndexOf('.');
            if (dot <= 0 || dot == grant.Length - 1 || grant.IndexOf('.', dot + 1) >= 0) return false;
            var payload = grant.Substring(0, dot);
            if (!IsBase64Url(payload)) return false;
            var signature = FromBase64Url(grant.Substring(dot + 1));
            if (signature == null) return false;

            var signed = Encoding.ASCII.GetBytes(payload);
            string verifiedBy = null;
            foreach (var key in keys)
            {
                if (key == null || !key.IsUsable || !VerifyRsa(key, signed, signature)) continue;
                verifiedBy = key.Id;
                break;
            }

            if (verifiedBy == null) return false;

            var json = FromBase64Url(payload);
            if (json == null) return false;
            string text;
            try
            {
                text = new UTF8Encoding(false, true).GetString(json);
            }
            catch (ArgumentException)
            {
                return false;
            }

            var obj = CheatGateJson.ParseFlatObject(text);
            if (!CheatGateJson.TryGetLong(obj, "v", out var version) || version != Version) return false;
            if (!CheatGateJson.TryGetString(obj, "p", out var p) || p != project) return false;
            if (!CheatGateJson.TryGetString(obj, "d", out var d) || d != deviceId) return false;
            if (!CheatGateJson.TryGetLong(obj, "iat", out var iat) || !CheatGateJson.TryGetLong(obj, "exp", out var exp) || exp <= iat) return false;
            CheatGateJson.TryGetString(obj, "kid", out var kid);
            CheatGateJson.TryGetString(obj, "n", out var nonce);
            claims = new CheatGateClaims
            {
                Version = (int)version,
                KeyId = kid ?? verifiedBy,
                Project = p,
                DeviceId = d,
                Nonce = nonce,
                IssuedAt = iat,
                ExpiresAt = exp,
            };
            return true;
        }

        /// <summary>iat - skew &lt;= now &lt; exp.</summary>
        public static bool IsCurrent(CheatGateClaims claims, long nowSeconds)
        {
            return claims != null && nowSeconds + ClockSkewSeconds >= claims.IssuedAt && nowSeconds < claims.ExpiresAt;
        }

        private static bool VerifyRsa(CheatGateKeys.Key key, byte[] data, byte[] signature)
        {
            try
            {
                using (var rsa = RSA.Create())
                {
                    rsa.ImportParameters(new RSAParameters { Modulus = key.Modulus, Exponent = key.Exponent });
                    return rsa.VerifyData(data, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
                }
            }
            catch (Exception)
            {
                // CryptographicException, a platform without the algorithm... -- either way not a valid grant.
                return false;
            }
        }

        private static bool IsBase64Url(string value)
        {
            foreach (var c in value)
            {
                var ok = (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '-' || c == '_';
                if (!ok) return false;
            }

            return value.Length > 0;
        }

        /// <summary>Bytes of an unpadded base64url string; null when it is not one.</summary>
        public static byte[] FromBase64Url(string value)
        {
            if (string.IsNullOrEmpty(value) || !IsBase64Url(value) || value.Length % 4 == 1) return null;
            var b64 = value.Replace('-', '+').Replace('_', '/');
            switch (b64.Length % 4)
            {
                case 2: b64 += "=="; break;
                case 3: b64 += "="; break;
            }

            try
            {
                return Convert.FromBase64String(b64);
            }
            catch (FormatException)
            {
                return null;
            }
        }
    }
}

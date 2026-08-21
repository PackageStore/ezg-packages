#if UNITY_EDITOR
using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace Ezg.Editor.Shared.Firebase
{
    /// <summary>
    ///     Đọc file JSON key của service account rồi tự ký JWT RS256 để đổi lấy OAuth access token
    ///     (luồng <c>urn:ietf:params:oauth:grant-type:jwt-bearer</c>). Không cần <c>gcloud</c>, không cần
    ///     <c>firebase-tools</c>, không cần lib ngoài — chạy được cả trên máy dev chưa cài gì.
    ///
    ///     <para>
    ///         <b>Vì sao phải tự parse DER thay vì gọi <c>RSA.ImportPkcs8PrivateKey</c>:</b> API đó CÓ
    ///         trong BCL của Unity 6 (.NET Standard 2.1) nên compile trót lọt, nhưng Mono của Editor
    ///         throw <c>PlatformNotSupportedException</c> ngay khi gọi (đã kiểm trên mono 6.13 của
    ///         6000.3.16f1, 2026-08-20). Đường duy nhất chạy được là bóc PKCS#8 → <c>RSAParameters</c> →
    ///         <c>ImportParameters</c>; chữ ký sinh ra đã đối chiếu bằng <c>openssl dgst -verify</c>.
    ///         Đừng "dọn dẹp" hàm <see cref="ParsePkcs8" /> thành một dòng <c>ImportPkcs8PrivateKey</c>.
    ///     </para>
    ///     <para>
    ///         <b>Đường đi của secret:</b> private key chỉ tồn tại trong RAM của Editor. Không ghi ra
    ///         file trong repo, không đưa vào report, không log. Đường dẫn tới file key nằm ở EditorPrefs
    ///         (theo máy) chứ không nằm trong <c>ProjectSettings/</c> để không lỡ commit.
    ///     </para>
    /// </summary>
    internal static class FirebaseServiceAccount
    {
        #region Constants

        /// <summary>Quyền vừa đủ để tạo app + đọc config.</summary>
        internal const string SCOPE_FIREBASE = "https://www.googleapis.com/auth/firebase";

        /// <summary>Chỉ thêm khi phải TẠO project mới (Cloud Resource Manager đòi scope này).</summary>
        internal const string SCOPE_CLOUD_PLATFORM = "https://www.googleapis.com/auth/cloud-platform";

        private const string DEFAULT_TOKEN_URI = "https://oauth2.googleapis.com/token";

        /// <summary>Google cấp token 1 giờ; xin 55 phút để không kẹt vào lúc hết hạn.</summary>
        private const int TOKEN_LIFETIME_SECONDS = 3300;

        #endregion

        #region Types

        // JsonUtility gán mấy field dưới đây qua reflection nên compiler không thấy chỗ nào assign.
#pragma warning disable CS0649

        /// <summary>
        ///     Nội dung file key tải từ Google Cloud Console. Field đặt tên khớp KEY JSON (yêu cầu của
        ///     <see cref="JsonUtility" />) nên không theo chuẩn `_camelCase` của runtime code.
        /// </summary>
        [Serializable]
        internal class Key
        {
            public string type;
            public string project_id;
            public string client_email;
            public string private_key;
            public string token_uri;
        }

        [Serializable]
        private class TokenResponse
        {
            public string access_token;
            public int expires_in;
        }

#pragma warning restore CS0649

        #endregion

        #region Public API

        /// <summary>
        ///     Nạp + kiểm file key. Chặn luôn trường hợp file key nằm TRONG repo: key đó là chìa vào
        ///     project Firebase, commit lên là hỏng chuyện, mà lỗi này im lặng nên phải bắt ở đây.
        /// </summary>
        internal static bool TryLoad(string path, out Key key, out string error)
        {
            key = null;
            error = null;

            if (string.IsNullOrEmpty(path))
            {
                error = "Chua chon file service account key (.json).";
                return false;
            }

            if (!File.Exists(path))
            {
                error = $"Khong thay file key: {path}";
                return false;
            }

            var full = Path.GetFullPath(path).Replace('\\', '/');
            var projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "..")).Replace('\\', '/');
            if (full.StartsWith(projectRoot + "/", StringComparison.OrdinalIgnoreCase))
            {
                error = $"File key dang nam TRONG project ({full}). Chuyen ra ngoai repo "
                        + "(vi du ~/.config/ezg/firebase-admin.json) roi chon lai - de trong nay la som "
                        + "muon commit len git.";
                return false;
            }

            try
            {
                key = JsonUtility.FromJson<Key>(File.ReadAllText(path));
            }
            catch (Exception exception)
            {
                error = $"File key khong phai JSON hop le: {exception.Message}";
                return false;
            }

            if (key == null || string.IsNullOrEmpty(key.private_key) || string.IsNullOrEmpty(key.client_email))
            {
                error = "File key thieu 'private_key' hoac 'client_email' -> khong phai key service "
                        + "account. Can loai 'Service account key' (Cloud Console > IAM > Service "
                        + "Accounts > Keys > Add key > JSON), khong phai OAuth client secret.";
                return false;
            }

            return true;
        }

        /// <summary>Đổi key sang access token. <paramref name="scope" /> ngăn cách bằng dấu cách.</summary>
        internal static bool TryGetAccessToken(Key key, string scope, out string token, out string error)
        {
            token = null;

            if (!TrySign(key, scope, out var jwt, out error)) return false;

            var tokenUri = string.IsNullOrEmpty(key.token_uri) ? DEFAULT_TOKEN_URI : key.token_uri;
            var form = "grant_type=" + Uri.EscapeDataString("urn:ietf:params:oauth:grant-type:jwt-bearer")
                                     + "&assertion=" + Uri.EscapeDataString(jwt);

            if (!FirebaseRest.PostForm(tokenUri, form, "Dang xin access token...", out var body, out error))
            {
                error = "Xin access token that bai: " + error
                        + "\nThuong la do dong ho may lech > 5 phut, hoac service account da bi xoa/khoa key.";
                return false;
            }

            try
            {
                token = JsonUtility.FromJson<TokenResponse>(body)?.access_token;
            }
            catch (Exception exception)
            {
                error = "Khong doc duoc access token: " + exception.Message;
                return false;
            }

            if (!string.IsNullOrEmpty(token)) return true;

            error = "Google tra ve phan hoi khong co access_token.";
            return false;
        }

        #endregion

        #region JWT

        private static bool TrySign(Key key, string scope, out string jwt, out string error)
        {
            jwt = null;
            error = null;

            try
            {
                // JWT đòi giây UTC thật (guardrail [TIME] nhắm vào gameplay; TimeManager không tồn tại
                // trong Editor, và server Google là bên kiểm iat/exp nên phải là đồng hồ hệ thống).
                var now = (long)(DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc))
                    .TotalSeconds;
                var tokenUri = string.IsNullOrEmpty(key.token_uri) ? DEFAULT_TOKEN_URI : key.token_uri;

                var header = Base64Url(Encoding.UTF8.GetBytes("{\"alg\":\"RS256\",\"typ\":\"JWT\"}"));
                var claims = Base64Url(Encoding.UTF8.GetBytes(
                    "{\"iss\":\"" + key.client_email + "\""
                    + ",\"scope\":\"" + scope + "\""
                    + ",\"aud\":\"" + tokenUri + "\""
                    + ",\"iat\":" + now
                    + ",\"exp\":" + (now + TOKEN_LIFETIME_SECONDS) + "}"));

                var signingInput = Encoding.ASCII.GetBytes(header + "." + claims);

                using var rsa = RSA.Create();
                rsa.ImportParameters(ParsePkcs8(DecodePem(key.private_key)));
                var signature = rsa.SignData(signingInput, HashAlgorithmName.SHA256,
                    RSASignaturePadding.Pkcs1);

                jwt = header + "." + claims + "." + Base64Url(signature);
                return true;
            }
            catch (Exception exception)
            {
                error = $"Ky JWT that bai ({exception.GetType().Name}): {exception.Message}";
                return false;
            }
        }

        private static string Base64Url(byte[] data) =>
            Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        private static byte[] DecodePem(string pem)
        {
            var body = pem.Replace("-----BEGIN PRIVATE KEY-----", string.Empty)
                .Replace("-----END PRIVATE KEY-----", string.Empty)
                .Replace("\\n", string.Empty)
                .Replace("\n", string.Empty)
                .Replace("\r", string.Empty)
                .Trim();
            return Convert.FromBase64String(body);
        }

        #endregion

        #region PKCS#8 (DER)

        /// <summary>
        ///     PKCS#8 <c>PrivateKeyInfo</c> → <see cref="RSAParameters" />.
        ///     <code>
        ///     SEQUENCE { INTEGER version, SEQUENCE algorithm, OCTET STRING {
        ///         SEQUENCE { INTEGER version, n, e, d, p, q, dp, dq, iqmp } } }
        ///     </code>
        /// </summary>
        private static RSAParameters ParsePkcs8(byte[] der)
        {
            var index = 0;
            ExpectTag(der, ref index, 0x30);
            ReadLength(der, ref index);
            ReadInteger(der, ref index); // version của PrivateKeyInfo

            // Bỏ qua AlgorithmIdentifier (đã biết là rsaEncryption). PHẢI tách 2 câu lệnh: viết
            // `index += ReadLength(der, ref index)` là C# đọc `index` CŨ trước khi ReadLength kịp dịch nó
            // qua các byte length -> lệch đúng 1 byte, và lỗi hiện ra dưới dạng "cho tag 0x04, gap 0x00".
            ExpectTag(der, ref index, 0x30);
            var algorithmLength = ReadLength(der, ref index);
            index += algorithmLength;

            ExpectTag(der, ref index, 0x04);
            ReadLength(der, ref index);
            ExpectTag(der, ref index, 0x30);
            ReadLength(der, ref index);
            ReadInteger(der, ref index); // version của RSAPrivateKey

            var modulus = ReadInteger(der, ref index);
            var exponent = ReadInteger(der, ref index);
            var d = ReadInteger(der, ref index);
            var p = ReadInteger(der, ref index);
            var q = ReadInteger(der, ref index);
            var dp = ReadInteger(der, ref index);
            var dq = ReadInteger(der, ref index);
            var inverseQ = ReadInteger(der, ref index);

            // Mono đòi ĐÚNG độ dài: D bằng modulus, các thành phần CRT bằng nửa. Thiếu byte 0 ở đầu là
            // CryptographicException chung chung, không nói vì sao.
            var half = (modulus.Length + 1) / 2;
            return new RSAParameters
            {
                Modulus = modulus,
                Exponent = exponent,
                D = PadLeft(d, modulus.Length),
                P = PadLeft(p, half),
                Q = PadLeft(q, half),
                DP = PadLeft(dp, half),
                DQ = PadLeft(dq, half),
                InverseQ = PadLeft(inverseQ, half),
            };
        }

        private static void ExpectTag(byte[] der, ref int index, byte tag)
        {
            if (der[index] != tag)
                throw new CryptographicException($"DER: cho tag 0x{tag:X2}, gap 0x{der[index]:X2} tai {index}");
            index++;
        }

        private static int ReadLength(byte[] der, ref int index)
        {
            int first = der[index++];
            if ((first & 0x80) == 0) return first;

            var count = first & 0x7F;
            var value = 0;
            while (count-- > 0) value = (value << 8) | der[index++];
            return value;
        }

        /// <summary>Đọc INTEGER và bỏ byte 0x00 đệm dấu ở đầu (DER dùng số có dấu, RSA thì không).</summary>
        private static byte[] ReadInteger(byte[] der, ref int index)
        {
            ExpectTag(der, ref index, 0x02);
            var length = ReadLength(der, ref index);
            var value = new byte[length];
            Buffer.BlockCopy(der, index, value, 0, length);
            index += length;

            var zeros = 0;
            while (zeros < value.Length - 1 && value[zeros] == 0) zeros++;
            if (zeros == 0) return value;

            var trimmed = new byte[value.Length - zeros];
            Buffer.BlockCopy(value, zeros, trimmed, 0, trimmed.Length);
            return trimmed;
        }

        private static byte[] PadLeft(byte[] value, int size)
        {
            if (value.Length == size) return value;
            if (value.Length > size)
                throw new CryptographicException($"DER: thanh phan dai {value.Length} > {size} byte");

            var padded = new byte[size];
            Buffer.BlockCopy(value, 0, padded, size - value.Length, value.Length);
            return padded;
        }

        #endregion
    }
}
#endif

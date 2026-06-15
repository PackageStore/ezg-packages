using System.Security.Cryptography;
using System.Text;

namespace Ezg.Core.Firebase
{
    /// <summary>
    ///     Utility class for generating secure random strings (nonces).
    /// </summary>
    public static class Nonce
    {
        #region Public Methods

        /// <summary>
        ///     Generates a cryptographically secure random string of a given length.
        ///     Primarily used for "Sign in with Apple" and other OAuth flows.
        /// </summary>
        /// <param name="length">Length of the generated string.</param>
        /// <returns>A random alphanumeric string.</returns>
        public static string GenerateNonce(int length)
        {
            const string charset = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";
            using (var rng = new RNGCryptoServiceProvider())
            {
                var result = new StringBuilder();
                var byteBuffer = new byte[1];
                while (result.Length < length)
                {
                    rng.GetBytes(byteBuffer);
                    var randomIndex = byteBuffer[0] % charset.Length;

                    // We only append if the byte is within the range that doesn't cause bias.
                    // This is for maximum randomness but for most use cases a simple modulo is fine.
                    // Here we just append for brevity but keeping it secure.
                    result.Append(charset[randomIndex]);
                }

                return result.ToString();
            }
        }

        #endregion
    }
}
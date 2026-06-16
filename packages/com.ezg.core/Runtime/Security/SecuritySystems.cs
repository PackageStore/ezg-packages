using System;
using System.IO;
using System.Runtime.Serialization.Formatters.Binary;
using System.Security.Cryptography;

namespace Ezg.Core.Security
{
    /// <summary>
    ///     Utility helpers for generating file and object hashes.
    /// </summary>
    public static class SecuritySystems
    {
        /// <summary>
        ///     Generates an MD5 hash for a file.
        /// </summary>
        /// <param name="filePath">The path to the file to hash.</param>
        /// <returns>The lowercase hexadecimal MD5 hash string.</returns>
        public static string GenMD5File(string filePath)
        {
            using (var md5 = MD5.Create())
            {
                using (var stream = File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    var hash = md5.ComputeHash(stream);
                    return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
                }
            }
        }

        /// <summary>
        ///     Generates an MD5 hash for an object instance.
        /// </summary>
        /// <param name="input">The object to serialize and hash.</param>
        /// <returns>The uppercase hexadecimal MD5 hash string.</returns>
        public static string CreateMD5(object input)
        {
            using (var md5 = MD5.Create())
            {
                var bf = new BinaryFormatter();
                using (var ms = new MemoryStream())
                {
                    bf.Serialize(ms, input);
                    var hashBytes = md5.ComputeHash(ms.ToArray());

                    return BitConverter.ToString(hashBytes).Replace("-", "");
                }
            }
        }
    }
}
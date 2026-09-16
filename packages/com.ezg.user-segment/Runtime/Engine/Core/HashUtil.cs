using System;
using System.Security.Cryptography;
using System.Text;

namespace Ezg.UserSegment.Engine
{
    /// <summary>Hash assignment / holdout — §C.5.1: uint32_BE(SHA256(user_id + ":" + key)[0..3]) mod 10000.</summary>
    public static class HashUtil
    {
        public const int BUCKETS = 10000;

        public static int Bucket(string userId, string key)
        {
            using (var sha = SHA256.Create())
            {
                var h = sha.ComputeHash(Encoding.UTF8.GetBytes(userId + ":" + key));
                var v = ((uint)h[0] << 24) | ((uint)h[1] << 16) | ((uint)h[2] << 8) | h[3];
                return (int)(v % BUCKETS);
            }
        }

        public static string NewUserId() => Guid.NewGuid().ToString("N");
    }
}

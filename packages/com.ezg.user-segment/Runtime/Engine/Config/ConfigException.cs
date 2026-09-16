using System;

namespace Ezg.UserSegment.Engine
{
    /// <summary>Lý do config_rejected — §C.8.1.</summary>
    public static class RejectReason
    {
        public const string Parse = "parse";
        public const string Schema = "schema";
        public const string MinSdk = "min_sdk";
        public const string GameEnvMismatch = "game_env_mismatch";
        public const string Ref = "ref";
        public const string Type = "type";
        public const string Cycle = "cycle";
        public const string Limit = "limit";
    }

    /// <summary>Config bị từ chối toàn bộ. <see cref="Reason" /> ∈ <see cref="RejectReason" />.</summary>
    public sealed class ConfigException : Exception
    {
        public string Reason { get; }

        public ConfigException(string reason, string message) : base($"[{reason}] {message}")
        {
            Reason = reason;
        }
    }
}

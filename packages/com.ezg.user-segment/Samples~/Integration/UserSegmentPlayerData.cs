using Ezg.Package.Factory;

namespace Ezg.Feature.System.UserSegment
{
    /// <summary>
    ///     Accessor cached cho module <see cref="PlayerUserSegment" />. Nằm trong module thay vì PlayerDataManager để core
    ///     không phụ thuộc module; cache tại đây giữ đúng tinh thần "không gọi GetModule mỗi lần".
    /// </summary>
    public static class UserSegmentPlayerData
    {
        private static PlayerUserSegment _module;

        public static PlayerUserSegment Module => _module ??= DataPlayer.GetModule<PlayerUserSegment>();

        /// <summary>Gọi khi DataPlayer re-init (đổi account) để lấy instance mới.</summary>
        public static void ClearCache() => _module = null;
    }
}

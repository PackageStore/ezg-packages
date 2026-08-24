#if UNITY_EDITOR
namespace EZG.TechnicalArt.VisualRoadBuilder
{
    /// <summary>Dải mesh khối phủ lên 1 hàng đường, đơn vị NỬA Ô — gom hết mọi khối rồi mới tính
    /// phần lấp ở mép (<see cref="BlockEdgeFiller"/>). <see cref="Side"/> = nửa đường dải
    /// này ăn: 2 dải khác nửa KHÔNG gộp được vì chúng lấp mép độc lập nhau.</summary>
    internal readonly struct BlockStrip
    {
        internal readonly bool Horizontal;
        internal readonly int Line2;
        internal readonly int Lo2;
        internal readonly int Hi2;
        internal readonly int Side;

        internal BlockStrip(bool horizontal, int line2, int lo2, int hi2, int side)
        {
            Horizontal = horizontal;
            Line2 = line2;
            Lo2 = lo2;
            Hi2 = hi2;
            Side = side;
        }
    }
}
#endif

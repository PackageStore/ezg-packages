using Ezg.Package.Factory;

/// <summary>Tầng lịch sử action của User Segmentation — blob JSON đi theo save của game (§4.5).</summary>
public class PlayerUserSegmentData : DataBase
{
    /// <summary>{ v, uid, actions: { id: { n, f, l } } } — SDK ghi lúc selected; game không sửa tay.</summary>
    public string HistoryBlob = "";
}

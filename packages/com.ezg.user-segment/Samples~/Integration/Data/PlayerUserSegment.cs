using Ezg.Package.Factory;
using Ezg.UserSegment;

/// <summary>
///     Module player data giữ lịch sử action (one-shot, sàn cooldown) cho SDK User Segmentation qua
///     <see cref="IActionHistoryStore" />. Save() ngay khi SDK ghi — blob ≤ 5 KB nên chấp nhận.
/// </summary>
public class PlayerUserSegment : DataPlayerBaseGeneric<PlayerUserSegmentData>, IActionHistoryStore
{
    public string Load()
    {
        return dataBase?.HistoryBlob;
    }

    public void Save(string blob)
    {
        dataBase.HistoryBlob = blob ?? "";
        Save();
    }

    protected override void SetupDefaultData()
    {
        base.SetupDefaultData();
        dataBase.HistoryBlob = "";
    }
}

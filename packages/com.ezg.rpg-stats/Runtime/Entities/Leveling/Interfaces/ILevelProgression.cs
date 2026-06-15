namespace Ezg.Package.RpgStats
{
    /// <summary>
    ///     Abstract contract for entities that have a level, min/max bounds, and exp-based progression.
    ///     Lives in Ezg.Core so higher layers can depend on the interface without pulling in the full stat system.
    /// </summary>
    public interface ILevelProgression
    {
        int Level { get; set; }
        int LevelMin { get; set; }
        int LevelMax { get; set; }
        int ExpCurrent { get; }
        int ExpRequired { get; }

        int GetExpRequiredForLevel(int level);
        void ModifyExp(int amount);
        void SetCurrentExp(int value);
        void SetLevel(int targetLevel);
        void SetLevel(int targetLevel, bool clearExp);
    }
}

using System.Collections.Generic;

namespace Ezg.Package.RpgStats
{
    /// <summary>
    ///     Allows the stat to use modifiers
    /// </summary>
    public interface IStatModifiable
    {
        float StatModifierValue { get; }

        void AddModifier(RPGStatModifier mod);
        void RemoveModifier(RPGStatModifier mod);
        void ClearModifiers();
        void UpdateModifiers();
        List<RPGStatModifier> GetStatModifiers();
        List<RPGStatModifier> GetStatModifiers<T>();
    }
}
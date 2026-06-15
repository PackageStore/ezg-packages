using System;

namespace Ezg.Package.RpgStats
{
    /// <summary>
    ///     Non-generic base for all stats. Holds the value logic that is independent of the
    ///     stat key type, so key-agnostic consumers (e.g. stat linkers) can reference a stat
    ///     without knowing its <c>TKey</c>.
    /// </summary>
    public abstract class RPGStat : ICloneable
    {
        /// <summary>
        ///     Used by the StatBase Value Property
        /// </summary>
        private float _statBaseValue;

        /// <summary>
        ///     The Total Value of the stat
        /// </summary>
        public virtual float StatValue => StatBaseValue;

        /// <summary>
        ///     The Base Value of the stat
        /// </summary>
        public virtual float StatBaseValue
        {
            get => _statBaseValue;
            set => _statBaseValue = value;
        }

        public virtual object Clone()
        {
            return MemberwiseClone();
        }
    }

    /// <summary>
    ///     The base class for all keyed Stats. <typeparamref name="TKey" /> is the project-defined
    ///     stat identifier (e.g. an <c>enum RPGStatType</c>).
    /// </summary>
    public class RPGStat<TKey> : RPGStat
    {
        /// <summary>
        ///     Used by the StatType Property
        /// </summary>
        private TKey _statType;

        /// <summary>
        ///     Basic Constructor
        /// </summary>
        public RPGStat()
        {
            StatType = default;
            StatBaseValue = 0;
        }

        /// <summary>
        ///     The key identifying the Stat within its collection
        /// </summary>
        public TKey StatType
        {
            get => _statType;
            set => _statType = value;
        }

        public override object Clone()
        {
            return MemberwiseClone();
        }
    }
}

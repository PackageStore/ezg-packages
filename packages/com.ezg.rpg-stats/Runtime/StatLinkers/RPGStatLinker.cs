using System;

namespace Ezg.Package.RpgStats
{
    /// <summary>
    ///     The base class used by all stat linkers
    /// </summary>
    public abstract class RPGStatLinker : IStatValueChange
    {
        /// <summary>
        ///     Basic Constructor. Listens to the Stat's OnValueChange
        ///     event if the stat implements IStatValueChange.
        /// </summary>
        public RPGStatLinker(RPGStat stat)
        {
            LinkedStat = stat;

            var iValueChange = LinkedStat as IStatValueChange;
            if (iValueChange != null) iValueChange.OnValueChange += OnLinkedStatValueChange;
        }

        /// <summary>
        ///     The RPGStat linked to by the stat linker
        /// </summary>
        public RPGStat LinkedStat { get; }

        /// <summary>
        ///     Gets the value of the stat linker
        /// </summary>
        public abstract int Value { get; }

        /// <summary>
        ///     Triggers when the Value of the linker changes
        /// </summary>
        public event EventHandler OnValueChange;

        /// <summary>
        ///     Listens to the LinkedStat's OnValueChange event if able to
        /// </summary>
        private void OnLinkedStatValueChange(object stat, EventArgs args)
        {
            if (OnValueChange != null) OnValueChange(this, null);
        }
    }
}
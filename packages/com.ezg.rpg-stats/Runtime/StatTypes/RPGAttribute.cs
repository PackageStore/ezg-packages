using System;
using System.Collections.Generic;

namespace Ezg.Package.RpgStats
{
    /// <summary>
    ///     RPGStat that inherits from RPGStatModifiable and implements IStatScalable and IStatLinkable.
    /// </summary>
    public class RPGAttribute<TKey> : RPGStatModifiable<TKey>, IStatScalable, IStatLinkable, ICloneable
    {
        /// <summary>
        ///     List of all stat linkers applied to the stat
        /// </summary>
        private readonly List<RPGStatLinker> _statLinkers;

        /// <summary>
        ///     Used By StatLevelValue Property
        /// </summary>
        private int _statLevelValue;

        /// <summary>
        ///     Used By StatLinker Value Property
        /// </summary>
        private float _statLinkerValue;

        /// <summary>
        ///     Basic Constructor
        /// </summary>
        public RPGAttribute()
        {
            _statLinkers = new List<RPGStatLinker>();
        }

        /// <summary>
        ///     The value gained by the ScaledStat method
        /// </summary>
        public int StatLevelValue => _statLevelValue;

        /// <summary>
        ///     Gets the stat base value with the StatLevelValue and StatLinkerValue added
        /// </summary>
        public override float StatBaseValue => base.StatBaseValue + StatLevelValue + StatLinkerValue;

        public override object Clone()
        {
            return MemberwiseClone();
        }

        /// <summary>
        ///     the value gained from all applied stat linkers
        /// </summary>
        public float StatLinkerValue => _statLinkerValue;

        /// <summary>
        ///     Add a linker to the stat and listen to it's valueChange event
        /// </summary>
        public void AddLinker(RPGStatLinker linker)
        {
            _statLinkers.Add(linker);
            linker.OnValueChange += OnLinkerValueChange;
        }

        /// <summary>
        ///     Removes a linker from the stat and stops listening to the value change event
        /// </summary>
        /// <param name="linker"></param>
        public void RemoveLinker(RPGStatLinker linker)
        {
            _statLinkers.Remove(linker);
            linker.OnValueChange -= OnLinkerValueChange;
        }

        /// <summary>
        ///     Removes all linkers from the stat
        /// </summary>
        public void ClearLinkers()
        {
            foreach (var linker in _statLinkers) linker.OnValueChange -= OnLinkerValueChange;
            _statLinkers.Clear();
        }

        /// <summary>
        ///     Update the StatLinkerValue based of the currently applied stat linkers
        /// </summary>
        public void UpdateLinkers()
        {
            _statLinkerValue = 0;
            foreach (var link in _statLinkers) _statLinkerValue += link.Value;
            TriggerValueChange();
        }

        /// <summary>
        ///     Overridable method that scales the class based off the passed value
        ///     Triggers the stat's Value Change event
        /// </summary>
        public virtual void ScaleStat(int level)
        {
            _statLevelValue = level;
            TriggerValueChange();
        }

        /// <summary>
        ///     Listens to the attached StatLinkers and Updates the StatLinkerValue if
        ///     a stat linker value changes
        /// </summary>
        private void OnLinkerValueChange(object linker, EventArgs args)
        {
            UpdateLinkers();
        }
    }
}

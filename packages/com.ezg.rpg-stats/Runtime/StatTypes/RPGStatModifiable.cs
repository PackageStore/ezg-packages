using System;
using System.Collections.Generic;
using System.Linq;

namespace Ezg.Package.RpgStats
{
    /// <summary>
    ///     A RPGStat Type that implements both IStatModifiable and IStatValueChange
    /// </summary>
    public class RPGStatModifiable<TKey> : RPGStat<TKey>, IStatModifiable, IStatValueChange
    {
        /// <summary>
        ///     List of RPGStatModifiers applied to the stat
        /// </summary>
        private readonly List<RPGStatModifier> _statMods;

        /// <summary>
        ///     Used by the StatModifierValue Property
        /// </summary>
        private float _statModValue;

        /// <summary>
        ///     Basic Constructor
        /// </summary>
        public RPGStatModifiable()
        {
            _statModValue = 0;
            _statMods = new List<RPGStatModifier>();
        }

        /// <summary>
        ///     The stat's total value including StatModifiers
        /// </summary>
        //public override float StatValue
        //{
        //    get { return base.StatValue + StatModifierValue; }
        //}

        public override float StatValue => StatConfigs<TKey>.ValidStat(StatType, base.StatValue + StatModifierValue);

        /// <summary>
        ///     The total value of the stat modifiers applied to the stat
        /// </summary>
        public float StatModifierValue => _statModValue;

        /// <summary>
        ///     Adds Modifier to stat and listens to the mod's value change event
        /// </summary>
        public void AddModifier(RPGStatModifier mod)
        {
            _statMods.Add(mod);
            mod.OnValueChange += OnModValueChange;
        }

        /// <summary>
        ///     Removes modifier from stat and stops listening to value change event
        /// </summary>
        public void RemoveModifier(RPGStatModifier mod)
        {
            _statMods.Remove(mod);
            mod.OnValueChange -= OnModValueChange;
        }

        /// <summary>
        ///     Removes all modifiers from the stat and stops listening to the value change event
        /// </summary>
        public void ClearModifiers()
        {
            foreach (var mod in _statMods) mod.OnValueChange -= OnModValueChange;
            _statMods.Clear();
        }

        /// <summary>
        ///     Updates the StatModifierValue based of the currently applied modifier values
        /// </summary>
        public void UpdateModifiers()
        {
            _statModValue = 0;

            var orderGroups = _statMods.OrderBy(m => m.Order).GroupBy(m => m.Order);
            foreach (var group in orderGroups)
            {
                float sum = 0, max = float.MinValue;
                foreach (var mod in group)
                    //Debug.Log(mod.Order);
                    if (!mod.Stacks)
                    {
                        if (mod.Value > max) max = mod.Value;
                    }
                    else
                    {
                        sum += mod.Value;
                    }

                _statModValue += group.First().ApplyModifier(
                    StatBaseValue + _statModValue,
                    sum > max ? sum : max);
            }

            TriggerValueChange();
        }

        public List<RPGStatModifier> GetStatModifiers()
        {
            return _statMods;
        }

        /// <summary>
        ///     Get stat với list custom
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        public List<RPGStatModifier> GetStatModifiers<T>()
        {
            return _statMods.Where(x => x.GetType() == typeof(T)).ToList();
        }

        /// <summary>
        ///     Event that triggers when the stat value changes
        /// </summary>
        public event EventHandler OnValueChange;

        /// <summary>
        ///     Triggers the OnValueChange Event
        /// </summary>
        protected void TriggerValueChange()
        {
            if (OnValueChange != null) OnValueChange(this, null);
        }

        /// <summary>
        ///     Used to listen to the applied stat modifier OnValueChange events
        /// </summary>
        public void OnModValueChange(object modifier, EventArgs args)
        {
            UpdateModifiers();
        }

        public override object Clone()
        {
            return MemberwiseClone();
        }
    }
}

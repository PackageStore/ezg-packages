using System;
using System.Collections.Generic;
using EzgInstanceFactory = Ezg.Package.InstanceFactory.InstanceFactory;
using UnityEngine;

namespace Ezg.Package.RpgStats
{
    /// <summary>
    ///     The base class used to define a collection of RPGStats.
    ///     Also used to apply and remove RPGStatModifiers from individual
    ///     RPGStats.
    /// </summary>
    public class RPGStatCollection<TKey> : ICloneable
    {
        private Dictionary<TKey, RPGStat<TKey>> _statDict;

        /// <summary>
        ///     Dictionary containing all stats held within the collection
        /// </summary>
        public Dictionary<TKey, RPGStat<TKey>> StatDict
        {
            get
            {
                if (_statDict == null) _statDict = new Dictionary<TKey, RPGStat<TKey>>();
                return _statDict;
            }
        }

        public object Clone()
        {
            var result = MemberwiseClone() as RPGStatCollection<TKey>;
            result._statDict = new Dictionary<TKey, RPGStat<TKey>>();
            foreach (var rpgStat in _statDict)
            {
                // Mirror the source stat's concrete type instead of hardcoding which key is a Vital.
                if (rpgStat.Value is RPGVital<TKey>)
                {
                    var vital = result.CreateOrGetStat<RPGVital<TKey>>(rpgStat.Key);
                    vital.StatType = rpgStat.Key;
                    vital.StatBaseValue = rpgStat.Value.StatBaseValue;
                    vital.SetCurrentValueToMax();
                }
                else
                {
                    var statInfo = result.CreateOrGetStat<RPGAttribute<TKey>>(rpgStat.Key);
                    statInfo.StatType = rpgStat.Key;
                    statInfo.StatBaseValue = rpgStat.Value.StatBaseValue;
                }

                var statMod = GetStatModifiers(rpgStat.Key);
                if (statMod != null)
                    result.AddStatModifier(rpgStat.Key, statMod, true);
            }

            return result;
        }

        public void SetDict(Dictionary<TKey, RPGStat<TKey>> data)
        {
            _statDict = data;
        }

        /// <summary>
        ///     Checks if there is a RPGStat with the given type and id
        /// </summary>
        public bool ContainStat(TKey statType)
        {
            return StatDict.ContainsKey(statType);
        }

        /// <summary>
        ///     Gets the RPGStat with the given RPGStatTyp and ID
        /// </summary>
        public RPGStat<TKey> GetStat(TKey statType)
        {
            if (ContainStat(statType)) return StatDict[statType];
            return null;
        }

        /// <summary>
        ///     Gets the RPGStat with the given RPGStatType and ID as type T
        /// </summary>
        public T GetStat<T>(TKey type) where T : RPGStat<TKey>
        {
            return GetStat(type) as T;
        }

        /// <summary>
        ///     Creates a new instance of the stat ands adds it to the StatDict
        /// </summary>
        public T CreateStat<T>(TKey statType) where T : RPGStat<TKey>
        {
            var stat = EzgInstanceFactory.CreateInstance(typeof(T));
            StatDict.Add(statType, (T)stat);
            return (T)stat;
        }

        /// <summary>
        ///     Creates or Gets a RPGStat of type T. Used within the setup method during initialization.
        /// </summary>
        public T CreateOrGetStat<T>(TKey statType) where T : RPGStat<TKey>
        {
            var stat = GetStat<T>(statType);
            if (stat == null) stat = CreateStat<T>(statType);
            return stat;
        }


        /// <summary>
        ///     Adds a Stat Modifier to the Target stat.
        /// </summary>
        public void AddStatModifier(TKey target, RPGStatModifier mod)
        {
            AddStatModifier(target, mod, false);
        }

        public void AddStatModifier(TKey target, List<RPGStatModifier> mod, bool update)
        {
            foreach (var s in mod) AddStatModifier(target, s, update);
        }

        /// <summary>
        ///     Adds a Stat Modifier to the Target stat and then updates the stat's value.
        /// </summary>
        public void AddStatModifier(TKey target, RPGStatModifier mod, bool update)
        {
            if (ContainStat(target))
            {
                var modStat = GetStat(target) as IStatModifiable;
                if (modStat != null)
                {
                    modStat.AddModifier(mod);
                    if (update) modStat.UpdateModifiers();
                }
                else
                {
                    Debug.Log("[RPGStats] Trying to add Stat Modifier to non modifiable stat \"" + target + "\"");
                }
            }
            else
            {
                Debug.Log("[RPGStats] Trying to add Stat Modifier to \"" + target +
                          "\", but RPGStats does not contain that stat");
            }
        }

        /// <summary>
        ///     Removes a Stat Modifier to the Target stat.
        /// </summary>
        public void RemoveStatModifier(TKey target, RPGStatModifier mod)
        {
            RemoveStatModifier(target, mod, false);
        }

        /// <summary>
        ///     Removes a Stat Modifier to the Target stat and then updates the stat's value.
        /// </summary>
        public void RemoveStatModifier(TKey target, RPGStatModifier mod, bool update)
        {
            if (ContainStat(target))
            {
                var modStat = GetStat(target) as IStatModifiable;
                if (modStat != null)
                {
                    modStat.RemoveModifier(mod);
                    if (update) modStat.UpdateModifiers();
                }
                else
                {
                    Debug.Log("[RPGStats] Trying to remove Stat Modifier from non modifiable stat \"" + target + "\"");
                }
            }
            else
            {
                Debug.Log("[RPGStats] Trying to remove Stat Modifier from \"" + target +
                          "\", but RPGStatCollection does not contain that stat");
            }
        }

        public List<RPGStatModifier> GetStatModifiers(TKey target)
        {
            return ((IStatModifiable)GetStat(target)).GetStatModifiers();
        }

        public List<RPGStatModifier> GetStatModifiers<T>(TKey target)
        {
            return ((IStatModifiable)GetStat(target)).GetStatModifiers<T>();
        }

        /// <summary>
        ///     Clears all stat modifiers from all stats in the collection.
        /// </summary>
        public void ClearStatModifiers()
        {
            ClearStatModifiers(false);
        }

        /// <summary>
        ///     Clears all stat modifiers from all stats in the collection then updates all the stat's values.
        /// </summary>
        /// <param name="update"></param>
        public void ClearStatModifiers(bool update)
        {
            foreach (var key in StatDict.Keys) ClearStatModifier(key, update);
        }

        /// <summary>
        ///     Clears all stat modifiers from the target stat.
        /// </summary>
        public void ClearStatModifier(TKey target)
        {
            ClearStatModifier(target, false);
        }

        /// <summary>
        ///     Clears all stat modifiers from the target stat then updates the stat's value.
        /// </summary>
        public void ClearStatModifier(TKey target, bool update)
        {
            if (ContainStat(target))
            {
                var modStat = GetStat(target) as IStatModifiable;
                if (modStat != null)
                {
                    modStat.ClearModifiers();
                    if (update) modStat.UpdateModifiers();
                }
                else
                {
                    Debug.Log("[RPGStats] Trying to clear Stat Modifiers from non modifiable stat \"" + target + "\"");
                }
            }
            else
            {
                Debug.Log("[RPGStats] Trying to clear Stat Modifiers from \"" + target +
                          "\", but RPGStatCollection does not contain that stat");
            }
        }

        /// <summary>
        ///     Updates all stat modifier's values
        /// </summary>
        public void UpdateStatModifiers()
        {
            foreach (var key in StatDict.Keys) UpdateStatModifer(key);
        }

        /// <summary>
        ///     Updates the target stat's modifier value
        /// </summary>
        public void UpdateStatModifer(TKey target)
        {
            if (ContainStat(target))
            {
                var modStat = GetStat(target) as IStatModifiable;
                if (modStat != null)
                    modStat.UpdateModifiers();
                else
                    Debug.Log("[RPGStats] Trying to Update Stat Modifiers for a non modifiable stat \"" + target +
                              "\"");
            }
            else
            {
                Debug.Log("[RPGStats] Trying to Update Stat Modifiers for \"" + target +
                          "\", but RPGStatCollection does not contain that stat");
            }
        }

        /// <summary>
        ///     Scales all stats in the collection to the same target level
        /// </summary>
        public void ScaleStatCollection(int level)
        {
            foreach (var key in StatDict.Keys) ScaleStat(key, level);
        }

        /// <summary>
        ///     Scales the target stat in the collection to the target level
        /// </summary>
        public void ScaleStat(TKey target, int level)
        {
            if (ContainStat(target))
            {
                var stat = GetStat(target) as IStatScalable;
                if (stat != null)
                    stat.ScaleStat(level);
                else
                    Debug.Log("[RPGStats] Trying to Scale Stat with a non scalable stat \"" + target + "\"");
            }
            else
            {
                Debug.Log("[RPGStats] Trying to Scale Stat for \"" + target +
                          "\", but RPGStatCollection does not contain that stat");
            }
        }
    }
}

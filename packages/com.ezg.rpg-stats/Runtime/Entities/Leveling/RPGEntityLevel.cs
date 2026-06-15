using System;
using UnityEngine;

namespace Ezg.Package.RpgStats
{
    /// <summary>
    ///     The base class for any entity the has a level. Must be inherited
    ///     and have the GetExpRequiredForLevel method overriden. Class handles
    ///     the level as well as the experience required to level and the current
    ///     experience. Also contains event for ExpGain and Level Change.
    /// </summary>
    [Serializable]
    public abstract class RPGEntityLevel : MonoBehaviour, ILevelProgression
    {
        /// <summary>
        ///     Variable used for the Level Property
        /// </summary>
        [SerializeField] private int _Level;

        /// <summary>
        ///     Variable used for the LevelMin Property
        /// </summary>
        [SerializeField] private int _LevelMin;

        /// <summary>
        ///     Variable used for the LevelMax Property
        /// </summary>
        [SerializeField] private int _LevelMax = 100;

        /// <summary>
        ///     Variable used for the ExpCurrent Property
        /// </summary>
        private int _expCurrent;

        /// <summary>
        ///     Variable used for the ExpRequired Property
        /// </summary>
        private int _expRequired;

        /// <summary>
        ///     Gets or sets the current Level
        /// </summary>
        public int Level
        {
            get => _Level;
            set => _Level = value;
        }

        /// <summary>
        ///     Gets or sets the minimum level
        /// </summary>
        public int LevelMin
        {
            get => _LevelMin;
            set => _LevelMin = value;
        }

        /// <summary>
        ///     Gets or sets the maximum level
        /// </summary>
        public int LevelMax
        {
            get => _LevelMax;
            set => _LevelMax = value;
        }

        /// <summary>
        ///     Gets the current experience
        /// </summary>
        public int ExpCurrent
        {
            get => _expCurrent;
            private set => _expCurrent = value;
        }

        /// <summary>
        ///     Gets the required experience to level
        /// </summary>
        public int ExpRequired
        {
            get => _expRequired;
            private set => _expRequired = value;
        }

        private void Awake()
        {
            ExpRequired = GetExpRequiredForLevel(Level);
        }

        /// <summary>
        ///     Triggers when experience is gained or lost
        /// </summary>
        public event EventHandler<RPGExpGainEventArgs> OnEntityExpGain;

        /// <summary>
        ///     Triggers when the entity changes level. Levels in the eventargs contain
        ///     the starting level and the ending level (Example 20 to 30)
        /// </summary>
        public event EventHandler<RPGLevelChangeEventArgs> OnEntityLevelChange;

        /// <summary>
        ///     Triggers when the entity's level increases. Levels in the eventArgs
        ///     contain the starting and ending levels always increase by one.
        /// </summary>
        public event EventHandler<RPGLevelChangeEventArgs> OnEntityLevelUp;

        /// <summary>
        ///     Triggers when the entity's level increases. Levels in the eventArgs
        ///     contain the starting and ending levels always decrease by one.
        /// </summary>
        public event EventHandler<RPGLevelChangeEventArgs> OnEntityLevelDown;

        /// <summary>
        ///     Gets the required experienced needed in order to level
        ///     from the level pass as the parameter
        /// </summary>
        public abstract int GetExpRequiredForLevel(int level);

        /// <summary>
        ///     Add or subtract a value from the current experience
        /// </summary>
        public void ModifyExp(int amount)
        {
            ExpCurrent += amount;

            if (OnEntityExpGain != null) OnEntityExpGain(this, new RPGExpGainEventArgs(amount));

            CheckCurrentExp();
        }

        /// <summary>
        ///     Sets the current experience then checks exp value
        /// </summary>
        public void SetCurrentExp(int value)
        {
            var expGained = value - ExpCurrent;

            ExpCurrent = value;

            if (OnEntityExpGain != null) OnEntityExpGain(this, new RPGExpGainEventArgs(expGained));

            CheckCurrentExp();
        }

        /// <summary>
        ///     Checks the ExpCurrent value and increases or decreases the
        ///     level based of if the ExpCurrent value is greater then ExpRequired
        ///     or less then Zero.
        /// </summary>
        public void CheckCurrentExp()
        {
            var oldLevel = Level;

            InternalCheckCurrentExp();

            if (oldLevel != Level && OnEntityLevelChange != null)
                OnEntityLevelChange(this, new RPGLevelChangeEventArgs(Level, oldLevel));
        }

        /// <summary>
        ///     Checks the ExpCurrent value and increases or decreases the
        ///     level based of if the ExpCurrent value is greater then ExpRequired
        ///     or less then Zero.
        /// </summary>
        private void InternalCheckCurrentExp()
        {
            while (true)
                if (ExpCurrent >= ExpRequired)
                {
                    ExpCurrent -= ExpRequired;
                    InternalIncreaseCurrentLevel();
                    if (Level >= LevelMax)
                        return;
                }
                else if (ExpCurrent < 0)
                {
                    ExpCurrent += GetExpRequiredForLevel(Level - 1);
                    InternalDecreaseCurrentLevel();
                }
                else
                {
                    break;
                }
        }

        /// <summary>
        ///     Increase the current level and set the ExpRequired for the new level.
        ///     Does not change the ExpCurrent value.
        /// </summary>
        public void IncreaseCurrentLevel()
        {
            var oldLevel = Level;

            InternalIncreaseCurrentLevel();

            if (oldLevel != Level && OnEntityLevelChange != null)
                OnEntityLevelChange(this, new RPGLevelChangeEventArgs(Level, oldLevel));
        }

        /// <summary>
        ///     Increase the current level and set the ExpRequired for the new level.
        ///     Does not change the ExpCurrent value.
        /// </summary>
        private void InternalIncreaseCurrentLevel()
        {
            var oldLevel = Level++;

            if (Level > LevelMax)
            {
                Level = LevelMax;
                ExpCurrent = GetExpRequiredForLevel(Level);
            }

            ExpRequired = GetExpRequiredForLevel(Level);
            if (OnEntityLevelUp != null) OnEntityLevelUp(this, new RPGLevelChangeEventArgs(Level, oldLevel));
        }

        /// <summary>
        ///     Decreases the current level and set the ExpRequired for the new level.
        ///     Does not change the ExpCurrent value.
        /// </summary>
        public void DecreaseCurrentLevel()
        {
            var oldLevel = Level;

            InternalDecreaseCurrentLevel();

            if (oldLevel != Level && OnEntityLevelChange != null)
                OnEntityLevelChange(this, new RPGLevelChangeEventArgs(Level, oldLevel));
        }

        /// <summary>
        ///     Decreases the current level and set the ExpRequired for the new level.
        ///     Does not change the ExpCurrent value.
        /// </summary>
        private void InternalDecreaseCurrentLevel()
        {
            var oldLevel = Level--;

            if (Level < LevelMin)
            {
                Level = LevelMin;
                ExpCurrent = 0;
            }

            ExpRequired = GetExpRequiredForLevel(Level);
            if (OnEntityLevelDown != null) OnEntityLevelDown(this, new RPGLevelChangeEventArgs(Level, oldLevel));
        }

        /// <summary>
        ///     Sets the Level value and clears the current exp value.
        /// </summary>
        public void SetLevel(int targetLevel)
        {
            SetLevel(targetLevel, true);
        }

        /// <summary>
        ///     Sets the Level value and clears the ExpCurrent value or checks
        ///     the ExpCurrent value based off the set level when the clearExp
        ///     parameter is set to false.
        /// </summary>
        public void SetLevel(int targetLevel, bool clearExp)
        {
            var oldLevel = Level;

            Level = targetLevel;
            ExpRequired = GetExpRequiredForLevel(Level);

            if (clearExp)
                SetCurrentExp(0);
            else
                InternalCheckCurrentExp();

            if (oldLevel != Level && OnEntityLevelChange != null)
                OnEntityLevelChange(this, new RPGLevelChangeEventArgs(Level, oldLevel));
        }
    }
}
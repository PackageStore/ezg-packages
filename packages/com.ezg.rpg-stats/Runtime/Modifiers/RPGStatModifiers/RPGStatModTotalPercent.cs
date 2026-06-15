namespace Ezg.Package.RpgStats
{
    /// <summary>
    ///     Modifier that takes a percentage of the stat's value
    /// </summary>
    /// [System.Serializable]
    public class RPGStatModTotalPercent : RPGStatModifier
    {
        /// <summary>
        ///     Constructor that sets the Value and sets Stacks to true
        /// </summary>
        public RPGStatModTotalPercent(float value) : base(value)
        {
        }

        /// <summary>
        ///     Constructor that sets the Value and Stacks
        /// </summary>
        public RPGStatModTotalPercent(float value, bool stacks) : base(value, stacks)
        {
        }

        public RPGStatModTotalPercent(float value, bool stacks, string name) : base(value, stacks, name)
        {
        }

        /// <summary>
        ///     The order in which the modifier is applied to the stat
        /// </summary>
        public override int Order => 3;

        /// <summary>
        ///     Calculates the amount to apply to the stat based off the
        ///     sum of all the stat modifier's value and the current value of
        ///     the stat.
        /// </summary>
        public override float ApplyModifier(float statValue, float modValue)
        {
            return statValue * modValue / (StatConfigs.IsCompactPercent ? 1 : 100f);
        }
    }
}
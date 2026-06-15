using System;

namespace Ezg.Package.RpgStats
{
    /// <summary>
    ///     Event Args displaying a gain in experience
    /// </summary>
    public class RPGExpGainEventArgs : EventArgs
    {
        /// <summary>
        ///     Basic Constructor takes the experienced gained
        /// </summary>
        public RPGExpGainEventArgs(int expGained)
        {
            ExpGained = expGained;
        }

        /// <summary>
        ///     The gain in experience, can be positive or negative.
        /// </summary>
        public int ExpGained { get; private set; }
    }
}
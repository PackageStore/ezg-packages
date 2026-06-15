using System;

namespace Ezg.Package.RpgStats
{
    /// <summary>
    ///     Used to indicate when the stat's current value changes
    /// </summary>
    public interface IStatCurrentValueChange
    {
        event EventHandler OnCurrentValueChange;
    }
}
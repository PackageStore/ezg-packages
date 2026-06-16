namespace Ezg.Core.Utils
{
    /// <summary>
    ///     Represents an object that can be updated by the centralized update manager.
    /// </summary>
    public interface IUpdateManager
    {
        /// <summary>
        ///     Executes one update tick.
        /// </summary>
        void UpdateMe();
    }
}
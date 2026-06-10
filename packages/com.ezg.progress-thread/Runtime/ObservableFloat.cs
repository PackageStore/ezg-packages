using System;
using System.Collections.Generic;
#if ODIN_INSPECTOR
using Sirenix.OdinInspector;
#endif

namespace Ezg.Package.ProgressThread
{
    /// <summary>
    ///     Defines an interface for an observable value.
    /// </summary>
    /// <typeparam name="T">The type of the observed value.</typeparam>
    public interface IObservable<T>
    {
        /// <summary>
        ///     Gets or sets the value of the observable.
        /// </summary>
        T Value { get; set; }

        /// <summary>
        ///     Adds a listener that will be notified when the value changes.
        /// </summary>
        /// <param name="action">The action to call on change.</param>
        void AddListener(Action<T> action);

        /// <summary>
        ///     Removes a registered change listener.
        /// </summary>
        /// <param name="action">The action to remove.</param>
        void RemoveListener(Action<T> action);
    }

    /// <summary>
    ///     A concrete implementation of IObservable for float values, serializable and inspectable.
    /// </summary>
    [Serializable]
    public class ObservableFloat : IObservable<float>
    {
        #region Fields

        private float _field;
        private readonly List<Action<float>> _onValueChanged = new();

        #endregion

        #region Public Methods

        /// <summary>
        ///     Gets or sets the float value and notifies registered listeners of changes.
        /// </summary>
#if ODIN_INSPECTOR
        [ShowInInspector]
#endif
        public float Value
        {
            get => _field;
            set
            {
                if (_field == value) return;

                _field = value;

                _onValueChanged.ForEach(action => action?.Invoke(_field));
            }
        }

        /// <summary>
        ///     Adds a listener for value changes.
        /// </summary>
        /// <param name="action">The listener action.</param>
        public void AddListener(Action<float> action)
        {
            if (_onValueChanged.Contains(action)) return;
            _onValueChanged.Add(action);
        }

        /// <summary>
        ///     Removes a registered value change listener.
        /// </summary>
        /// <param name="action">The listener action.</param>
        public void RemoveListener(Action<float> action)
        {
            _onValueChanged.Remove(action);
        }

        #endregion
    }
}
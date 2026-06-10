using System;
using UnityEngine;
using UnityEngine.UI;

namespace Ezg.Package.ProgressThread
{
    /// <summary>
    ///     A visual representation component of a progress thread timer, modifying image fill amounts and updating UI texts.
    /// </summary>
    public class ProgressView : MonoBehaviour
    {
        #region Fields

        [SerializeField] private Image image;
        [SerializeField] private Text timeCountDown;
        [SerializeField] private bool isReverse;

        /// <summary>
        ///     Callback invoked when progress completes.
        /// </summary>
        public Action callback;

        /// <summary>
        ///     Gets or sets the visual image fill amount, accounting for reverse settings.
        /// </summary>
        public float FillAmount
        {
            get => GetReversibleFill(image.fillAmount);
            set => image.fillAmount = GetReversibleFill(value);
        }

        private const int FullFill = 1;
        private IProgress _progress;

        #endregion

        #region Initialize

        /// <summary>
        ///     Called when the behaviour becomes disabled or inactive. Cleans up sources.
        /// </summary>
        private void OnDisable()
        {
            SetSource(null);
            callback = null;
        }

        /// <summary>
        ///     Called when the MonoBehaviour is destroyed. Cleans up sources.
        /// </summary>
        private void OnDestroy()
        {
            SetSource(null);
            callback = null;
        }

        #endregion

        #region Public Methods

        /// <summary>
        ///     Sets the source progress interface tracking values. Binds event listeners for visual updates.
        /// </summary>
        /// <param name="progress">The source progress instance.</param>
        public void SetSource(IProgress progress)
        {
            if (progress != null)
            {
                progress.Current.RemoveListener(_ => UpdateView());
                progress.Max.RemoveListener(_ => UpdateView());
            }

            _progress = progress;

            if (progress != null)
            {
                progress.Current.AddListener(_ => UpdateView());
                progress.Max.AddListener(_ => UpdateView());
            }

            UpdateView();
        }

        /// <summary>
        ///     Sets the countdown text field range.
        /// </summary>
        /// <param name="startTime">The start duration offset.</param>
        /// <param name="maxTime">The total limit duration.</param>
        public void SetRangeTimeTxt(float startTime, float maxTime)
        {
            if (timeCountDown != null)
            {
                //timeCountDown.text = ((long)(maxTime - startTime)).ToTimeSpanString2();
            }
        }

        /// <summary>
        ///     Configures the countdown label to show an error or blank placeholder.
        /// </summary>
        public void SetTimeError()
        {
            if (timeCountDown != null)
                timeCountDown.text = "##########";
        }

        #endregion

        #region Private Methods

        private float GetReversibleFill(float fill)
        {
            return isReverse ? FullFill - fill : fill;
        }

        /// <summary>
        ///     Refreshes the visual fill state and texts matching the latest progress values.
        /// </summary>
        private void UpdateView()
        {
            if (_progress is null)
            {
                UpdateViewDefault();
                return;
            }

            var current = _progress.Current.Value;
            var max = _progress.Max.Value;

            if (max == default)
            {
                UpdateViewDefault();
                return;
            }

            if (timeCountDown != null)
            {
                //timeCountDown.text = ((long)(max - current)).ToTimeSpanString2();
            }

            if (_progress.StartTime == -1)
            {
                FillAmount = current / max;
            }
            else
            {
                var mileStone = _progress.EndTime - _progress.StartTime;
                FillAmount = (mileStone - max + current) / mileStone;
            }

            if (FillAmount >= 1)
            {
                callback?.Invoke();
                callback = null;
            }
        }

        private void UpdateViewDefault()
        {
            FillAmount = GetReversibleFill(default);
        }

        #endregion
    }

    /// <summary>
    ///     Interface representing trackable progress values with start and end times.
    /// </summary>
    public interface IProgress
    {
        /// <summary>
        ///     Observable current progress value.
        /// </summary>
        ObservableFloat Current { get; }

        /// <summary>
        ///     Observable maximum progress capacity value.
        /// </summary>
        ObservableFloat Max { get; }

        /// <summary>
        ///     Epoch start time timestamp.
        /// </summary>
        long StartTime { get; set; }

        /// <summary>
        ///     Epoch end time timestamp.
        /// </summary>
        long EndTime { get; set; }
    }

    /// <summary>
    ///     Standard implementation of IProgress.
    /// </summary>
    public class AdaptedProgress : IProgress
    {
        /// <summary>
        ///     Observable current progress value.
        /// </summary>
        public ObservableFloat Current { get; set; }

        /// <summary>
        ///     Observable maximum progress capacity value.
        /// </summary>
        public ObservableFloat Max { get; set; }

        /// <summary>
        ///     Epoch start time timestamp.
        /// </summary>
        public long StartTime { get; set; }

        /// <summary>
        ///     Epoch end time timestamp.
        /// </summary>
        public long EndTime { get; set; }
    }

    /// <summary>
    ///     Static extension class adapting cooldown and duration observers to IProgress adapters.
    /// </summary>
    public static class IProgressAdapterExtension
    {
        /// <summary>
        ///     Converts a cooldown observable into an IProgress adapter.
        /// </summary>
        /// <param name="observable">The cooldown observable.</param>
        /// <returns>The adapted progress instance.</returns>
        public static IProgress ToIProgress(this ICooldownObservable observable)
        {
            return new AdaptedProgress
            {
                Current = observable.CooldownCurrent,
                Max = observable.CooldownMax
            };
        }

        /// <summary>
        ///     Converts a duration observable into an IProgress adapter.
        /// </summary>
        /// <param name="observable">The duration observable.</param>
        /// <returns>The adapted progress instance.</returns>
        public static IProgress ToIProgress(this IDurationObservable observable)
        {
            return new AdaptedProgress
            {
                Current = observable.DurationCurrent,
                Max = observable.DurationMax
            };
        }
    }

    /// <summary>
    ///     Interface for objects offering observable cooldown progress values.
    /// </summary>
    public interface ICooldownObservable
    {
        /// <summary>
        ///     Observable current cooldown value.
        /// </summary>
        ObservableFloat CooldownCurrent { get; }

        /// <summary>
        ///     Observable maximum cooldown capacity value.
        /// </summary>
        ObservableFloat CooldownMax { get; }
    }

    /// <summary>
    ///     Interface for objects offering observable duration progress values.
    /// </summary>
    public interface IDurationObservable
    {
        /// <summary>
        ///     Observable current duration value.
        /// </summary>
        ObservableFloat DurationCurrent { get; }

        /// <summary>
        ///     Observable maximum duration capacity value.
        /// </summary>
        ObservableFloat DurationMax { get; }
    }
}
using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Events;
using Random = UnityEngine.Random;

namespace Ezg.Core.Extensions
{
    /// <summary>
    ///     Di chuyển theo vòng cung từ A -> B
    /// </summary>
    public class BezierMove : MonoBehaviour
    {
        #region Public Methods

        /// <summary>
        ///     Moves the object along a Bezier curve from start to end at a specified speed.
        /// </summary>
        /// <param name="startPoint">The starting position.</param>
        /// <param name="endPoint">The ending position.</param>
        /// <param name="speed">The movement speed.</param>
        /// <param name="success">Callback action executed upon reaching the destination.</param>
        /// <param name="isUI">If true, uses localPosition on RectTransform; otherwise, uses transform.position.</param>
        /// <param name="isRandom">If true, applies a random height to the control point.</param>
        /// <param name="isRotateToward">If true, rotates the object to face the direction of movement.</param>
        /// <param name="heightCustom">Custom height for the curve's peak. If 0, uses a default calculated distance-based height.</param>
        /// <param name="percentActive">The percentage of completion (0 to 1) at which to trigger percentAction.</param>
        /// <param name="percentAction">Callback event triggered when reaching the specified percent completion.</param>
        public void MoveBySpeed(Vector2 startPoint, Vector2 endPoint, float speed = 20, Action success = null,
            bool isUI = false, bool isRandom = false, bool isRotateToward = false, float heightCustom = 0,
            float percentActive = 0f, UnityAction percentAction = null)
        {
            var direction = (endPoint - startPoint).normalized;
            var distance = Vector2.Distance(startPoint, endPoint);
            var height = heightCustom != 0 ? heightCustom : distance / (isRandom ? Random.Range(-2, 2f) : 2f);
            var controlPoint = startPoint + direction * distance * 0.5f + Vector2.up * height;
            var _timeToFire = distance / speed;

            StartCoroutine(CalculateBezierPoint(startPoint,
                controlPoint, endPoint, _timeToFire, success, isUI, isRotateToward, percentActive, percentAction));
        }

        /// <summary>
        ///     Moves the object along a Bezier curve from start to end over a specified duration of time.
        /// </summary>
        /// <param name="startPoint">The starting position.</param>
        /// <param name="endPoint">The ending position.</param>
        /// <param name="time">The duration of the movement in seconds.</param>
        /// <param name="success">Callback action executed upon reaching the destination.</param>
        /// <param name="isUI">If true, uses localPosition on RectTransform; otherwise, uses transform.position.</param>
        /// <param name="isRandom">If true, applies a random height to the control point.</param>
        /// <param name="isRotateToward">If true, rotates the object to face the direction of movement.</param>
        /// <param name="heightCustom">Custom height for the curve's peak. If 0, uses a default calculated distance-based height.</param>
        /// <param name="percentActive">The percentage of completion (0 to 1) at which to trigger percentAction.</param>
        /// <param name="percentAction">Callback event triggered when reaching the specified percent completion.</param>
        public void MoveByTime(Vector2 startPoint, Vector2 endPoint, float time = .2f, Action success = null,
            bool isUI = false, bool isRandom = false, bool isRotateToward = false, float heightCustom = 0,
            float percentActive = 0f, UnityAction percentAction = null)
        {
            var direction = (endPoint - startPoint).normalized;
            var distance = Vector2.Distance(startPoint, endPoint);
            var height = heightCustom != 0 ? heightCustom : distance / (isRandom ? Random.Range(-2, 2f) : 2f);
            var controlPoint = startPoint + direction * distance * 0.5f + Vector2.up * height;

            StartCoroutine(CalculateBezierPointByTime(startPoint,
                controlPoint, endPoint, time, success, isUI, isRotateToward, percentActive, percentAction));
        }

        #endregion

        #region Private Methods

        /// <summary>
        ///     Coroutine that calculates the Bezier curve points and moves the object based on speed.
        /// </summary>
        /// <param name="p0">Starting point.</param>
        /// <param name="p1">Control point.</param>
        /// <param name="p2">Ending point.</param>
        /// <param name="_timeToFire">Total duration calculated based on speed.</param>
        /// <param name="success">Callback action executed upon completion.</param>
        /// <param name="isUI">If true, moves RectTransform.localPosition.</param>
        /// <param name="rotateToward">If true, rotates object to face direction.</param>
        /// <param name="percentActive">Percentage of completion to trigger action.</param>
        /// <param name="percentAction">Callback action for percentage threshold.</param>
        /// <returns>IEnumerator for coroutine execution.</returns>
        private IEnumerator CalculateBezierPoint(Vector2 p0, Vector2 p1, Vector2 p2,
            float _timeToFire,
            Action success, bool isUI = false, bool rotateToward = false, float percentActive = 0f,
            UnityAction percentAction = null)
        {
            var isActivePercenAction = false;
            var startTime = Time.time; // Thời gian bắt đầu di chuyển
            var rect = transform.GetComponent<RectTransform>();
            while (Time.time - startTime < _timeToFire)
            {
                var t = (Time.time - startTime) / _timeToFire;
                var pos = CalculatorPos(p0, p1, p2, t);
                if (isUI)
                    rect.localPosition = pos;
                else
                    transform.position = pos;

                //Kích hoạt sự kiện khi di chuyển được ? %
                if (percentActive != 0 && !isActivePercenAction &&
                    (Time.time - startTime) / _timeToFire >= percentActive)
                {
                    percentAction?.Invoke();
                    isActivePercenAction = true;
                }

                if (rotateToward)
                {
                    var arrowDirection =
                        CalculatorPos(p0, p1, p2, t + 0.01f) - pos;
                    var angle = Mathf.Atan2(arrowDirection.y, arrowDirection.x) * Mathf.Rad2Deg;
                    // var targetPos = pos + (pos - (Vector2)transform.position).normalized;
                    // float angle = Mathf.Atan2(targetPos.y - transform.position.y, targetPos.x - transform.position.x) *
                    //               Mathf.Rad2Deg;
                    transform.rotation = Quaternion.AngleAxis(angle, Vector3.forward);
                }

                yield return new WaitForEndOfFrame();
            }

            success?.Invoke();
        }

        /// <summary>
        ///     Coroutine that calculates the Bezier curve points and moves the object based on fixed duration.
        /// </summary>
        /// <param name="p0">Starting point.</param>
        /// <param name="p1">Control point.</param>
        /// <param name="p2">Ending point.</param>
        /// <param name="totalTime">Total time duration.</param>
        /// <param name="success">Callback action executed upon completion.</param>
        /// <param name="isUI">If true, moves RectTransform.localPosition.</param>
        /// <param name="rotateToward">If true, rotates object to face direction.</param>
        /// <param name="percentActive">Percentage of completion to trigger action.</param>
        /// <param name="percentAction">Callback action for percentage threshold.</param>
        /// <returns>IEnumerator for coroutine execution.</returns>
        private IEnumerator CalculateBezierPointByTime(Vector2 p0, Vector2 p1, Vector2 p2, float totalTime,
            Action success, bool isUI = false, bool rotateToward = false, float percentActive = 0f,
            UnityAction percentAction = null)
        {
            var isActivePercenAction = false;
            var startTime = Time.time;
            var rect = transform.GetComponent<RectTransform>();
            while (Time.time - startTime < totalTime)
            {
                // Tính tỷ lệ hoàn thành 
                var t = (Time.time - startTime) / totalTime;

                // Giới hạn t trong khoảng 0-1
                t = Mathf.Clamp01(t);
                var pos = CalculatorPos(p0, p1, p2, t);
                if (isUI)
                    rect.localPosition = pos;
                else
                    transform.position = pos;

                //Kích hoạt sự kiện khi di chuyển được ? %
                if (percentActive != 0 && !isActivePercenAction &&
                    (Time.time - startTime) / totalTime >= percentActive)
                {
                    percentAction?.Invoke();
                    isActivePercenAction = true;
                }

                if (rotateToward)
                {
                    var arrowDirection =
                        CalculatorPos(p0, p1, p2, t + 0.01f) - pos;
                    var angle = Mathf.Atan2(arrowDirection.y, arrowDirection.x) * Mathf.Rad2Deg;
                    // var targetPos = pos + (pos - (Vector2)transform.position).normalized;
                    // float angle = Mathf.Atan2(targetPos.y - transform.position.y, targetPos.x - transform.position.x) *
                    //               Mathf.Rad2Deg;
                    transform.rotation = Quaternion.AngleAxis(angle, Vector3.forward);
                }

                yield return new WaitForEndOfFrame();
            }

            success?.Invoke();
        }

        /// <summary>
        ///     Calculates the position along a quadratic Bezier curve at a given parameter t.
        /// </summary>
        /// <param name="p0">Starting point.</param>
        /// <param name="p1">Control point.</param>
        /// <param name="p2">Ending point.</param>
        /// <param name="tParam">The normalized time parameter [0, 1].</param>
        /// <returns>The calculated position vector at parameter t.</returns>
        private Vector2 CalculatorPos(Vector2 p0, Vector2 p1, Vector2 p2, float tParam)
        {
            var u = 1f - tParam;
            var tt = tParam * tParam;
            var uu = u * u;
            var pos = uu * p0 + 2f * u * tParam * p1 + tt * p2;
            return pos;
        }

        #endregion
    }
}
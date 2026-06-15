using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Networking;

namespace Ezg.Core.Networking
{
    /// <summary>
    ///     Represents a query builder for Cloudflare worker operations.
    /// </summary>
    /// <typeparam name="T">The type of the data being queried.</typeparam>
    public class CloudflareQuery<T>
    {
        #region Initialize

        /// <summary>
        ///     Initializes a new instance of the CloudflareQuery class.
        /// </summary>
        /// <param name="endPoint">The target endpoint.</param>
        public CloudflareQuery(string endPoint)
        {
            _endpoint = endPoint ?? throw new ArgumentNullException(nameof(endPoint));
        }

        #endregion

        #region Fields

        private readonly string _endpoint;
        private readonly Dictionary<string, object> _where = new();
        private int _timeout = 3;

        #endregion

        #region Public Methods

        // --- Configuration ---

        /// <summary>
        ///     Sets the timeout for the request.
        /// </summary>
        /// <param name="seconds">The timeout in seconds.</param>
        /// <returns>The query instance.</returns>
        public CloudflareQuery<T> WithTimeout(int seconds)
        {
            _timeout = seconds;
            return this;
        }

        /// <summary>
        ///     Adds a filter to the query based on a predicate.
        /// </summary>
        /// <param name="predicate">The filter expression.</param>
        /// <returns>The query instance.</returns>
        public CloudflareQuery<T> Where(Expression<Func<T, bool>> predicate)
        {
            ParseExpression(predicate.Body);
            return this;
        }

        // --- Operations ---

        /// <summary>
        ///     Executes a GET request to fetch data.
        /// </summary>
        /// <param name="onFail">Optional callback on failure.</param>
        /// <returns>A list of data items.</returns>
        public async UniTask<List<T>> Get(Action onFail = null)
        {
            var jsonBody = JsonConvert.SerializeObject(_where);
            var bodyRaw = Encoding.UTF8.GetBytes(jsonBody);

            var url = $"{CloudflareDB.BaseUrl}/{_endpoint}";

            using var request = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST);
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            request.timeout = _timeout;

            await request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                var errorMessage = $"Request failed: {request.error}";
                if (!string.IsNullOrEmpty(request.downloadHandler?.text))
                    errorMessage += $" | Response: {request.downloadHandler.text}";

                Debug.LogError(errorMessage);
                onFail?.Invoke();
            }

            var responseText = request.downloadHandler?.text;
            if (string.IsNullOrEmpty(responseText))
            {
                onFail?.Invoke();
                return null;
            }

            try
            {
                var wrapper = JsonConvert.DeserializeObject<ApiResponse<List<T>>>(responseText);
                return wrapper?.Data ?? new List<T>();
            }
            catch (JsonException ex)
            {
                Debug.LogWarning($"Failed to deserialize API response: {ex.Message}\nResponse: {responseText}");
                // Try to parse as direct list if wrapper format fails
                try
                {
                    var result = JsonConvert.DeserializeObject<List<T>>(responseText);
                    return result ?? new List<T>();
                }
                catch
                {
                    return new List<T>();
                }
            }
        }

        /// <summary>
        ///     Executes a DELETE request to remove data.
        /// </summary>
        /// <returns>A task representing the operation.</returns>
        public async UniTask Delete()
        {
            var jsonBody = JsonConvert.SerializeObject(_where);
            var bodyRaw = Encoding.UTF8.GetBytes(jsonBody);

            var url = $"{CloudflareDB.BaseUrl}/{_endpoint}";

            using var request = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPUT);
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            request.timeout = _timeout;

            await request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                var errorMessage = $"Request failed: {request.error}";
                if (!string.IsNullOrEmpty(request.downloadHandler?.text))
                    errorMessage += $" | Response: {request.downloadHandler.text}";

                throw new Exception(errorMessage);
            }
        }

        /// <summary>
        ///     Executes an UPSERT request for a single data item.
        /// </summary>
        /// <param name="data">The item to upsert.</param>
        /// <returns>A task representing the operation.</returns>
        public async UniTask Upsert(T data)
        {
            var jsonBody = JsonConvert.SerializeObject(data);
            var bodyRaw = Encoding.UTF8.GetBytes(jsonBody);

            var url = $"{CloudflareDB.BaseUrl}/{_endpoint}";

            using var request = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPUT);
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            request.timeout = _timeout;

            await request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                var errorMessage = $"Request failed: {request.error}";
                if (!string.IsNullOrEmpty(request.downloadHandler?.text))
                    errorMessage += $" | Response: {request.downloadHandler.text}";

                throw new Exception(errorMessage);
            }
        }

        /// <summary>
        ///     Executes an UPSERT request for a list of data items.
        /// </summary>
        /// <param name="data">The list of items to upsert.</param>
        /// <returns>A task representing the operation.</returns>
        public async UniTask Upsert(List<T> data)
        {
            var jsonBody = JsonConvert.SerializeObject(data);
            var bodyRaw = Encoding.UTF8.GetBytes(jsonBody);

            var url = $"{CloudflareDB.BaseUrl}/{_endpoint}";

            using var request = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPUT);
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            request.timeout = _timeout;

            await request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                var errorMessage = $"Request failed: {request.error}";
                if (!string.IsNullOrEmpty(request.downloadHandler?.text))
                    errorMessage += $" | Response: {request.downloadHandler.text}";

                throw new Exception(errorMessage);
            }
        }

        #endregion

        #region Private Methods

        /// <summary>
        ///     Parses a query expression.
        /// </summary>
        /// <param name="expression">The expression to parse.</param>
        private void ParseExpression(Expression expression)
        {
            if (expression is BinaryExpression binary)
            {
                if (binary.NodeType == ExpressionType.AndAlso)
                {
                    ParseExpression(binary.Left);
                    ParseExpression(binary.Right);
                    return;
                }

                if (binary.NodeType == ExpressionType.Equal)
                {
                    ParseEquality(binary);
                    return;
                }
            }

            throw new NotSupportedException(
                $"Expression '{expression}' is not supported. Only '==' and '&&' are currently supported.");
        }

        /// <summary>
        ///     Parses an equality expression.
        /// </summary>
        /// <param name="binary">The binary expression.</param>
        private void ParseEquality(BinaryExpression binary)
        {
            // Normalize: Ensure Left is Member, Right is Value
            var memberExpr = binary.Left;
            var valueExpr = binary.Right;

            // Try to identify which side is the property access
            if (memberExpr.NodeType != ExpressionType.MemberAccess && valueExpr.NodeType == ExpressionType.MemberAccess)
                // Swap: Value == Property -> Property == Value
                (memberExpr, valueExpr) = (valueExpr, memberExpr);

            // Handle simple boxing/casting (Convert) if present
            if (memberExpr.NodeType == ExpressionType.Convert &&
                ((UnaryExpression)memberExpr).Operand.NodeType == ExpressionType.MemberAccess)
                memberExpr = ((UnaryExpression)memberExpr).Operand;

            if (memberExpr is not MemberExpression member)
                throw new NotSupportedException(
                    $"Invalid expression format in '{binary}'. One side must be a property.");

            if (member.Member is not PropertyInfo propInfo)
                throw new NotSupportedException(
                    $"Member '{member.Member.Name}' is not a property. Fields are not supported in queries.");

            // Get JsonProperty name
            var jsonAttr = propInfo.GetCustomAttribute<JsonPropertyAttribute>();
            var jsonKey = jsonAttr != null ? jsonAttr.PropertyName : propInfo.Name;

            // Evaluate value
            var value = EvaluateValue(valueExpr);

            _where[jsonKey] = value;
        }

        /// <summary>
        ///     Evaluates the value of an expression.
        /// </summary>
        /// <param name="expr">The expression to evaluate.</param>
        /// <returns>The evaluated object.</returns>
        private object EvaluateValue(Expression expr)
        {
            if (expr is ConstantExpression constant)
                return constant.Value;

            if (expr is MemberExpression member && member.Expression is ConstantExpression constExpr)
            {
                if (member.Member is FieldInfo field) return field.GetValue(constExpr.Value);
                if (member.Member is PropertyInfo prop) return prop.GetValue(constExpr.Value);
            }

            // Fallback for complex calculations
            return Expression.Lambda(expr).Compile().DynamicInvoke();
        }

        #endregion
    }
}
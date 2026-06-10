using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace Ezg.Package.CsvReader
{
    /// <summary>
    ///     Utility class responsible for deserializing CSV structured text into C# object arrays or ID-value classes.
    /// </summary>
    public static class CsvReader
    {
        #region Public Methods

        /// <summary>
        ///     Parses a raw CSV string into a list of string arrays, each representing a row.
        /// </summary>
        /// <param name="text">The raw CSV text content.</param>
        /// <param name="separator">The CSV field separator character.</param>
        /// <returns>A list of string arrays representing CSV rows.</returns>
        public static List<string[]> ParseCsv(string text, char separator = ',')
        {
            var lines = new List<string[]>();
            var line = new List<string>();
            var token = new StringBuilder();
            var quotes = false;
            var isComment = false;
            for (var i = 0; i < text.Length; i++)
                if (quotes)
                {
                    if ((text[i] == '\\' && i + 1 < text.Length && text[i + 1] == '\"') ||
                        (text[i] == '\"' && i + 1 < text.Length && text[i + 1] == '\"'))
                    {
                        token.Append('\"');
                        i++;
                    }
                    else
                    {
                        switch (text[i])
                        {
                            case '\\' when i + 1 < text.Length && text[i + 1] == 'n':
                                token.Append('\n');
                                i++;
                                break;
                            case '\"':
                            {
                                line.Add(token.ToString());
                                token = new StringBuilder();
                                quotes = false;
                                if (i + 1 < text.Length && text[i + 1] == separator)
                                    i++;
                                break;
                            }
                            default:
                                token.Append(text[i]);
                                break;
                        }
                    }
                }
                else if (text[i] == '\r' || text[i] == '\n')
                {
                    if (token.Length > 0)
                    {
                        line.Add(token.ToString());
                        token = new StringBuilder();
                    }

                    if (line.Count > 0)
                    {
                        lines.Add(line.ToArray());
                        line.Clear();
                    }
                }
                else if (text[i] == separator)
                {
                    line.Add(token.ToString());
                    token = new StringBuilder();
                }
                else if (text[i] == '\"')
                {
                    quotes = true;
                }
                else
                {
                    token.Append(text[i]);
                }

            if (token.Length > 0) line.Add(token.ToString());

            if (line.Count > 0) lines.Add(line.ToArray());

            for (var i = 0; i < lines.Count; i++)
            {
                var data = lines[i];
                if (data.Contains("/") || data.Contains("//")) lines.Remove(data);
            }

            return lines;
        }

        #endregion

        #region Public Methods

        /// <summary>
        ///     Deserializes a CSV raw string into an array of the specified generic model type.
        /// </summary>
        /// <typeparam name="T">The type of the target model elements.</typeparam>
        /// <param name="text">The raw CSV text.</param>
        /// <param name="assetFile">The source asset path (used for warnings/logs).</param>
        /// <param name="separator">The CSV field separator character.</param>
        /// <returns>An array of deserialized model objects.</returns>
        public static T[] Deserialize<T>(string text, string assetFile, char separator = ',')
        {
            return (T[])CreateArray(typeof(T), ParseCsv(text, separator), assetFile);
        }

        /// <summary>
        ///     Deserializes a CSV raw string into an array of the specified Type.
        /// </summary>
        /// <param name="text">The raw CSV text.</param>
        /// <param name="type">The Type of the target model elements.</param>
        /// <param name="assetFile">The source asset path (used for warnings/logs).</param>
        /// <returns>An array of deserialized objects.</returns>
        public static object Deserialize(string text, Type type, string assetFile)
        {
            return CreateArray(type, ParseCsv(text), assetFile);
        }

        /// <summary>
        ///     Deserializes pre-parsed rows into an array of the specified generic model type.
        /// </summary>
        /// <typeparam name="T">The type of the target model elements.</typeparam>
        /// <param name="rows">A list of pre-parsed string array representing CSV rows.</param>
        /// <param name="assetFile">The source asset path (used for warnings/logs).</param>
        /// <returns>An array of deserialized model objects.</returns>
        public static T[] Deserialize<T>(List<string[]> rows, string assetFile)
        {
            return (T[])CreateArray(typeof(T), rows, assetFile);
        }

        /// <summary>
        ///     Deserializes an ID-Value style CSV text into a single model object.
        /// </summary>
        /// <typeparam name="T">The type of the target model object.</typeparam>
        /// <param name="text">The raw CSV text.</param>
        /// <param name="id_col">The column index for the field IDs.</param>
        /// <param name="value_col">The column index for the field values.</param>
        /// <returns>The deserialized model object.</returns>
        public static T DeserializeIdValue<T>(string text, int id_col = 0, int value_col = 1)
        {
            return (T)CreateIdValue(typeof(T), ParseCsv(text), id_col, value_col);
        }

        /// <summary>
        ///     Deserializes an ID-Value style CSV text into a single model object of the specified Type.
        /// </summary>
        /// <param name="type">The Type of the target model object.</param>
        /// <param name="text">The raw CSV text.</param>
        /// <param name="id_col">The column index for the field IDs.</param>
        /// <param name="value_col">The column index for the field values.</param>
        /// <returns>The deserialized model object.</returns>
        public static object DeserializeIdValue(Type type, string text, int id_col = 0, int value_col = 1)
        {
            return CreateIdValue(type, ParseCsv(text), id_col, value_col);
        }

        /// <summary>
        ///     Deserializes pre-parsed ID-Value CSV rows into a single model object.
        /// </summary>
        /// <typeparam name="T">The type of the target model object.</typeparam>
        /// <param name="rows">A list of pre-parsed CSV rows.</param>
        /// <param name="id_col">The column index for the field IDs.</param>
        /// <param name="value_col">The column index for the field values.</param>
        /// <returns>The deserialized model object.</returns>
        public static T DeserializeIdValue<T>(List<string[]> rows, int id_col = 0, int value_col = 1)
        {
            return (T)CreateIdValue(typeof(T), rows, id_col, value_col);
        }

        #endregion

        #region Private Methods

        /// <summary>
        ///     Instantiates and populates an array or a single object of the target Type from pre-parsed CSV rows.
        /// </summary>
        /// <param name="type">The Type of the model elements.</param>
        /// <param name="rows">Pre-parsed CSV rows list.</param>
        /// <param name="assetFile">The source CSV asset file path.</param>
        /// <returns>An array of populated elements, or a single element if array length is 1.</returns>
        private static object CreateArray(Type type, List<string[]> rows, string assetFile)
        {
            // Need test for sure logic
            //Test(rows);

            var (countElement, startRows) = CountNumberElement(1, 0, 0, rows);
            try
            {
                var arrayValue = Array.CreateInstance(type, countElement);
                var table = new Dictionary<string, int>();

                var log = -1;
                var _id = "";
                try
                {
                    for (var i = 0; i < rows[0].Length; i++)
                    {
                        var id = rows[0][i];
                        _id = id;
                        if (_id == " " || _id == "")
                        {
                        }

                        if (IsValidKeyFormat(id))
                        {
                            if (!table.ContainsKey(id))
                            {
                                for (var z = 0; z < id.Length; z++) // or count if it is a list
                                    if (id[z] == '_')
                                    {
                                        var index = z;
                                        if (index < id.Length - 1)
                                            id = id.Replace("_" + id[index + 1], id[index + 1].ToString().ToUpper());
                                        else
                                            id = id.Replace("_", "");
                                    }

                                table.Add(id, i);
                            }
                            else
                            {
                                throw new Exception("Key is duplicate: " + id);
                            }
                        }
                        else
                        {
                            throw new Exception("Key is not valid: " + id);
                        }
                    }
                }
                catch (Exception e)
                {
                    Debug.LogError("type: " + type + "-index fail: " + log + "-id: " + _id);
                    Debug.LogError(e.Message);
                }

                for (var i = 0; i < arrayValue.Length; i++)
                {
                    var rowData = Create(startRows[i], 0, rows, table, type, assetFile);
                    arrayValue.SetValue(rowData, i);
                }

                if (arrayValue.Length > 1)
                    return arrayValue;
                return arrayValue.GetValue(0);
            }
            catch (Exception e)
            {
                Debug.LogError(e);
                return default;
            }
        }

        /// <summary>
        ///     Recursively creates and populates an instance of the specified Type from CSV rows using the field mapping table.
        /// </summary>
        /// <param name="index">The row index to process.</param>
        /// <param name="parentIndex">The parent object's starting column index.</param>
        /// <param name="rows">Pre-parsed CSV rows list.</param>
        /// <param name="table">A field-to-column mapping dictionary.</param>
        /// <param name="type">The Type to instantiate.</param>
        /// <param name="assetFile">The source CSV asset file path.</param>
        /// <returns>A fully populated object instance.</returns>
        private static object Create(int index, int parentIndex, List<string[]> rows, Dictionary<string, int> table,
            Type type, string assetFile)
        {
            var v = Activator.CreateInstance(type);

            var fieldInfo = type.GetFields(BindingFlags.Public | BindingFlags.Instance);

            var cols = rows[index];

            foreach (var tmp in fieldInfo)
            {
                var isPrimitive = IsPrimitive(tmp);
                if (isPrimitive)
                {
                    if (table.ContainsKey(tmp.Name))
                    {
                        var idx = table[tmp.Name];

                        if (idx < cols.Length) SetValue(v, tmp, cols[idx]);
                    }
                    else
                    {
                        try
                        {
                            SetValue(v, tmp, default);
                            Debug.LogWarning("Column is not exist: " + tmp.Name + ", type: " + type + ", file: " +
                                             assetFile);
                            //foreach (var data in table)
                            //{
                            //    Debug.Log(data.Key + "-");
                            //}
                        }
                        catch
                        {
                            Debug.LogWarning("Column is not exist: " + tmp.Name + ", type: " + type + ", file: " +
                                             assetFile);
                            //Debug.LogError("Column is not exist: " + tmp.Name + ", type: " + type + ", file: " + assetFile);
                        }
                    }
                }
                else
                {
                    if (tmp.FieldType.IsArray)
                    {
                        var elementType = GetElementTypeFromFieldInfo(tmp);

                        var objectIndex = GetObjectIndex(elementType, table);
                        var (countElement, startRows) = CountNumberElement(index, objectIndex, parentIndex, rows);

                        var arrayValue = Array.CreateInstance(elementType, countElement);

                        for (var i = 0; i < arrayValue.Length; i++)
                        {
                            var value = Create(startRows[i], objectIndex, rows, table, elementType, assetFile);
                            arrayValue.SetValue(value, i);
                        }

                        tmp.SetValue(v, arrayValue);
                    }
                    else
                    {
                        var typeName = tmp.FieldType.FullName;
                        if (typeName == null) throw new Exception("Full name is nil");

                        var elementType = GetType(typeName);

                        var objectIndex = GetObjectIndex(elementType, table);

                        var value = Create(index, objectIndex, rows, table, elementType, assetFile);

                        tmp.SetValue(v, value);
                    }
                }
            }

            return v;
        }

        /// <summary>
        ///     Converts the raw string cell value and sets it to the specified field on an object.
        /// </summary>
        /// <param name="v">The target object instance.</param>
        /// <param name="fieldInfo">The field to populate.</param>
        /// <param name="value">The raw string value from the CSV cell.</param>
        private static void SetValue(object v, FieldInfo fieldInfo, string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                var type = fieldInfo.FieldType;
                if (type == typeof(string))
                {
                    value = string.Empty;
                }
                else if (type == typeof(int) || type == typeof(float) || type == typeof(double) || type == typeof(long))
                {
                    value = "0";
                }
                else if (type == typeof(bool))
                {
                    value = "FALSE";
                }
                else if (fieldInfo.FieldType.IsEnum)
                {
                    var defaultValue =
                        Enum.GetValues(fieldInfo.FieldType)
                            .GetValue(0); // Lấy giá trị đầu tiên trong enum như là giá trị mặc định
                    Debug.LogWarning(fieldInfo.Name + "-value is nil and set value default");
                    value = defaultValue.ToString();
                }
            }

            if (fieldInfo.FieldType.IsArray)
            {
                var elementType = fieldInfo.FieldType.GetElementType();
                var elem = value.Split(',', '~');
                if (elem.Length <= 1) elem = value.Split('|', '~');

                var arrayValue = string.IsNullOrEmpty(value)
                    ? null
                    : Array.CreateInstance(elementType ?? throw new InvalidOperationException(), elem.Length);

                if (arrayValue == null) goto SetValue;

                for (var i = 0; i < elem.Length; i++)
                {
                    if (elementType == typeof(string))
                        arrayValue.SetValue(elem[i], i);
                    else
                        arrayValue.SetValue(
                            elementType.IsEnum
                                ? Enum.Parse(elementType, elem[i])
                                : Convert.ChangeType(elem[i], elementType), i);
                    ;
                }

                SetValue:

                fieldInfo.SetValue(v, arrayValue);
            }
            else if (fieldInfo.FieldType.IsEnum)
            {
                try
                {
                    fieldInfo.SetValue(v, Enum.Parse(fieldInfo.FieldType, value));
                }
                catch (Exception e)
                {
                    Debug.LogWarning("enum fieldInfo: " + fieldInfo.FieldType);
                    Debug.LogWarning("enum value: " + value);
                    Debug.LogWarning(e);
                    fieldInfo.SetValue(v, default);
                }
            }
            else if (value.IndexOf('.') != -1 &&
                     (fieldInfo.FieldType == typeof(int) || fieldInfo.FieldType == typeof(long) ||
                      fieldInfo.FieldType == typeof(short)))
            {
                var f = (float)Convert.ChangeType(value, typeof(float));
                fieldInfo.SetValue(v, Convert.ChangeType(f, fieldInfo.FieldType));
            }
            else if (fieldInfo.FieldType == typeof(string))
            {
                fieldInfo.SetValue(v, value);
            }
            else
            {
                try
                {
                    fieldInfo.SetValue(v, Convert.ChangeType(value, fieldInfo.FieldType));
                }
                catch
                {
                    Debug.LogWarning("V: " + v + "------value: " + value + " -------- field: " + fieldInfo);
                }
            }
        }

        /// <summary>
        ///     Populates an ID-value structure model from CSV rows mapping row IDs to target fields.
        /// </summary>
        /// <param name="type">The Type to instantiate.</param>
        /// <param name="rows">Pre-parsed CSV rows list.</param>
        /// <param name="idCol">The index of the ID column.</param>
        /// <param name="valCol">The index of the Value column.</param>
        /// <returns>A fully populated object instance.</returns>
        private static object CreateIdValue(Type type, List<string[]> rows, int idCol = 0, int valCol = 1)
        {
            var v = Activator.CreateInstance(type);

            var table = new Dictionary<string, int>();

            for (var i = 1; i < rows.Count; i++)
                if (rows[i][idCol].Length > 0)
                    table.Add(rows[i][idCol].TrimEnd(' '), i);

            var fieldInfo =
                type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            foreach (var tmp in fieldInfo)
                if (table.ContainsKey(tmp.Name))
                {
                    var idx = table[tmp.Name];
                    if (rows[idx].Length > valCol)
                        SetValue(v, tmp, rows[idx][valCol]);
                }
                else
                {
                    Debug.Log("Miss " + tmp.Name);
                }

            return v;
        }

        #endregion

        #region Private Methods

        /// <summary>
        ///     Retrieves a Type by its fully qualified name, searching across all loaded assemblies if needed.
        /// </summary>
        /// <param name="strFullyQualifiedName">The fully qualified type name string.</param>
        /// <returns>The resolved Type.</returns>
        private static Type GetType(string strFullyQualifiedName)
        {
            var type = Type.GetType(strFullyQualifiedName);
            if (type == null)
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    type = asm.GetType(strFullyQualifiedName);
                    if (type != null)
                        break;
                }

            if (type == null) throw new Exception("BattleUnitType is null: " + strFullyQualifiedName);

            return type;
        }

        /// <summary>
        ///     Resolves the starting column index for fields of a nested object class within the fields table.
        /// </summary>
        /// <param name="type">The type of the nested object.</param>
        /// <param name="table">The column mapping dictionary.</param>
        /// <returns>The minimum column index for the target object's fields.</returns>
        private static int GetObjectIndex(Type type, Dictionary<string, int> table)
        {
            var minIndex = int.MaxValue;
            var fieldInfo =
                type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            foreach (var tmp in fieldInfo)
                if (table.ContainsKey(tmp.Name))
                {
                    var idx = table[tmp.Name];
                    if (idx < minIndex)
                        minIndex = idx;
                }

            //Debug.Log("Miss " + tmp.Name);
            return minIndex;
        }

        /// <summary>
        ///     Calculates the number of elements of a list/array starting from the given row index.
        /// </summary>
        /// <param name="rowIndex">The current row index to evaluate.</param>
        /// <param name="objectIndex">The starting column index of the child object.</param>
        /// <param name="parentIndex">The starting column index of the parent object.</param>
        /// <param name="rows">Pre-parsed CSV rows list.</param>
        /// <returns>A tuple containing the element count and list of starting row indices.</returns>
        private static (int, List<int>) CountNumberElement(int rowIndex, int objectIndex, int parentIndex,
            List<string[]> rows)
        {
            var count = 0;
            var startRows = new List<int>();
            for (var i = rowIndex; i < rows.Count; i++)
            {
                var row = rows[i];
                if (!row[objectIndex].Equals(string.Empty))
                {
                    if (objectIndex == parentIndex)
                    {
                        count++;
                        startRows.Add(i);
                    }
                    else if (row[parentIndex].Equals(string.Empty) || i == rowIndex)
                    {
                        count++;
                        startRows.Add(i);
                    }
                    else
                    {
                        break;
                    }
                }
            }

            return (count, startRows);
        }

        /// <summary>
        ///     Validates that a CSV header key is lower-case.
        /// </summary>
        /// <param name="key">The key string.</param>
        /// <returns>True if the key is fully lower-case, false otherwise.</returns>
        private static bool IsValidKeyFormat(string key)
        {
            return key.Equals(key.ToLower());
        }

        /// <summary>
        ///     Checks whether the field is of a primitive type or array of primitive types.
        /// </summary>
        /// <param name="tmp">The FieldInfo to check.</param>
        /// <returns>True if the field or its elements are primitive types, false otherwise.</returns>
        private static bool IsPrimitive(FieldInfo tmp)
        {
            Type type;
            if (tmp.FieldType.IsArray)
                type = GetElementTypeFromFieldInfo(tmp);
            else
                type = tmp.FieldType;

            return IsPrimitive(type);
        }

        /// <summary>
        ///     Evaluates if a Type is string, enum, or system primitive type.
        /// </summary>
        /// <param name="type">The Type to check.</param>
        /// <returns>True if primitive, false otherwise.</returns>
        private static bool IsPrimitive(Type type)
        {
            return type == typeof(string) || type.IsEnum || type.IsPrimitive;
        }

        /// <summary>
        ///     Retrieves the element type of an array field from its FieldInfo.
        /// </summary>
        /// <param name="tmp">The target field info.</param>
        /// <returns>The resolved element Type.</returns>
        private static Type GetElementTypeFromFieldInfo(FieldInfo tmp)
        {
            var fullName = string.Empty;
            if (tmp.FieldType.IsArray)
            {
                if (tmp.FieldType.FullName != null)
                    fullName = tmp.FieldType.FullName.Substring(0, tmp.FieldType.FullName.Length - 2);
            }
            else
            {
                fullName = tmp.FieldType.FullName;
            }

            return GetType(fullName);
        }

        /// <summary>
        ///     Converts a snake_case styled string to camelCase styled string.
        /// </summary>
        /// <param name="snakeCase">The snake_case string.</param>
        /// <returns>The converted camelCase string.</returns>
        private static string ConvertSnakeCaseToCamelCase(string snakeCase)
        {
            var strings = snakeCase.Split(new[] { "_" }, StringSplitOptions.RemoveEmptyEntries);
            var result = strings[0];
            for (var i = 1; i < strings.Length; i++)
            {
                var currentString = strings[i];
                result += char.ToUpperInvariant(currentString[0]) +
                          currentString.Substring(1, currentString.Length - 1);
            }

            return result;
        }

        #endregion
    }
}
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;

namespace Ezg.Package.CsvReader
{
    public class CSVReaderManager
    {
        #region Fields

        public TextAsset csvFile;

        private static readonly string SPLIT_RE = ",";

        private static string SPLIT_TSV = "\t";

        //static string SPLIT_RE = @",(?=(?:[^""]*""[^""]*"")*(?![^""]*""))";
        private static readonly string LINE_SPLIT_RE = @"\r\n|\n\r|\n|\r";

        //static string LINE_SPLIT_RE = @"\n|\r";
        private static readonly char[] TRIM_CHARS = { '\"' };
        private static readonly string comma = "|";

        #endregion

        #region Public Methods

        /// <summary>
        ///     Builds a dictionary mapping column header names to their corresponding column index.
        /// </summary>
        /// <param name="datas">A 2D string array representing the CSV grid cells.</param>
        /// <returns>A dictionary mapping header strings to column indices.</returns>
        public static Dictionary<string, int> GetColumnNameIndex(string[,] datas)
        {
            var result = new Dictionary<string, int>();
            var column = datas.GetLength(0);
            for (var i = 0; i < column; i++)
            {
                if (string.IsNullOrEmpty(datas[i, 0]))
                    return result;
                result.Add(datas[i, 0], i);
            }

            return result;
        }

        /// <summary>
        ///     Starts the CSV parsing execution (called by Unity).
        /// </summary>
        public void Start()
        {
            var grid = SplitCsvGrid(csvFile.text);
            Debug.Log("size = " + (1 + grid.GetUpperBound(0)) + "," + (1 + grid.GetUpperBound(1)));

            DebugOutputGrid(grid);
        }

        /// <summary>
        ///     Outputs the contents of a 2D grid array to the console log, useful for debugging CSV imports.
        /// </summary>
        /// <param name="grid">The 2D string grid to print.</param>
        public static void DebugOutputGrid(string[,] grid)
        {
            var textOutput = "";
            for (var y = 0; y < grid.GetUpperBound(1); y++)
            {
                for (var x = 0; x < grid.GetUpperBound(0); x++)
                {
                    textOutput += grid[x, y];
                    textOutput += "|";
                }

                textOutput += "\n";
            }

            Debug.Log(textOutput);
        }

        /// <summary>
        ///     Splits a CSV file raw text into a 2D string array grid.
        /// </summary>
        /// <param name="csvText">The raw CSV text.</param>
        /// <returns>A 2D string array grid representing rows and columns.</returns>
        public static string[,] SplitCsvGrid(string csvText)
        {
            var lines = csvText.Split("\n"[0]);

            // finds the max width of row
            var width = 0;
            for (var i = 0; i < lines.Length; i++)
            {
                var row = SplitCsvLine(lines[i]);
                width = Mathf.Max(width, row.Length);
            }

            // creates new 2D string grid to output to
            var outputGrid = new string[width, lines.Length];
            for (var y = 0; y < lines.Length; y++)
            {
                var row = SplitCsvLine(lines[y]);
                for (var x = 0; x < row.Length; x++)
                {
                    outputGrid[x, y] = row[x];

                    // This line was to replace "" with " in my output. 
                    // Include or edit it as you wish.
                    outputGrid[x, y] = outputGrid[x, y].Replace("\"\"", "\"");
                }
            }

            return outputGrid;
        }

        /// <summary>
        ///     Splits a single CSV row string into a string array using regular expressions.
        /// </summary>
        /// <param name="line">The single row text line.</param>
        /// <returns>An array of cell values.</returns>
        public static string[] SplitCsvLine(string line)
        {
            return (from Match m in Regex.Matches(line,
                    @"(((?<x>(?=[,\r\n]+))|""(?<x>([^""]|"""")+)""|(?<x>[^,\r\n]+)),?)",
                    RegexOptions.ExplicitCapture)
                select m.Groups[1].Value).ToArray();
        }

        /// <summary>
        ///     Groups a flat list of CSV row dictionaries into nested lists based on a separator column value.
        /// </summary>
        /// <param name="csvData">The parsed flat CSV data dictionary list.</param>
        /// <param name="tagSeparator">The column name key to group by.</param>
        /// <returns>A list of grouped lists of row dictionaries.</returns>
        public static List<List<Dictionary<string, string>>> GetListGroupCSV(List<Dictionary<string, string>> csvData,
            string tagSeparator)
        {
            var result = new List<List<Dictionary<string, string>>>();
            var csvListOfElement = new List<Dictionary<string, string>>();
            var groupIdentify = string.Empty;
            foreach (var row in csvData)
            {
                if (row.ContainsKey(tagSeparator) && !row[tagSeparator].Equals(""))
                {
                    var groupIdentifyNext = row[tagSeparator].Trim();
                    if (!string.IsNullOrEmpty(groupIdentifyNext))
                    {
                        if (string.IsNullOrEmpty(groupIdentify)) groupIdentify = groupIdentifyNext;

                        if (!groupIdentifyNext.Equals(groupIdentify))
                        {
                            result.Add(csvListOfElement);
                            csvListOfElement = new List<Dictionary<string, string>>();

                            groupIdentify = groupIdentifyNext;
                        }
                    }
                }

                csvListOfElement.Add(row);
            }

            if (!string.IsNullOrEmpty(groupIdentify)) result.Add(csvListOfElement);

            if (result.Count == 0) return new List<List<Dictionary<string, string>>> { csvData };

            return result;
        }

        /// <summary>
        ///     Reads and deserializes a CSV TextAsset into a flat list of cell key-value dictionaries.
        /// </summary>
        /// <param name="data">The text asset.</param>
        /// <returns>A list of row dictionaries mapping column headers to cell values.</returns>
        public static List<Dictionary<string, string>> Read(TextAsset data)
        {
            return Read(data.text);
        }

        /// <summary>
        ///     Reads and deserializes a raw CSV content string into a flat list of cell key-value dictionaries.
        /// </summary>
        /// <param name="content">The raw CSV text.</param>
        /// <param name="lowerCaseKey">If true, converts header keys to lowercase.</param>
        /// <returns>A list of row dictionaries mapping column headers to cell values.</returns>
        public static List<Dictionary<string, string>> Read(string content, bool lowerCaseKey = false)
        {
            return ReadSpecialSplit(content, lowerCaseKey, SPLIT_RE);
        }

        /// <summary>
        ///     Reads and deserializes a CSV content string using a custom split character into a flat list of cell dictionaries.
        /// </summary>
        /// <param name="content">The raw CSV text.</param>
        /// <param name="lowerCaseKey">If true, converts header keys to lowercase.</param>
        /// <param name="keySplit">The custom split character sequence (e.g. comma or tab).</param>
        /// <returns>A list of row dictionaries mapping column headers to cell values.</returns>
        public static List<Dictionary<string, string>> ReadSpecialSplit(string content, bool lowerCaseKey = false,
            string keySplit = ",")
        {
            var list = new List<Dictionary<string, string>>();
            var lines = Regex.Split(content, LINE_SPLIT_RE);
            if (lines.Length <= 1)
                return list;

            var header = Regex.Split(lines[0], keySplit);
            for (var i = 1; i < lines.Length; i++)
            {
                var values = Regex.Split(lines[i], keySplit);
                if (values.Length == 0)
                    continue;

                var isEmptyAll = true;
                for (var j = 0; j < values.Length; j++)
                    if (!values[j].Equals(""))
                    {
                        isEmptyAll = false;
                        break;
                    }

                if (isEmptyAll) continue;

                var entry = new Dictionary<string, string>();
                for (var j = 0; j < header.Length && j < values.Length; j++)
                {
                    var value = values[j];
                    value = value.TrimStart(TRIM_CHARS).TrimEnd(TRIM_CHARS);
                    value = value.Replace(comma, ",");
                    var key = lowerCaseKey ? header[j].ToLower() : header[j];
                    key = key.TrimStart(' ').TrimEnd(' ');
                    entry[key] = value;
                }

                list.Add(entry);
            }

            return list;
        }

        #endregion
    }
}
namespace Ezg.Package.CsvReader
{
    public interface ICsvCustomData
    {
        /// <summary>
        ///     Imports raw CSV data string into the asset.
        /// </summary>
        /// <param name="data">The raw CSV text data string.</param>
        public void ImportData(string data);
    }
}
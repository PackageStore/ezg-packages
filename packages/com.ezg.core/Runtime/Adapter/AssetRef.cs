namespace Ezg.Core.Adapter
{
    /// <summary>
    ///     Represents a reference to an asset, specifying its bundle and file path.
    /// </summary>
    public readonly struct AssetRef
    {
        #region Fields

        /// <summary>
        ///     The name of the bundle containing the asset.
        /// </summary>
        public readonly string bundle;

        /// <summary>
        ///     The resource path of the asset.
        /// </summary>
        private readonly string _path;

        #endregion

        #region Initialize

        /// <summary>
        ///     Initializes a new instance of the <see cref="AssetRef" /> struct.
        /// </summary>
        /// <param name="resourcePath">The relative path of the resource.</param>
        /// <param name="bundle">The name of the asset bundle.</param>
        public AssetRef(string resourcePath, string bundle = null)
        {
            _path = resourcePath;
            this.bundle = bundle;
        }

        #endregion

        #region Public Methods

        /// <summary>
        ///     Gets the name of the asset, extracted from its path.
        /// </summary>
        public string Name
        {
            get
            {
                var i = _path.LastIndexOf('/');
                return i < 0 ? _path : _path.Substring(i + 1);
            }
        }

        /// <summary>
        ///     Gets the folder path containing the asset.
        /// </summary>
        public string Folder
        {
            get
            {
                var i = _path.LastIndexOf('/');
                return i < 0 ? "" : _path.Substring(0, i + 1);
            }
        }

        #endregion
    }
}
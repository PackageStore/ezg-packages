namespace UnityFigmaBridge.Editor.PostProcess
{
    /// <summary>
    ///     Extension point run once at the end of every document import (after component
    ///     instantiation, nine-slice collapse and behaviour binding), and again on demand from
    ///     <c>Run Post-Processors (no Sync)</c> without any network access.
    ///
    ///     Implement this in any Editor assembly of the project. Types are found with
    ///     <c>TypeCache.GetTypesDerivedFrom</c>, so nothing has to be registered: a concrete class
    ///     with a public parameterless constructor is enough. Processors run in ascending
    ///     <see cref="Order"/>; a processor that throws is logged and does not stop the others.
    ///
    ///     The raw prefabs the bridge wrote are the input. A processor should treat them as
    ///     read-only intermediate output and write its own assets elsewhere: the next Sync
    ///     overwrites everything under the bridge output folders.
    /// </summary>
    public interface IFigmaImportPostProcessor
    {
        /// <summary>Lower runs first. Ties are broken by type name so the order is stable.</summary>
        int Order { get; }

        /// <summary>Called once per import with everything the bridge knows about what it wrote.</summary>
        void OnDocumentImported(FigmaImportContext context);
    }
}

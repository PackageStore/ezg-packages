#nullable enable

namespace Ezg.IconCsvGenerator.Editor
{
    using System;

    /// <summary>
    /// Resolves {token} placeholders in a prompt template string against an
    /// <see cref="IconRowModel.Fields"/> dictionary.
    ///
    /// Delegates the actual regex substitution to <see cref="IconTokenResolver"/> (SSOT).
    /// Per development-principles.md: NO silent fallbacks.
    /// If a {token} has no matching column, throws with a clear message naming the token + row Id.
    /// </summary>
    internal static class IconPromptResolver
    {
        /// <summary>
        /// Resolves all {token} placeholders in the template against the row's Fields dictionary.
        /// Uses raw values (no filename sanitization) so the full CSV text appears in the prompt.
        /// </summary>
        /// <param name="promptTemplate">The raw prompt template string with {token} placeholders.</param>
        /// <param name="row">The row whose Fields provide substitution values.</param>
        /// <returns>The resolved prompt string with all placeholders replaced.</returns>
        /// <exception cref="InvalidOperationException">
        /// Thrown when a {token} has no matching key in <paramref name="row"/>.Fields.
        /// </exception>
        public static string Resolve(string promptTemplate, IconRowModel row)
        {
            if (promptTemplate == null) throw new ArgumentNullException(nameof(promptTemplate));
            if (row == null) throw new ArgumentNullException(nameof(row));

            return IconTokenResolver.ResolveRaw(promptTemplate, row.Fields, row.Id);
        }
    }
}

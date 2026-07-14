#nullable enable

namespace Ezg.IconCsvGenerator.Editor
{
    using UnityEditor;

    /// <summary>
    /// Shows a cost-estimate confirm dialog before any Gemini API calls are fired.
    ///
    /// Per decision #12: ZERO API calls may be made until the operator explicitly confirms
    /// after seeing the row count, model name, and estimated cost.
    ///
    /// The per-image price is intentionally a named constant (<see cref="ESTIMATED_COST_PER_IMAGE_USD"/>)
    /// because Gemini pricing changes independently of this tool — update the constant when the
    /// pricing page changes, NOT inline magic numbers.
    /// </summary>
    internal static class IconCostEstimator
    {
        // ── Constants ────────────────────────────────────────────────────────────

        /// <summary>
        /// Estimated cost per generated image in USD.
        /// Source: Gemini API pricing page for gemini-3.1-flash-image (2026-06).
        /// Update this constant when pricing changes — do not hardcode inline.
        /// </summary>
        private const double ESTIMATED_COST_PER_IMAGE_USD = 0.003;

        /// <summary>Model identifier shown in the confirm dialog (mirrors GeminiImageClient).</summary>
        private const string MODEL_DISPLAY_NAME = "gemini-3.1-flash-image";

        /// <summary>Dialog title shown in the EditorUtility dialog.</summary>
        private const string DIALOG_TITLE = "Confirm Icon Generation";

        // ── Public API ────────────────────────────────────────────────────────────

        /// <summary>
        /// Shows a blocking <see cref="EditorUtility.DisplayDialog"/> with the cost estimate
        /// and returns <c>true</c> only if the operator clicks "Generate".
        /// </summary>
        /// <param name="selectedRowCount">Number of rows selected for generation.</param>
        /// <returns><c>true</c> if confirmed, <c>false</c> if cancelled.</returns>
        public static bool ConfirmGeneration(int selectedRowCount)
        {
            if (selectedRowCount <= 0)
            {
                EditorUtility.DisplayDialog(
                    DIALOG_TITLE,
                    "No rows selected. Select at least one row before generating.",
                    "OK");
                return false;
            }

            var estimatedCost = selectedRowCount * ESTIMATED_COST_PER_IMAGE_USD;
            var message =
                $"You are about to generate {selectedRowCount} icon(s) using the Gemini API.\n\n" +
                $"Model: {MODEL_DISPLAY_NAME}\n" +
                $"Estimated cost: ~${estimatedCost:F3} USD ({selectedRowCount} × ${ESTIMATED_COST_PER_IMAGE_USD:F3}/image)\n\n" +
                "Note: actual cost depends on your Gemini billing tier and any free-tier quota. " +
                "No API calls are made if you cancel.\n\n" +
                "Proceed?";

            return EditorUtility.DisplayDialog(DIALOG_TITLE, message, "Generate", "Cancel");
        }

        /// <summary>
        /// Returns a formatted cost estimate string for display in the window UI
        /// without showing a dialog.
        /// </summary>
        public static string FormatEstimate(int rowCount)
        {
            if (rowCount <= 0) return "0 images — $0.000";
            var cost = rowCount * ESTIMATED_COST_PER_IMAGE_USD;
            return $"{rowCount} image(s) — ~${cost:F3} USD";
        }
    }
}

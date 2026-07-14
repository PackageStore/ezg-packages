#nullable enable

namespace Ezg.IconCsvGenerator.Editor
{
    using UnityEngine;

    // ---------------------------------------------------------------------------
    // Per-row review state for the thumbnail review grid.
    // Owned by Phase 6.
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Lifecycle of one icon in the review grid.
    /// Transitions: Pending → Approved | Rejected.
    /// Skipped is set by <see cref="IconExistenceChecker"/> when the file already exists.
    /// Regenerate resets a row back to Pending and re-fires the Gemini request.
    /// </summary>
    internal enum ReviewStatus
    {
        /// <summary>Waiting for user decision (default after generation).</summary>
        Pending,

        /// <summary>Operator approved — will be written as a PSD on Write.</summary>
        Approved,

        /// <summary>Operator rejected — discarded, no PSD written.</summary>
        Rejected,

        /// <summary>
        /// Idempotency skip: file already exists in _Incoming or Sprites/ tree.
        /// Treated as Rejected (no write) unless force-all is enabled.
        /// </summary>
        Skipped,

        /// <summary>Waiting in the generation pool for a free concurrency slot.</summary>
        Queued,

        /// <summary>
        /// Generation in progress (coroutine running).
        /// Prevents duplicate requests on the same row.
        /// </summary>
        Generating,

        /// <summary>
        /// Generation failed — Error field contains raw Gemini response.
        /// Operator can Regenerate to retry.
        /// </summary>
        Failed,
    }

    /// <summary>
    /// Holds the mutable review state for a single <see cref="IconRowModel"/> row.
    /// One instance per loaded row; created when the window loads rows.
    /// </summary>
    internal sealed class IconReviewStateItem
    {
        /// <summary>The source CSV row this state tracks.</summary>
        public IconRowModel Row { get; set; }

        /// <summary>
        /// Preview texture decoded from Gemini PNG bytes.
        /// Null until generation succeeds. Destroyed and replaced on Regenerate.
        /// </summary>
        public Texture2D? Preview { get; set; }

        /// <summary>
        /// Raw PNG bytes from a successful Gemini result.
        /// Kept for PSD encoding on Approve+Write (avoids re-decoding the texture).
        /// </summary>
        public byte[]? PngBytes { get; set; }

        /// <summary>Current review status of this row.</summary>
        public ReviewStatus Status { get; set; } = ReviewStatus.Pending;

        /// <summary>
        /// Whether this row is selected for generation (controlled by per-row checkbox).
        /// Defaults to true so "select all" works out of the box.
        /// </summary>
        public bool IsSelected { get; set; } = true;

        /// <summary>
        /// Human-readable error from a failed generation.
        /// Null when Status != Failed.
        /// </summary>
        public string? Error { get; set; }

        /// <summary>
        /// Per-item EXTRA prompt set by the artist in the item row. When non-null/non-whitespace,
        /// it is appended after the resolved group System Prompt at generation time — i.e. the only
        /// text that differs from the shared system prompt. Null = no extra.
        /// Session-scoped: resets on Reload (not persisted).
        /// </summary>
        public string? PromptOverride { get; set; }

        /// <summary>
        /// Absolute path to the on-disk PNG cache of this row's most recent successful generation
        /// (see <see cref="IconGenCache"/>). Lets generated-but-unapproved work survive an Editor
        /// script recompile / domain reload. Null until first generation or cache restore.
        /// </summary>
        public string? CachePath { get; set; }

        public IconReviewStateItem(IconRowModel row)
        {
            this.Row = row;
        }

        /// <summary>
        /// Destroys the current preview texture and resets to Pending ready for re-generation.
        /// Does NOT reset PromptOverride — the artist's edits are preserved across regenerates.
        /// </summary>
        public void ResetForRegenerate()
        {
            if (this.Preview != null)
            {
                UnityEngine.Object.DestroyImmediate(this.Preview);
                this.Preview = null;
            }
            this.PngBytes = null;
            this.Error = null;
            this.Status = ReviewStatus.Pending;
        }
    }
}

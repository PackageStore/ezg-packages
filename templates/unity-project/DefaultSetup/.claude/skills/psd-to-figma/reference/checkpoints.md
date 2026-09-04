# Checkpoints

A stage can be killed mid-task (a usage-limit pause, a crash). A checkpoint lets
the successor resume in minutes instead of redoing the stage. Every brief's first
instruction is to read its checkpoint and continue from `next`.

## File

`<data>/.progress/<stage>-<key>.md`, one per running stage:

- `<stage>` is the brief's stage: `data`, `art`, `components`, `screen-build`,
  `screen-verify`, `gate`.
- `<key>` identifies the unit of work — a screen key for the screen stages, a
  component set for `components`, `all` for a whole-run stage (`data`, `art`,
  `gate`).

`.progress/` is a data-dir concern, covered by the `<data>/.gitignore` the scripts
write on their first run; it is never written under the skill's `scripts/`.

## Fields

Write the checkpoint after each completed step:

- `step` — the last step finished (a number or short name from the brief).
- `ids created` — Figma node ids created so far, with what each is.
- `files written` — data files written or changed this stage.
- `next` — the exact next step to run.

## Rules

- Read the checkpoint before doing anything; if it exists, jump to `next`.
- Update it after every completed step, not only at the end.
- **Remove an id from `ids created` the moment you delete its node.** A probe or
  placement frame that has been deleted must not linger as "created" — a resumed
  successor would try to reuse or re-delete a node that no longer exists.
- Delete the checkpoint when the stage is fully done and its acceptance passes.

## Example

```markdown
step: 3 (art placed, extract matched)
ids created:
  - 812:44  screen frame
  - 812:51  plate instance
next: step 4 — create TEXT nodes with textByInk, then re-gate
files written:
  - figma_extract_<key>.json
```

When step 8 deletes a probe frame `812:99`, its line leaves `ids created` in the
same edit that records the deletion.

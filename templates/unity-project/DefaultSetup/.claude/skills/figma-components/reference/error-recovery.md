# Error recovery

Forked from the Figma plugin skill `figma-generate-library` (v2.2.95)
`references/error-recovery.md`. The protocol is upstream's. The error table and
the cleanup rules are this file's. `<body-font>` below is the project body font
(`figma.fonts.body`).

## 1. Protocol

`use_figma` is **atomic**. A script that throws did not execute; the file is
unchanged. There are no partial nodes to clean up from a failed call.

1. **Stop.** Do not fix and retry in the same breath.
2. **Read the error.** Most of them name the exact rule that was broken.
3. **Inspect if unclear** — `get_metadata`, or a read-only `use_figma`.
4. **Fix the script.**
5. **Retry.**

The failure mode that costs the most time is a script that *succeeds* and
produces something wrong. Validate after every create.

## 2. Cleanup by id, never by name

Never delete by name prefix. `page.findAll(n => n.name.startsWith('Btn'))`
matches masters a person made by hand.

```javascript
const IDS = ['1:23', '1:24'];        // from the previous call's return value
const removed = [];
for (const id of IDS) {
  const n = await figma.getNodeByIdAsync(id);
  if (n && !n.removed) { const name = n.name; n.remove(); removed.push({ id, name }); }
}
return { removed };
```

`node.remove()` invalidates the handle immediately — read every field you need
before removing.

## 3. Check before create

Every create is idempotent or it is a bug. A re-run must not produce
`Button` and `Button 2`.

```javascript
const page = figma.root.children.find(p => p.name === 'Components');
await figma.setCurrentPageAsync(page);

const existing = page.findOne(n =>
  (n.type === 'COMPONENT_SET' || n.type === 'COMPONENT') && n.name === 'Button');

if (existing) return { existed: true, id: existing.id, type: existing.type };
```

## 4. State ledger

Long component work outruns the context window. Write the ledger to disk in the
session scratchpad and re-read it at the start of every turn.

```json
{
  "runId": "components-<date>",
  "phase": "states",
  "sets": {
    "Button": { "id": "<id>", "axes": ["Color"], "statesAdded": false },
    "<ComponentName>": { "id": "<id>", "axes": ["Type"], "statesAdded": false }
  },
  "created": [],
  "pendingValidation": ["Button"],
  "done": ["audit"]
}
```

Never reconstruct a node id from memory. Every id comes from a return value or
from a fresh read.

## 5. Resume

At session start, or after context truncation, run one read-only `use_figma`
that inventories the `Components` and `Icons` pages by name, then reconcile
against the ledger file. Names are stable; ids in your head are not.

## 6. Error table

| Error | Cause | Fix |
|---|---|---|
| `Component set has existing errors` | Duplicate variant names in the set | Make every variant name unique, then re-read |
| `Can only get component property definitions of a component set or non-variant component` | Read from a variant | Narrow to the parent set first |
| `Setting figma.currentPage is not supported` | Sync page setter | `await figma.setCurrentPageAsync(page)` |
| `in set_layoutSizingHorizontal: FILL can only be set on children of auto-layout frames` | Set before `appendChild`, or parent is not auto-layout | Append first; make the parent auto-layout |
| `Expected 'FIXED' \| 'AUTO', received 'FILL'` | Crossed the two sizing enums | `layoutSizing*` takes FIXED/HUG/FILL; `*AxisSizingMode` takes FIXED/AUTO |
| `Cannot write to node with unloaded font "<body-font>"` | Font not loaded | `await figma.loadFontAsync(BODY_FONT)` before the write, with `BODY_FONT = figma.fonts.body` |
| `x`/`y` assignment throws, `resize()` does nothing | The node is a child of an `INSTANCE` | Edit the master. Instance children cannot move or resize |
| `combineAsVariants` throws | A non-`COMPONENT` node in the array | Filter by `type === 'COMPONENT'` |
| `figma.createSlot is not a property` | Slots are unavailable in this environment | Use the exposed-instance fallback in `slots-guide.md` |
| `The node with id X does not exist` | A parent instance was detached, changing ids | Re-discover by traversal from a non-instance parent |
| Set frame clips its variants | `resizeWithoutConstraints` not called after layout | Recompute bounds from children |
| `figma.notify is not implemented` | Used `figma.notify()` | Remove it; `return` is the output channel |

## 7. Recovery by phase

**Audit fails.** Read-only. Re-run with a narrower scope; the page may be large.

**A master is half built.** The script was atomic, so the master is either whole
or absent. If a *previous* call created it and a later call failed, delete by
the ids in the ledger and rebuild.

**States half applied.** `addStateVariants.js` clones then renames. If it failed
after cloning, the set has variants whose names lack the `State=` part, which
puts the set into a duplicate-name error. Delete the clones by id and re-run.

**Promotion half applied.** Some screen copies are instances and some are not.
This is the dangerous one — it changes the Screens page. Finish the swap rather
than reverting, then run the `psd-to-figma` verify pass on every screen touched.
If verify fails, the master's geometry is wrong; fix the master, not the
instances.

**Description or binding failed.** Deterministic and cheap. Re-run the same
script; both operations are idempotent.

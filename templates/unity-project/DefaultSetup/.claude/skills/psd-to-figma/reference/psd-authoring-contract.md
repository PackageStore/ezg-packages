# PSD Authoring Contract

Rules for structuring a PSD so it imports as grouped, component-based Figma
frames on the first run. Each rule names the lint id that enforces it.

---

## 1. Frame

One artboard per screen at the project frame size. Art wider than the frame
declares its horizontal offset `dx` in `screens.json`.

## 2. Groups are containers

Every region is a named group. A screen root holds groups, not loose layers.
More than eight ungrouped visible leaves at the root is an error (**L-2**).
Group names become container names — the generator adds the `Container-`
prefix automatically.

## 3. Names

Use `Prefix-Name` with PascalCase segments and hyphen separators:
`Btn-Buy`, `Text-Title`, `Icon-Gem`.

Do not leave Photoshop default names (`Rectangle 7`, `Layer 1`) or copy
suffixes (`icon copy 3`). Every such name is an error (**L-1**).

## 4. Repeats are smart objects

A widget used more than twice must be one linked smart object placed N times.
The pipeline clusters instances by their smart-object id; a flat merge of the
whole grid into one bitmap defeats both reuse detection and 9-slice.

Never merge repeated widgets into a single pixel layer (**L-3**).

Linked smart objects are preferred over embedded ones. `psd-tools` reads the
placed id either way, but linked objects keep the PSD smaller.

## 5. Demo art

Content that is not part of the UI — sample data, placeholder grids, test
imagery — belongs in a group named `Demo`, set to hidden. The import skips
groups whose name contains `demo`, `grid`, or `dont_use` (**L-3**).

Large pixel layers that cover 40 % or more of their parent and sit among
three or more siblings are also flagged as likely baked demo art (**L-3**).

## 6. Text

One type layer per string.

| rule | lint |
|---|---|
| No rotation on text layers. | **L-4** (P-15) |
| Use fonts from the project's designated family. A condensed font that is not a real OS family causes a substitution pin on every run. | **L-6** |
| Delete or disable any effect you do not want rendered. Disabled effects in the PSD produce wrong output (especially strokes). | **L-5** (P-17) |
| Set fill opacity to 100 %; use layer opacity for fades. Fill opacity below 100 % has no direct Figma equivalent. | **L-7** |

The raw `fontSize` from psd-tools ignores the type layer's transform scale.
The pipeline multiplies by the transform's scale factor automatically (P-16).

A Photoshop paragraph box clips overflow; Figma text does not. The pipeline
wraps cropped text in a clipping frame when configured (P-23).

## 7. Plates

Stretchable plates (buttons, panels, cards) need a flat middle band for the
9-slice detector. A gradient that runs along the stretch axis makes the plate
un-sliceable — keep gradients perpendicular or use a solid centre.

## 8. States

A hidden layer that overlaps a visible sibling with the same stem name is
treated as a state variant (**L-8**). Move each variant into its own group
named `<Name>-State-<State>` (e.g. `Btn-Buy-State-Disabled`), still hidden.

## 9. Checklist

| id | check |
|---|---|
| **L-1** | No default or copy names. |
| **L-2** | Root has groups, not a flat pile of layers. |
| **L-3** | No baked demo art; demo content in a hidden `Demo` group. |
| **L-4** | No rotated text. |
| **L-5** | No disabled effects left on layers. |
| **L-6** | Fonts from the project's designated family only. |
| **L-7** | Fill opacity is 100 %; layer opacity for fades. |
| **L-8** | Hidden state variants in their own groups. |

Run the lint:

```
python3 <scripts>/psd_lint.py --data-dir <data>
```

A PSD that predates this contract is still importable. The lint distinguishes
errors (L-1, L-2, L-3, L-8) from warnings (L-4, L-5, L-6, L-7); fix errors
before importing.

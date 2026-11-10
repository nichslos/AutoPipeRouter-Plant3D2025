# SPTC Production Iso REV21 — Stretch Native Filter and Keep Lines Visible

**Baseline:** REV20. Earlier revision source branches and DLLs are unchanged.

## Fix 1: Native Filter bar follows the resized Display lines pane
In REV20, the left **Display lines** GroupBox and line checklist could be widened, but the Autodesk native Line Numbers filter/search control and its funnel stayed at their old positions. This made the original funnel overlap the search area while the new folder arrow had already moved to the far right.

REV21:
- Captures the **original** filter textbox and native funnel positions before widening the panel.
- Resizes the filter textbox (and its fixed-width ancestor containers, when present) to follow the expanded left pane.
- Moves the original funnel icon to stay near the right edge of the Display lines section.
- Anchors the custom folder dropdown just to the **left** of the original funnel using the GroupBox's current right edge (not the old search textbox width).
- Preserves the same native search text; the resize handler only changes UI geometry and does not set filter values or modify line groups.
- Keeps the draggable splitter and wider line checklist from REV20.

## Fix 2: Avoid leaving a zero-line native list after automatic active-route filtering
REV09+ uses Autodesk's native search textbox to implement the Drawing/Project active/all toggle. If the verified line tag does **not** match the native list in the selected Drawing/Project mode, REV20 could leave a long tag in the search textbox with **Select All (0)** and an empty line list.

REV21 checks the native line count after a short delay **only when the plugin applied the active tag**. If the native list is empty, the plugin clears its automatic filter, restores the native list, and resynchronizes the toggle. It does not delete, rename, or edit any route; the text field is a filter, not a model tag editor.

## Still preserved
- **No automatic folder scanning at dialog startup** (REV20).
- Folder scanning begins **only after choosing a folder**; progress/Cancel and caching during the dialog remain.
- `Select All (N)`, first-level Plant 3D Drawings folder dropdown, custom path, responsive Revision/Custom Save Location fields, and automatic copying of completed Production ISO DWGs.
- No project/database or `IsoConfig.xml` edits.

## Recommended test
1. Fully exit/reopen Plant 3D 2025, then `NETLOAD` REV21 `SPTC.ProductionIso.dll`.
2. Run `SPTC_PRODISO` and verify there is no automatic scan.
3. Widen **Display lines** with the separator. Confirm that the **native funnel** and custom folder arrow move together to the right end of the filter row; the text entry area grows and remains legible.
4. Confirm that route names stay inside the **Line Numbers** checklist and `Select All (N)` matches.
5. Click Drawing or Project top icons to test the active/full toggle. If a long tag matches nothing, the list should automatically recover instead of remaining blank.
6. Select a folder to confirm its on-demand scan still works; then choose All Project.
7. Confirm Custom Save Location still stretches with the dialog and generate only a single known-good ISO test.

**Experimental:** compilation does not verify the proprietary Autodesk dialog layout at runtime. For an unrecognized native filter implementation, the plugin will skip that control rather than modify unrelated UI. Share a screenshot and F2 output if alignment or native line selection is not correct.

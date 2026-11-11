# SPTC Production Iso REV26 — Active DWG Checkbox Selection Fix

**Baseline:** REV25. Prior revision branches and DLL builds remain unchanged.

## Reported defect
- In **All Project** mode, checking a line-number entry works.
- In **Active DWG** mode (for example line tag `A-08`), the line name becomes blue-highlighted but its checkbox refuses to stay checked.
- Autodesk's original **Create** remains disabled even though one active line appears in the temporary list.

## Root cause and REV26 fix
REV25's temporary active/folder CheckedListBox forwards its `ItemCheck` event **synchronously** to the underlying Autodesk CheckedListBox. WinForms `ItemCheck` is a **pre-commit** event: the proxy's checkbox has not changed yet. Calling the native checklist from inside that event reenters WinForms/Autodesk selection handlers. If the nested interaction returns failure, the handler sets `e.NewValue = e.CurrentValue` and cancels the user's visible click.

REV26 now:
- Lets the temporary checkbox complete its own `ItemCheck` first, then posts a **single UI-thread `BeginInvoke`** to synchronize that exact native line item through the REV24 native mouse/selection relay.
- Verifies the original row's index **and exact tag**, and compares its final native checked state to the user-requested state.
- Uses a **projection generation guard**, so a queued action cannot alter the original list after the user switches from Active to All or closes the native dialog.
- When the user clicks repeatedly before a queued update runs, superseded events are skipped.
- If the original Autodesk list refuses the selection, the visible checkbox is returned to its **actual native state** and a `SPTC REV26:` diagnostic is logged (F2); Create is **never** forced enabled.
- `Select All (N)` inside Active/folder views uses the same deferred non-reentrant approach and mirrors native checked states back after the batch.
- The original Autodesk `Create` button remains in charge of enabling ISO generation based on its own validations.

## Preserved from REV25
- Immediate folder menu paint and asynchronous listing of direct children under Plant 3D Drawings; PnP scanning only after folder choice.
- Adaptive vertical draggable line-number divider, wide route-name list, responsive native filter-row icons.
- Exact current physical-DWG line-tag filter, Active/All toggle, visible count, Select All.
- Aligned, stretching Custom Save Location and automatic copying of completed ISO drawings.
- No changes to Project Manager, PnP records, model geometry, `IsoConfig.xml`, or existing Production ISO outputs.

## Test
1. Completely close AutoCAD Plant 3D 2025. Restart; `NETLOAD` REV26 `SPTC.ProductionIso.dll`.
2. Run `SPTC_PRODISO` in the known one-route drawing.
3. Click the top Active/All button once to show only `A-08`.
4. **Click the checkbox beside `A-08`** (not just the text). It should remain checked after the deferred native commit; `Create` should become enabled if Autodesk accepts the route.
5. Uncheck and recheck repeatedly, and try `Select All (1)`; verify both checkmarks and native Create eligibility update.
6. Switch Active → All → Active, confirming that the correct line list and checked states remain synchronized.
7. Generate **one** sample Production ISO only when Create is enabled. Verify the custom destination when supplied.

If the visible checkmark still clears or Create stays disabled, press F2 after closing the dialog and share any lines beginning `SPTC REV26:` or `SPTC REV24:`. This will distinguish native selection rejection from another Autodesk Create validation.

**Note:** GitHub compilation is not a Plant 3D runtime test. Custom native-dialog interception is experimental. Retain REV25 as the fallback until REV26 has been tested.

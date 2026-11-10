# SPTC Production Iso REV24 — Reliable native Create eligibility from checked line numbers

**Baseline:** REV23. All previous revision branches/files remain unchanged.

## Bug observed in Plant 3D 2025
In REV23, an exact active line sometimes appeared checked (including `Select All (1)`) while the original Autodesk **Create** button remained disabled. At other times Create became enabled. This was intermittent.

The active/folder projection is a temporary visual list placed over Autodesk's actual checklist. REV23 forwarded checks using `CheckedListBox.SetItemChecked`, which updates the native checkmark but does **not necessarily dispatch the same mouse/selection event sequence** that Autodesk uses to update the Create-button state.

## REV24 change
- For user-initiated line check/uncheck, REV24 validates the exact native row text and index and delivers checked-list mouse down/up messages **to the original Autodesk checked-list control**. Native ItemCheck and selection events can now run as they would for a real click, rather than relying solely on `SetItemChecked`.
- The resulting *native* checked state is read back and compared against the requested state. If the original control doesn't respond to the native click, the safe programmatic setter remains a fallback.
- After a short, reusable 220 ms UI debounce, the plugin checks whether the original Create button is still disabled while one or more genuine native lines are checked.
- A checked native row that already existed when the projection was shown is also revalidated.
- If Autodesk's Create button remains disabled, the plugin makes one controlled **uncheck/check** event replay on one exact selected native route. It keeps the final checked state and logs an F2 diagnostic if Create remains disabled.
- **No forced `Create.Enabled=true`**. Other Autodesk project/style validations may also disable Create. The original Autodesk button and production ISO creation mechanism are used.
- Select All updates remain efficient: native check-state changes are applied across visible entries, with a full original mouse-event sequence on the final item instead of a separate synthetic mouse dispatch for every line.
- All active/folder projection selection operations are verified against the native list. No project record, model file, route name or ISO configuration is modified.

## Preserved
- REV23 deterministic Active / All line-number toggle.
- Exact current drawing tags, rather than substring/prefix matching.
- REV20+ resizable Line Numbers panel and aligned filter row.
- REV17 Select All (N) and on-demand folder list (no automatic startup scan).
- REV19 Custom Save Location alignment/resizing.
- REV05 automatic copying of completed Production Iso DWGs to custom path.

## Test
1. Completely exit Plant 3D 2025; restart and `NETLOAD` the REV24 DLL.
2. Run `SPTC_PRODISO`. Select the exact active route with the top icon.
3. Check one line number in the visible list (or use Select All).
4. Verify **Create** becomes clickable while the original checked selection is present. Test at least 4 rounds of uncheck/check and Active / All toggling because the original problem was intermittent.
5. Confirm `Select All (N)` matches the visible list and Create returns disabled when no routes are checked.
6. Generate **one** known-good test Production ISO. Confirm the expected DWG appears in the custom location if it was specified.
7. If Create remains disabled despite a checked line, close the dialog, press **F2** and send the lines starting with `SPTC REV24:` plus a screenshot. REV23 remains available.

## Caveat
This is experimental interoperability with an Autodesk native WinForms dialog. GitHub compilation proves the C# builds, not that Autodesk's internal eligibility logic has been fully exercised. Only a Plant 3D runtime test confirms the intermittent Create-state fix.

# SPTC Production Iso REV23 — Stable Active / All Line Toggle

**Baseline:** REV22. All previous revision builds and branches remain untouched.

## Why REV22 failed
The old upper-left toolbar click was handled by **two competing mechanisms**: Autodesk's original Drawing/Project mode event and an SPTC delayed projection of the active line list. Autodesk could asynchronously reload or clear the underlying native line list after SPTC had overlaid exact active tags. This produced the reported symptoms:
- The first click showed the correct one active line; the next click showed **zero**.
- Old lines remained for an extended interval, reappeared, or never disappeared.
- Select All from active mode sometimes touched hidden project lines instead of only the visible active ones.

## REV23 change
- The three original upper-left line-list mode buttons become an **SPTC Active/All toggle while the plugin is loaded in Create Production Iso**. Their mouse click down/up messages are intercepted *once* within the dialog, without also triggering Autodesk's original list mode event. No Autodesk model, project or ISO data are edited. Other UI controls and native Production Iso Create remain untouched.
- **First click:** show only the current open DWG's exact line tags from live physical piping objects, using a temporary checked-list view whose check/uncheck actions are forwarded to the real native ISO line list. No substring search and no text inserted in Autodesk's Filter textbox.
- **Second click:** remove only the temporary list view. Autodesk's underlying full project list was never switched or emptied, so it should reappear immediately. Further clicks alternate.
- Duplicate managed WinForms Click handlers and delayed 100/300/900ms filter timers no longer run for the top-button toggle. The original PnP data/read-only model scan is deferred until the **first active-mode click** and cached for this dialog; no automatic startup scanning.
- Select All (N) now uses **HasVisibleProjection** for both folder and exact active-DWG modes, so it cannot check old hidden native lines while active-only is visible.

## Important behavior change
In REV23, while SPTC has enhanced the Production Iso dialog, clicking one of the three original upper-left icons performs **Active / All toggle** rather than Autodesk's original toolbar list-source action. This avoids the double reload race. If the native Drawing/Project source selection needs to be used exactly as Autodesk designed it, open the unmodified Production Iso dialog outside the SPTC command.

## Safety rules
- No deletion of ghost/stale line records. Model-verified line tags only, displayed as exact matches.
- If physical components in the open DWG genuinely have multiple distinct line tags, the plugin shows all of them, not an arbitrary single value.
- If none of the model tags is available in the currently loaded native line list, the active projection is **not** applied. The native list remains visible and a warning is written to the AutoCAD F2 command log.
- Folder list still shows first-level Plant 3D Drawings folders. Folder scans are user-initiated with progress/Cancel, not automatic on opening.
- REV05 completed ISO DWG copying to Custom Save Location, REV17 Select All (N), REV19 dynamic location row and REV21 resizing/filter-row behavior remain unchanged.

## Test steps
1. Exit Plant 3D 2025 completely and reopen it. NETLOAD **REV23** `SPTC.ProductionIso.dll`.
2. In your single-route drawing, check the actual Line Number Tag on a current pipe using Plant 3D Properties.
3. Run `SPTC_PRODISO`. The native full list should be visible at first, without a database precache.
4. Click one upper-left toolbar icon **once**: only the exact current-DWG line tag(s) should appear; Select All (N) should match.
5. Click the same icon again: the full native list should reappear, without becoming blank.
6. Repeat at least 4 clicks to check alternation. The filter textbox must remain blank.
7. In active-only mode, check Select All: only displayed active routes should be selected; verify hidden old project routes are not checked.
8. If the list is still empty or stale, close the dialog and collect F2 command log plus a screenshot. Keep REV22 available as fallback, and don't batch export before checking exact native selections.

**Status:** Experimental until Plant 3D 2025 runtime validation. GitHub compilation alone cannot prove Autodesk modal-dialog mouse message interception is available in this installation. No changes to `IsoConfig.xml`, Project Manager, PnP tables or original DWGs.

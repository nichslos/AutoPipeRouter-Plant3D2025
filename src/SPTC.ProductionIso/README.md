# SPTC Production Iso REV29 — Restore REV24 Top Buttons

**Baseline:** REV28. Earlier revision branches and builds remain unchanged.

## Requested REV29 behavior
- Restore the same **three top-left Drawing / Project / Selection toolbar buttons** behavior used in **REV24** inside the SPTC-enhanced Production Iso dialog: a click toggles between the **full native line list** and an **exact current-DWG physical line tag view**. A second click returns to the original full native list; further clicks alternate. This is the plugin’s REV24 behavior, **not** Autodesk's default per-button functions.
- Remove REV28's separately added **DWG** checkbox beside **Select All (N)**. The footer is back to the REV24 layout with Select All only.
- Reuse the tested REV24 message-filter implementation (`NativeIsoToolbarClickFilter.cs` was identical on REV24 and REV28) and restore the REV24 callback hookup in `NativeIsoDialogEnhancer`.
- Keep exact tag filtering: `A-08` is not the same as `A-008` or another old/ghost line-group name. Current-DWG physical component tag detection is still read-only.
- Keep the current REV26/REV28 native checkbox relay and folder selection code unchanged; this revision is **only the top buttons and supplemental DWG footer UI**.
- Preserve REV25 adaptive height of the line-pane divider, responsive native filter/funnel, user-triggered folder scanning, aligned Custom Save Location/Browse and automatic output DWG copy.
- Keep the more relaxed 1600ms Select All counter polling from REV28. Do not change startup performance/lag code in this revision.

## Important behavior and limitations
- **All Project can legitimately include old registered line-group names**; the REV24-style *active* view hides nonmatching entries but does not remove or repair ghost line records in the project database.
- Active mode uses a temporary checked-list projection in order to show exact current-DWG physical line tags. This was the same REV24 approach that sometimes failed to enable the original native **Create** button on some installations. **REV29 does not claim that the older Create-button limitation is fixed.** Confirm the genuine native checked state before generating an ISO, and use original All Project mode if the active-only proxy is not selectable.
- No modifications to PnP database records, project structure, model DWGs, `IsoConfig.xml`, ISO configuration, or native default output folder.

## Test
1. Restart Plant 3D 2025, NETLOAD REV29 `SPTC.ProductionIso.dll` and run `SPTC_PRODISO`.
2. Verify that only **Select All (N)** appears at the bottom-left, without REV28's extra **DWG** checkbox.
3. Click one of the three top-left buttons: the list should display exact live physical tags belonging to the currently open DWG.
4. Click the same button again: the original full project list should return. Repeat several times.
5. Try the third top button; within SPTC it should have the same REV24 toggle action, not the native selection-set filter that could be empty.
6. Before any ISO batch generation, verify you can genuinely check the route and that the original Create button is enabled. If not, stop and send F2 output or use the unmodified native Autodesk dialog for ISO generation.

**Build status:** GitHub Actions builds the DLL; installation-specific native dialog and checking behavior require Plant 3D runtime testing.

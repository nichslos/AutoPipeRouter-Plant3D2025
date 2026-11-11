# SPTC Production Iso REV27 — Native Active-Line Selection and Faster Dialog Detection

**Baseline:** REV26. Earlier branches and compiled DLLs remain unchanged.

## Runtime problems reported
- The user cannot check line `A-08` after switching to Active DWG even in REV26, while checking the original All Project lines works.
- Running `SPTC_PRODISO` takes approximately **5–8 seconds** before Create Production Iso appears.

## REV27: Remove the duplicate Active-DWG checkbox list
REV22–26 created a temporary **second CheckedListBox** covering Autodesk's original line list and attempted to forward the visual check to the underlying original. That forwarding does not reliably activate the native **Create** button. Merely adjusting the order of nested ItemCheck events in REV26 was insufficient.

REV27 changes strategy:
- **Active DWG mode does not create any temporary checklist.** It sets the exact verified physical component line tag into Autodesk's **original Line Numbers Filter textbox**. Autodesk's own native TextChanged filtering then determines which items remain displayed in the original **clickable** line-number checklist.
- **All Project mode clears that native filter textbox** and returns the original list. There are no proxy checkboxes or native click-message relays in Active mode.
- The filter textbox is found **inside the Display lines GroupBox**, independently of how wide the resizable left panel is. The earlier approach relied on a fixed percentage of the entire dialog and could fail after stretching.
- The existing REV23 single Active/All toolbar toggle is retained, so Autodesk's original toolbar click does not also reload the checklist.
- The original REV17 Select All works directly on the original native checked items in Active mode. Native Create remains governed by Autodesk eligibility; the plugin does not force it on.
- A lightweight check records whether the original native list contains only the expected exact tag. If filtering is slow or unusual, a warning is logged to F2, **but the plugin does not automatically clear the filter**. This prevents old line names from reappearing unexpectedly.
- **Tradeoff:** The verified current tag is visible in the original native Filter textbox while Active mode is selected. This is intentional: it avoids maintaining a second checkbox list and lets Autodesk own selection/ISO generation. If Autodesk's native filter matches additional lines rather than exactly one, the plugin logs a warning; review the visible list before Create.

## REV27: Faster startup detection and objective timing
- The dialog-watcher interval before first detection changes from **1500 ms to 150 ms**; after the dialog appears it switches to **1000 ms** to avoid unnecessary repeated window enumeration.
- The AutoCAD **F2** command log records
  `SPTC REV27: Native dialog detected after N ms; enhancements attached in M ms.`
- **Important:** Polling less slowly can remove the extra wait for SPTC UI injection, but cannot guarantee a 5–8 second reduction if the `PLANTPRODUCTIONISO` native Autodesk command itself needs project/network database loading. Compare by running `PLANTPRODUCTIONISO` directly without `SPTC_PRODISO`.

## Preserved from REV26
- User-triggered first-level `Plant 3D Drawings` folder dropdown, with its separate folder-specific projection when selected.
- Resizable Line Numbers divider and dynamic vertical grip, Select All (N) counter.
- Responsive Revision number / Custom Save Location row and background copy of completed DWGs to the selected custom directory.
- No edits to model DWGs, PnP/Project Manager data, ISO style or `IsoConfig.xml`.

## Runtime test
1. Fully exit/restart Plant 3D 2025, `NETLOAD` REV27 `SPTC.ProductionIso.dll`.
2. Run `SPTC_PRODISO`, noting how long the native dialog takes to appear.
3. Click a top toolbar button once to show the actual current DWG line tag; it should filter the original list (the tag may be displayed in the original Filter field).
4. Check **the original native checkbox next to A-08**. It should stay checked and enable native Create if Autodesk recognizes it as a valid route.
5. Click the same toolbar button again to clear the Filter field and show All Project.
6. Repeat several times and verify the original Create works for one sample ISO; verify the custom copied output if specified.
7. If selection or performance still fails, close the dialog and press **F2**. Provide the `SPTC REV27:` timing line, any active filter verification diagnostics, and the results of testing `PLANTPRODUCTIONISO` directly.

**Status:** Compiled on GitHub, then requires runtime validation. This revision deliberately favors native checked-list reliability over the previous visual-only active projection.

# SPTC Production Iso REV10 — Toggle Active / All line numbers

REV10 continues directly from REV09. Earlier releases, including working REV05 copy/export and REV06 Select All, are unchanged.

## Change relative to REV09
The original Production Iso dialog initially shows **the normal unfiltered list**. Clicking a top-left native **Drawing / Project** toolbar icon the first time shows the verified active route tag in the open DWG. Clicking again clears the temporary tag filter and restores Autodesk's unfiltered native line list. Repeated clicks alternate between filtered and unfiltered.

For the single-line example:
- Dialog opens: normal full list (native field blank).
- First top icon click: native filter `A-008`, only `A-008` displayed.
- Second top icon click: clears native filter; all available lines in Autodesk's selected Drawing/Project mode display again.
- Third top icon click: `A-008` again.

The `DWG Only` button still applies the active tag, and its state is synchronized with the toolbar toggle, so the next top click restores the unfiltered list. The Select All checkbox and the working automatic ISO copy behavior are unchanged.

## Testing
1. Restart Plant 3D 2025 completely to unload previously NETLOADed DLLs.
2. Load REV10 `SPTC.ProductionIso.dll` using `NETLOAD`.
3. Open the one-route DWG and run `SPTC_PRODISO`.
4. Confirm the line filter box starts blank with the normal lines visible.
5. Click the first or second native top-left line-list icon: filter should become `A-008`.
6. Click the top icon again: filter should become blank and the native line list should repopulate.
7. Click once more: only `A-008` should remain.
8. Test **Select All** only after confirming the right list is shown. Don't generate all project isos during a filter test.

## Technical notes and safety
- Uses Autodesk's own Line Numbers text filter, never deletes or alters pipeline records.
- "All" means unfiltered lines in the **currently selected native Drawing or Project scope**. Autodesk's existing scope buttons keep their behavior. For all project lines, select the Project mode.
- Supports one verified active tag from the model-space read-only PnP DataLinks scan. More than one active tag is not forced into a single-tag filter.
- Uses a one-shot 350 ms WinForms message filter for toolbar clicks; the original click passes to Autodesk unchanged.
- Compiles in cloud with GitHub Actions. In-Plant 3D runtime verification is still required because Autodesk's native dialog is not a supported customization API.
- No modifications to Project Manager, Project.xml, IsoPart.xml, IsoConfig.xml, actual DWGs, or model objects.

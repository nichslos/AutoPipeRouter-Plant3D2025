# SPTC Production Iso REV17 — Visible Line Count Beside Select All

**Baseline:** REV16. All previous revisions remain unchanged.

## New feature
- The bottom-left checkbox label shows **`Select All (N)`**, where `N` is the count of **line-number entries currently displayed**.
- When no folder is selected (native Drawing/Project view), the count comes from Autodesk's current line list.
- After selecting a Plant 3D Drawings folder and completing its scan, the count automatically switches to the number of line entries shown for that folder.
- Selecting **All Project** restores the original native list and its count.
- Native Drawing/Project toolbar clicks and typed native line filtering also refresh the displayed count via a lightweight **850 ms UI timer**. This reads existing list controls only: **no database queries or file enumeration**.
- The value is the total **visible** line entries, not just selected (checked) entries. For an empty folder the label is `Select All (0)` if its filtered projection is available.
- The count is presentation-only: the checkbox still selects/deselects using the same REV16 logic.

## Test procedure
1. Close Plant 3D completely, reopen, and `NETLOAD` REV17's `SPTC.ProductionIso.dll`.
2. Run `SPTC_PRODISO` and check the left footer **Select All (N)**.
3. Use the native Drawing/Project buttons to change displayed routes; verify the number changes to match.
4. Choose a folder from the dropdown in the native Filter row. After the folder scan completes, verify **Select All (N)** matches the folder's displayed routes.
5. Choose **All Project** and verify the full list count is restored.
6. Verify Select All still checks just the visible folder lines in folder mode.
7. Test ISO creation with **one** known-good route only, after confirming the count and selection.

## Preserved
REV05 automatic completed-DWG copying to Custom Save Location; REV06 Select All; REV09–11 native active/all toggle; REV12 field alignment; REV15 dropdown placement/top-level folder names; REV16 cancelable incremental folder scan.

## Notes
Count reading is restricted to existing native/UI list items, so it does not scan DWGs or query PnP metadata. If Autodesk uses a different line-list UI class on another installation and the count cannot be safely determined, the label remains `Select All` rather than showing an invented count.

No changes to Project Manager, model DWGs, PnP database, `IsoConfig.xml`, or existing generated ISOs. The build needs runtime testing in Plant 3D 2025.

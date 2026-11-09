# SPTC Production Iso REV09 — Filter after native button click

## Revision basis
REV09 is a new isolated branch based on REV08, which itself preserves REV05 automatic custom ISO copying and REV06 Select All. Older revisions are not changed.

## What changed from REV08
- **When Create Production Iso first opens, the line-number list is left entirely in Autodesk's original mode.** No tag is forced into the filter box.
- The current DWG's real P3d Line Group Tag(s) are still read, using the working REV08 read-only scan.
- After the user clicks one of the three small **Drawing / Project / Filter** icons at the top-left of the native dialog, a small one-shot delay lets Autodesk refresh the native list, then the plugin applies the verified active current-DWG tag to the native text filter.
- Clicking **DWG Only** below the list still applies the verified tag manually. **Select All** and background ISO copying are unchanged.
- For zero or multiple verified tags, this exact-one-tag filter is intentionally not applied; it will never guess which route should be visible.

## Example: one actual line in an open DWG
Before clicking any top icon, the native list might contain `A-08`, `A-008`, `A-088`.
After clicking Drawing or Project, only `A-008` should remain visible in the filtered list.
The native filter textbox should then contain `A-008`.

## Test instructions
1. Completely exit Plant 3D and reopen it; NETLOAD the REV09 DLL.
2. Open the single-pipeline drawing.
3. Enter `SPTC_DWGTAGS` in the command line; verify exactly one tag (`A-008` in the sample).
4. Run `SPTC_PRODISO` and verify the filter box initially stays blank and native tags are displayed.
5. Click the native **Drawing** icon at top left; verify the filter box becomes `A-008` and the native line list displays only A-008.
6. Click the native **Project** icon, then verify the same outcome after a moment.
7. Verify Select All and Custom Save Location are still available; test ISO export only after line selection is verified.

## Technical boundaries
- An experimental WinForms message filter / native managed toolbar Click observer is used to detect user clicks without consuming or replacing Autodesk's click messages.
- The filter is not a replacement for Autodesk's project line records. No database rows, tags, or model geometry are changed.
- Some Autodesk toolbar implementations may not relay the click through the WinForms message pump. If the icon click does not cause filtering, use DWG Only and send a screenshot. Do not use bulk generation until verified.
- Scan is read-only and bounded to protect responsiveness. Only one verified active tag is filtered at a time.
- Never modifies Project Manager, IsoConfig.xml, Project.xml, IsoPart.xml or native ISO output registration.

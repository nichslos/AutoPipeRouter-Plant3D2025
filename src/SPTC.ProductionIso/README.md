# SPTC Production Iso REV11 — Faster toggle, cleaner left panel

REV11 is based directly on REV10. Earlier REV05–REV10 branches and builds remain unchanged.

## Changes
- Removed the redundant **DWG Only** button from the lower-left corner. The **Select All** checkbox is preserved and now uses the full width of the footer.
- The two Autodesk Drawing/Project buttons still toggle between full native line list and the verified active current-DWG tag, per REV10.
- Reduced the plugin's debounce **when returning to the full list** from 350 ms to 100 ms. Applying the active route still waits 300 ms to allow Autodesk's mode transition.
- **Important limitation:** Autodesk's native Drawing/Project list may still take time to retrieve pipeline tags from a project stored on a mapped/shared drive. This revision only reduces the plugin-imposed delay, not Autodesk's own database refresh time.
- No edits to Plant 3D project databases, model files, Project Manager settings, IsoConfig.xml, Project.xml or IsoPart.xml.
- The REV05 background ISO copy/export and the Custom Save Location field are unchanged.

## Test
1. Exit Plant 3D 2025 completely, reopen, and NETLOAD this REV11 DLL.
2. Run SPTC_PRODISO in the one-route test drawing.
3. Verify the normal full line list is visible when the dialog first opens.
4. Click the Drawing or Project icon at top-left: the verified active line number should be shown.
5. Click again: native full list should reappear. The filter textbox should become blank.
6. Repeat and test how quickly the list returns. On large network projects, the native query still determines most of the latency.
7. Verify Select All works as before and that the Custom Save Location field is intact; generate a single test ISO only after checking selection.

This is an experimental dialog extension compiled via GitHub Actions. Compilation is not a substitute for in-Plant 3D runtime testing.

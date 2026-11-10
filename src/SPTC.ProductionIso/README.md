# SPTC Production Iso REV18 — Automatic Folder Cache on Command

**Starting point:** REV17 (preserves all earlier revisions without modifying their builds).

## Automatic behavior
- After `SPTC_PRODISO` opens Autodesk's native Create Production Iso window, the plugin automatically starts **one read-only warming pass** across the immediate child folders under `Plant 3D Drawings`.
- The warming pass caches *completed*, verified line-number results for each registered folder. Clicking a **cached folder** displays its line numbers immediately without a fresh database scan or progress panel.
- If a folder is clicked **before its cache is ready**, the warm-up yields priority to the selected folder. The existing progress/Cancel panel appears for that folder, and the remaining folder caches resume afterward.
- The cache is created only for the current Production Iso dialog session, and is discarded when that dialog closes. This avoids old line/tag records carrying into later sessions after model changes.
- Read-only Autodesk/PnP calls stay on the **Plant 3D UI thread**, with cooperative yielding between calls. A `SemaphoreSlim` serializes all project database scans so the background warm-up and a user-selected folder cannot interleave PnP operations.
- There is **no `Task.Run` for Plant 3D APIs**, no database or DWG editing, no extra executable installed and no project configuration changes.

## Responsiveness and limits
- The 120-DWG refusal remains removed (since REV16).
- The native dialog remains interactive between database requests, and cached folders open without waiting. Some individual slow database calls, particularly in network-backed projects, can still temporarily occupy the CAD UI thread. No safe code can guarantee zero lag when the Autodesk PnP API call itself blocks.
- At most 500,000 inspected relationship records per requested folder; corrupted/extremely large records return a diagnostic rather than hang indefinitely.
- Cache warming stops when the native dialog closes, and a partially completed or canceled folder scan is **never** applied to the original line selection.

## Preserved
- Folder dropdown remains on the **Line Numbers Filter** row, showing first-level `Plant 3D Drawings` folders only (REV15).
- `Select All (N)` displays the count of line numbers visible in current list/folder (REV17).
- Scanning progress and Cancel remain for uncached, user-selected folders (REV16).
- Current drawing / all-project top-button toggle, original native dialog, aligned Custom Save Location and REV05 automatic completed ISO copying are unchanged.

## Test procedure
1. Completely close Plant 3D 2025 to unload older .NET DLL, reopen and `NETLOAD` REV18 DLL.
2. Open the same DWG/project and run `SPTC_PRODISO`.
3. Without clicking the folder list, check the command line (F2): it should eventually report `SPTC REV18: Preloading line tags` and then `Folder cache ready`.
4. Once caching completes, choose the large folder that formerly showed `>120 DWGs`; its line numbers should appear without the scanning panel.
5. For a clean test with a cold cache, close and reopen the Production Iso dialog and select a folder while warming is still underway; priority scanning, progress and Cancel should remain available without applying partial results.
6. Verify `Select All (N)`, original Project/Drawing buttons and `All Project` restoration.
7. Create **one** known-good test ISO only after verifying the actual native selected line-number checkboxes and custom save path.

**Experimental:** GitHub build success confirms compilation, not runtime responsiveness or PnP metadata validity in Plant 3D 2025. If an individual network database call blocks, the UI may still pause. Send F2 diagnostic output and a screenshot if that occurs.

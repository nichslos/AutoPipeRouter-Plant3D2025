# SPTC Production Iso REV06 (custom result hyperlink experiment)

## Changes relative to working REV05

**Automatic ISO copying is preserved unchanged.** `SPTC_PRODISO` still opens Autodesk's original Production Iso dialog, inserts Custom Save Location below Revision Number, and begins a background file copy once the native dialog closes. There is no network file enumeration on the UI thread.

After copying successfully, REV06 attempts to attach a small **SPTC Custom ISO** hyperlink *inside* Autodesk's **Isometric Creation Results** window. It displays the **actual exported DWG path**, not a guessed path. Clicking opens the copied DWG using its Windows file association. When more than one DWG was copied, the link opens the destination folder instead.

The original Autodesk `File: L:\...\ProdIsos\Drawings\...` hyperlink and Project Manager registration are **not altered**. The native results path will still be the default project location because that's where Plant 3D actually generates its drawing.

The Results dialog may be opened by clicking the `Isometric Creation Complete` notification. **Opening it is optional** for copying; the extra hyperlink appears only if you open the results window after at least one DWG has been copied.

## Testing REV06
1. Completely exit Plant 3D 2025 so an older NETLOADed DLL cannot remain in memory.
2. Reopen Plant 3D and NETLOAD the REV06 DLL.
3. Enter `SPTC_PRODISO`, choose your custom folder and generate one ISO.
4. Verify the custom output DWG has been copied.
5. Click the **Isometric Creation Complete** notification, or use Autodesk's Results window, and check for **Custom ISO (SPTC Export)** plus the clickable custom-path hyperlink.
6. If the link does not appear, run `SPTC_ISOSTATUS` and send the output/screenshot. The underlying copy continues to work independently.

## Known limitations and safety
- This injects a managed child control into an Autodesk dialog using Win32 `SetParent`, which is not a documented Autodesk customization point. Visual placement depends on the actual Results dialog control hierarchy and Windows scaling. It may need adjustments based on a screenshot.
- The new results hyperlink appears only after the background copy task is marked complete. For multi-file outputs, the worker allows a short quiet period after the last copy.
- The Results window can be opened at any time within ten minutes of copy completion while the monitor remains alive.
- Neither XML configuration nor shared project database nor original DWGs are modified.
- Do not move the original project-registered ISO DWGs to the custom folder. Custom output is a copy.
- Test on one ISO first; if lag or instability occurs, restart Plant 3D and return to REV05.

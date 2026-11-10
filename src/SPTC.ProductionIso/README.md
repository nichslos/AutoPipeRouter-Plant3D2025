# SPTC Production Iso REV19 — Responsive Custom Save Location + Label Alignment

**Baseline:** REV18. Existing releases remain unchanged.

## What changed
- The **Custom Save Location:** label is left-aligned with the Autodesk-native **Revision number:** label; both use consistent label font and baseline.
- The Custom Save Location textbox starts at the same horizontal coordinate as the native Revision number textbox.
- The textbox **grows and shrinks with the original Create Production Iso dialog**, and the **Browse (...) button moves with its right edge**. Its right edge aligns with the right edge of Autodesk's Revision textbox.
- Prefer actual native Revision label and textbox coordinates (Win32 read-only geometry). A stable REV12-style fallback remains available when Autodesk doesn't expose native child windows.
- A lightweight 180 ms UI timer checks only whether the dialog's client **size changed**. It recalculates geometry only if necessary; no PnP/SQL/file enumeration is done by the timer.
- Explicitly resizes the custom WinForms controls instead of relying on Autodesk/Win32 `SetParent` to relay a `Resize` event.
- Label position and text remain aligned even when the dialog is stretched vertically, as well as horizontally.

## Preserved
- REV05 custom destination automatic copying of completed ISOs.
- REV06 Select All; REV17 **Select All (N)** counter.
- REV09–11 Drawing / Project active-versus-all toggle.
- REV15 top-level Project Manager folder dropdown at the Line Numbers filter spot.
- REV16 cooperative folder scan with Cancel.
- REV18 automatic folder cache prewarming after the native command.

## Test
1. Completely close AutoCAD Plant 3D 2025; restart and NETLOAD the REV19 DLL.
2. Run `SPTC_PRODISO`, then stretch the **right edge** of Create Production Iso.
3. Confirm that the Custom Save Location box expands to the right, leaving the Browse button aligned to the right end of Revision number's textbox.
4. Confirm both labels are aligned at their left edge and both textboxes start at the same X coordinate.
5. Stretch the dialog smaller and taller; the row should maintain alignment and not overlap the native controls.
6. Check that Browse still selects a folder and test creating one known-good ISO before batch creation.

## Limits
- This is an experimental overlay on an Autodesk-native dialog. Windows display scaling or a different Plant 3D build may use different native control classes; the fallback keeps the prior layout when necessary.
- Compilation is not a substitute for in-Plant 3D runtime testing.
- No modifications to project data, Autodesk ISO XML, model DWGs, or default ISO destination.

# SPTC Production Iso REV33 — Faster Custom Save Location Copy

**Approved baseline:** REV32 (preserved unchanged). REV33 changes only `NativeIsoDialogEnhancer.cs`, specifically its background completed-ISO DWG copier and per-file F2 progress reporting.

## Problem
The user reports that ISO generation completes, but the completed `.dwg` appears in **Custom Save Location** only several seconds later.

In REV32 the copy worker scans every **2.5 seconds** and waits at least **3 seconds** of unchanged source size/timestamp before attempting a copy. This imposes several seconds of delay even when Autodesk has finished and released the file.

## REV33 behavior
- Start copying on the existing trigger: when Autodesk's native Production Iso dialog closes and the background watcher is started. Do not depend on manually opening or closing the Isometric Creation Results window.
- Use a **FileSystemWatcher** on the native `ProdIsos\Drawings` source directory to wake the worker when a `.dwg` is created, changed, or renamed.
- Keep a **650 ms fallback polling** interval. This is critical for network/shared drives (mapped `L:` paths), where filesystem notifications may be unavailable, delayed, or missed.
- Reduce the observed stable-size/LastWriteTime window from 3 seconds to **650 ms**, but require an **exclusive read FileStream lock over the ENTIRE transfer**. If Autodesk is still writing/holding the file, copying waits and retries. A zero-byte or changing file is not copied.
- Write into a `.sptc-part` file in the destination directory, flush it, then rename it to `.dwg`. The user only sees the finished destination DWG, never a partial one.
- Preserve the REV32 collision-safe filename strategy: existing files are not overwritten; use `_copy1`, `_copy2`, etc.
- Preserve the REV32 multi-DWG batch window (up to ten minutes, ending after 25 seconds of inactivity following the last successful copy).
- Queue a success message for **each completed output** and print on Plant 3D's UI thread:
  `SPTC REV33: Custom ISO ready: Z:\...\sample.dwg`
- All filesystem watching/reading/writing runs in the existing background worker. No PnP database scans, ISO configuration changes, Project Manager operations, or new WinForms window overlays.

## Expected improvement
After Autodesk finishes writing a source DWG and releases its file lock, the destination copy should typically start within roughly a second, instead of waiting for REV32's 2.5-second polling plus 3-second stability delay. Actual latency still depends on source and destination drive/network throughput, Autodesk file sharing, and file size. **No promise of zero delay**.

## Test
1. Close/restart Plant 3D 2025. NETLOAD the REV33 `SPTC.ProductionIso.dll` and run `SPTC_PRODISO`.
2. Select one valid current line number; choose a **custom folder outside the default native ISO output folder**, preferably an empty folder.
3. Click native **Create**. Do not open the Isometric Creation Results dialog manually.
4. When the ISO completion notification appears, inspect the custom location in Explorer. The copied `.dwg` should appear promptly if the source DWG is stable and released.
5. Press **F2** and look for `SPTC REV33: Custom ISO ready: ...`; optionally run `SPTC_ISOSTATUS` for source/destination state.
6. Open the copied drawing and compare it with the original output. The original project DWG is retained.
7. For a batch test with more than one ISO, confirm every expected output is copied (the worker stays alive for the batch quiet window).

## Safety and limitations
- Custom Save Location empty: native default location is used; no copy is attempted.
- Output target must be outside the native source tree; unchanged validation.
- Existing target files remain untouched.
- Copy is dependent on Autodesk writing a complete, unlocked DWG. A locked source, unavailable share, or slow network can still cause retries and additional delay. Failed attempt counters may increment during retries, as in REV32.
- **REV32 remains the approved fallback.** Compilation verifies build compatibility, not observed timing on the user's network.

# SPTC Production Iso REV12 — Align Custom Save Location row

Based directly on REV11. This revision makes one **layout-only** adjustment:
the Custom Save Location label and entry field are shifted about 28 pixels left,
to align their left edges with the native **Revision number** label and entry.

The Browse button remains at the same right edge. The original Create Production
Iso window is still used. Select All, Drawing/Project active/full route toggle,
and automatic export to a custom folder are unchanged.

## Test
1. Completely close AutoCAD Plant 3D 2025 to unload the prior DLL.
2. Reopen Plant 3D, run NETLOAD, choose this REV12 SPTC.ProductionIso.dll.
3. Run SPTC_PRODISO. Check the horizontal alignment of the two rows.
4. Verify Browse works, then optionally test one ISO export.

No changes to Project Manager, ISO project XML, PnP records, model geometry, or
existing DWGs. Native-dialog injection is experimental; source builds alone
cannot verify exact alignment at the user's Windows display scaling.

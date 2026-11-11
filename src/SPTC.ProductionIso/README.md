# SPTC Production Iso REV31 — Exact Native Active Line Matching

**Baseline:** REV30. Earlier source branches and DLLs are unchanged.

## Reported REV30 issue
The Active DWG filter now uses Autodesk's **real native selectable checkbox list**, so the user can select the line and the Create button is no longer blocked by the duplicate-list proxy. However, typing the correct current physical route `A-08` into the native filter still returns multiple substring matches:

- `A-08` **wanted**
- `A-088` **not wanted**
- `CAP_BF_BND_TEE_RTEE+A-088` **not wanted**
- `CAP_BLIND_BEND+A-088` **not wanted**
- `CAP_BLIND+A-088` **not wanted**

The Autodesk filter is **not an exact-match filter**. REV31 adds exact-match handling without reintroducing the unselectable overlay.

## REV31 implementation
- Keep REV24/REV29-style three-top-button Active/All toggle.
- On Active click: detect exactly one real physical current-DWG Line Number Tag, apply REV30's native Filter text, and then perform **reversible exact-name narrowing** of the existing, original Autodesk checked-list item collection. This removes the four substring-only UI entries from the original control itself while preserving `A-08` as its **original object** in Autodesk's list.
- On the next button click: restore the removed **original item objects** to their original positions (and retain their original check states) **before** clearing the native Filter field. This does not change SQL/PnP, drawing files, line tags, project metadata or ISO configuration.
- No duplicate CheckedListBox is displayed. The visible checkbox remains **Autodesk's actual native checkbox**, with Autodesk's own Create eligibility logic. It is never force-enabled.
- Use a small UI-only timer while Active mode is selected. Autodesk may re-populate substring matches asynchronously; the timer checks only the existing WinForms list contents and prunes any reappearing nonexact items. It does **not** query the project database or scan DWGs. Stop the timer on toggling All or closing the native dialog.
- Restore original item objects only when the remaining native survivors still have the expected object identity; if Autodesk has replaced its list asynchronously, do not inject stale objects.
- For a data-bound, virtual, or otherwise unrecognized native list, the plugin will **cancel exact filtering, clear the native Filter text and log `SPTC REV31:` to F2** rather than risk UI corruption or show extra lines as if they were exact.

## Unchanged
- Three upper toolbar buttons retain REV24-style Active/All behavior.
- Select All (N), resizable Display lines panel, folder filtering, aligned Custom Save Location, and automatic copy of finished ISO DWGs to the custom path.
- No startup-lag optimizations in this revision.
- No project/Plant database, model geometry, line-number property, or `IsoConfig.xml` modifications.

## Test
1. Completely exit Plant 3D 2025; reopen and `NETLOAD` REV31 `SPTC.ProductionIso.dll`.
2. Run `SPTC_PRODISO`. Click one of the three top-left buttons.
3. The native Filter field should contain `A-08`, while the actual **Line Numbers** checklist must display **only `A-08`**, not `A-088` or longer names ending with `A-088`.
4. Click the real `A-08` checkbox and confirm it remains checked and the native Create button becomes enabled if Autodesk considers the route eligible.
5. Click the top icon again to return to All. Verify that the original list is restored and checked items behave normally; repeat Active → All several times.
6. Test `Select All (1)` while Active. Confirm hidden nonmatching entries are not accidentally selected for ISO creation.
7. Generate **one** known-good ISO only after confirming selection/Create works. Verify custom copied DWG in the designated folder if a custom path is set.

**Caveat:** This revision removes and reinserts original dialog-control items **in memory only**. That may depend on Plant 3D's undocumented control implementation and should be tested on a copy/sample project first. A successful cloud C# build does not prove native dialog runtime behavior. If filtering fails, F2 output beginning `SPTC REV31:` explains the safe fallback. REV30 remains available.

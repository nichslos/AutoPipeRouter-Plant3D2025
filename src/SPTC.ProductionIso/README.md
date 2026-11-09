# SPTC Production Iso REV15 — Filter-row folder selector and first-level folders only

**Source baseline:** REV14; prior revisions unchanged.

## Fixes requested
- Put the folder-dropdown arrow back on the **Line Numbers Filter row**, close to the right end of the filter text field, **immediately left of** Autodesk's own filter funnel. The funnel remains operable. The separate **Folders** button at the bottom has been removed; the **Select All** footer is restored.
- Keep REV14's safe **child panel inside the native Create Production Iso dialog**, rather than using REV13's focus-stealing ContextMenuStrip popup.
- In the dropdown, show **only immediate child folders under Plant 3D Drawings**, not nested subfolders. Child DWGs still count toward their top-level parent, but the submenu never lists the nested folder individually.
- Display folder names only, without appended DWG counts or paths.
- **All Project (no folder filter)** restores the native unfiltered line list.

For example, the dropdown should show root-level names such as:
`01_INSTRUCTION_No.1 & 2`, `02_INSTRUCTION_No.3`, `03_HOLD`, `04_WI2-05_WI-06_WI2-07.20260928`, `05_SPTC TESTING MODEL`, `ADD_COMPONENT`, `HARVEY MODEL`, `PCF_TEST_DATA`, `PLOT PLAN`, `temp`, provided those directories contain a registered project DWG. It should NOT list `02_INSTRUCTION_No.3 > SOURCE...` or other descendant folders.

## Test
1. Close and restart Plant 3D 2025; `NETLOAD` REV15's `SPTC.ProductionIso.dll`.
2. Run `SPTC_PRODISO`. Verify the original funnel remains visible and a small **▾** arrow is immediately to its left on the same Filter row.
3. Click the new ▾ arrow. The Production Iso dialog must **stay open**, with a child list under the filter row.
4. Inspect the folder list. Only first-level folder names should appear, with an initial **All Project** option.
5. Choose a known folder and verify its line numbers. If mapping fails or the native list is incompatible, do **not** generate an ISO; send `SPTC_FOLDERDIAG` and F2 output.
6. Choose All Project and verify the native line list returns.

## Preserved
REV05 automatic copying to Custom Save Location, REV06 Select All, REV09–11 Active/All toggle, REV12 Custom Save Location alignment, and REV14 embedded list safety.

## Limitations
- **Runtime verification required:** GitHub compilation is not a test of Autodesk's native dialog or folder database in the user's project.
- The folder catalog reads only project-registered Piping DWGs; folders with no registered DWG cannot be inferred from the drawing records and may not appear.
- The folder records are obtained read-only from Piping project data. Model, PnP data, Project Manager, and project XML configurations remain unchanged.
- Test on one known route before batch ISO generation.

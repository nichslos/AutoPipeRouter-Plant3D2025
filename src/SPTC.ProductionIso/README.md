# SPTC Production Iso REV07 — Active DWG Routes (test)

**Base:** Working REV05 automatic copy + REV06 Select All checkbox. REV07 is a new isolated test branch; the original working revisions are retained unchanged.

## New behavior
- When `SPTC_PRODISO` starts, it scans **only model space of the currently active DWG** for live Plant 3D pipe/fitting/valve object types. The scan is read-only and does not query stale project-wide line-group records.
- It creates an AutoCAD PickFirst selection of those live model objects and attempts to invoke Autodesk's **native third Filter icon** inside Create Production Iso. This displays line numbers for the selected route components rather than all project records if Autodesk's command retains the selection.
- Below **Line Numbers**, the left footer now shows **Select All** and **DWG Only** side by side. Clicking DWG Only tries to reapply the native Filter mode after using Autodesk's Drawing or Project icons.
- The native **Drawing** icon still means current drawing (Autodesk's original metadata-based list), and **Project** still means all project line numbers. REV07 does **not** rename or override Autodesk's buttons or delete project line records.
- If the native Filter icon cannot be invoked automatically, manually click the **third Filter icon** at the top of the line-number list. The console reports whether active DWG selection was prepared.

## Test procedure
1. Restart Plant 3D completely to unload any older DLL.
2. NETLOAD the REV07 `SPTC.ProductionIso.dll`.
3. Open a DWG that physically contains one pipeline and run `SPTC_PRODISO`.
4. Observe whether only one live route is listed. If not, click **DWG Only** or the third native **Filter** icon.
5. Compare the result against **Drawing** and **Project** modes, which retain Autodesk's normal behavior.
6. Verify Select All is preserved, but **do not create all isos** in a large project when testing.
7. Run `SPTC_ISOSTATUS` to inspect existing automatic copy status, if needed.

## Safety and limitations
- No changes to `IsoConfig.xml`, `Project.xml`, `IsoPart.xml`, Project Manager, PnP database, model entities, or default output paths.
- REV05 background DWG copying is unchanged.
- The current-DWG selection uses runtime Plant 3D object type recognition. Some custom object types may not be recognized until tested.
- AutoCAD PickFirst selection may be cleared when Autodesk opens Production Iso; this extension is **experimental** and requires runtime verification before declaring filtering functional.
- It only changes display scope using Autodesk's native Filter behavior, not the underlying Plant 3D data. The Project button intentionally retains project-wide semantics.
- If the new behavior is wrong, close Plant 3D and reload the working REV06 or REV05 DLL instead.

# REV22.22 — FIX: Structural Member Size and TOS were concatenated in actual AutoCAD MText

**New command:** `SPTC_ORTHO22_22_MTEXT_SEPARATOR_TEST` in AutoCAD Plant 3D 2025. Test only in a COPIED registered project. Previous REV22.21 and earlier versions remain untouched.

**Observed Plant 3D screenshot:** `[55D] Sloping channel 100x50 web-5 fl-7.5TOS ~ +1117.5745 mm`. The 22.21 annotation planner correctly produced one newline between Size and TOS, but the native MText writer incorrectly called `SupportOrthoMemberMetadata.SafeMText(item.Text).Replace("\\n","\\\\P")`. `SafeMText` calls `Clean`, which removes control characters including `\\n` BEFORE `Replace` could convert them to AutoCAD's paragraph command. This is why `fl-7.5TOS` was joined with no gap despite the earlier tests succeeding.

**REV22.22 actual fix:** New `SupportOrthoMemberMText22.Encode` splits `item.Text` by source line breaks **before** individual per-line sanitization, then joins cleaned paragraphs with native AutoCAD MText `\\P`. The new generator uses `Contents=SupportOrthoMemberMText22.Encode(item.Text)`. The user-visible label renders as two real lines:

```text
[55D] Sloping channel 100x50 web-5 fl-7.5
TOS ~ +1117.5745 mm
```

This is a genuine AutoCAD paragraph break, not spaces artificially inserted on the same line. There is no extra blank line. Exact Top of Steel still uses `TOS EL. +... mm`; approximate geometry-derived Top of Steel uses `TOS ~ +... mm` with spaces. **TOS remains HORIZONTAL-only**; vertical/inclined steel member tags are a single line and have no TOS. Actual PnP structural `Size`, elevation values, source member handles, ten native views/titles, SupportFocus, camera fixes, 1-click placement, and paper-space leader geometry are all unchanged.

**Regression tests:** Verify the exact `\\P` native MText string for both LF and CRLF, no `fl-7.5TOS` concatenation, exact size and elevation values, unique one-line vertical tags, 130-label original paper layout, and the preceding REV22.21/19 tests. No change to the shared `SafeMText` helper, earlier commands or unrelated code.

**Test:** Fully exit Plant 3D 2025, NETLOAD the new DLL, open a COPIED registered source TEST5 project, run `SPTC_ORTHO22_22_MTEXT_SEPARATOR_TEST`, choose SupportFocus, select 21 source structural objects (13 Plant 3D Structure Members), optionally 3 nearby pipes, choose an EMPTY registered native Ortho seed, confirm `REV22.22 TEST`, click once. Inspect FRONT and NW labels. The log contains `REV22_22_MTEXT_TWO_LINES=...` and the 10 native views/130 member annotation counts. Send `*_REV22_22_RESULT.txt` and screenshot for runtime visual verification.

**Safety:** The custom paper-space leader/MText annotations are not native associative `PLANTORTHOANNOTATE`. The generator still invokes an undocumented native Ortho API and is experimental; copied projects only. REV22.21 remains the rollback.

---

# REV22.21 — Screenshot-matching Structural Member TOS Label (two lines, no blank row)

**New isolated command:** `SPTC_ORTHO22_21_TOS_DIRECT_LINE_TEST` in AutoCAD Plant 3D 2025, copied registered test project only. The REV22.20 command remains unchanged and available.

The user's latest screenshot shows **the structural member name and the approximate TOS immediately beneath it**, with ONE line break, not a blank line. The tilde retains spaces:

```text
[557] Sloping channel 100x50 web-5 fl-7.5
TOS ~ +502.7872 mm
```

The short stroke preceding the handle in the screenshot is the leader entering the MText label, not a newly prefixed hyphen in the underlying source member identity. REV22.21 changes only the REV22.20 label planner, from `label+"\\n\\n"+tos` to `label+"\\n"+tos`, and returns the two-line reserved bounding height. The display stays `TOS ~ +... mm` for geometric approximate elevation, while exact sourced elevation remains `TOS EL. +... mm`. Both use one line break after the steel Size. All source `Size` and TOS numerical data, member handles, eligibility (TOS on **horizontal** steel only), 10 native Ortho views, camera orientations, clipping, view titles, compact SupportFocus, one-click native layout, and per-member leader geometry are unchanged.

**Test:** Fully close/restart Plant 3D 2025, NETLOAD the new DLL. On a copied project, run `SPTC_ORTHO22_21_TOS_DIRECT_LINE_TEST`, choose `SupportFocus`, select TEST5's same 21 support objects with 13 native structure members, optionally three nearby pipes, choose an EMPTY registered Ortho seed, confirm with `REV22.21 TEST` then click once. Verify all native Ortho views are present and that member handle 557 prints as in screenshot with TOS immediately below. Check `STEP10_INDIVIDUAL_MEMBER_ANNOTATION_COUNT` (130 target), `STEP10_HORIZONTAL_TOS_LABEL_COUNT` and `STEP10_MEMBER_ANNOTATIONS_CHECK`; send `*_REV22_21_RESULT.txt` and close-up screenshot. Do not test this unsupported internal Autodesk native Ortho API in production drawings.

---

# REV22.20 — Format horizontal-member TOS annotation with a blank line and a spaced tilde

**New isolated Plant 3D 2025 command:** `SPTC_ORTHO22_20_TOS_SPACING_TEST`. The working REV22.19 command `SPTC_ORTHO22_19_HORIZONTAL_TOS_TEST` and all earlier versions remain unchanged.

**Only revision requested:** On **horizontal structural members**, add one blank line after the member's real PnP structural size and before its TOS elevation. Display approximate elevation as `TOS ~ +1117.5745 mm` (space on either side of the tilde) instead of `TOS~ +1117.5745 mm`. Do not change the source model, native Size value, TOS value/units, horizontal eligibility, exact/approximate semantics, projected member leader anchor, ten view titles/cameras, SupportFocus cube, native view definitions, or other components.

**Example with a real selected TEST5 member handle:**

```text
[539] Sloping channel 100x50 web-5 fl-7.5

TOS ~ +1117.5745 mm
```

**Exact-data case:** `TOS EL. +... mm` stays unchanged except for the added blank line above it. Vertical and non-horizontal members still receive their member-size annotation and leader with **NO TOS**. `SupportOrthoMemberCalloutPlan20` is a separate copy of REV22.19's planner with only three presentation adjustments: `"TOS~ "` → `"TOS ~ "`; `label+"\\n"+tos` → `label+"\\n\\n"+tos`; and reserved label height for a horizontal TOS caption is increased from two text rows to three. The paper-space MText writer already converts each line break to a native MText paragraph break (`\\P`), so two breaks leave a visible empty paragraph. Internal approximate-TOS detection uses the new spaced string.

**Smoke tests:** verify 130 per-member labels still fit on ten native views with example input, horizontal-only TOS rule, 40 approximate-TOS labels, exact elevation retained, blank line and spaced tilde, and title-block collision-safe boxes. No modifications to REV22.19's source reader or previous generator.

**Testing:** Fully restart AutoCAD Plant 3D 2025, NETLOAD the new DLL, run `SPTC_ORTHO22_20_TOS_SPACING_TEST` in a COPIED project, choose SupportFocus, select structural members (the previous test had 21 support objects, 13 native Structure Members), optionally select 3 nearby pipes, choose an EMPTY registered native Ortho seed, authorize `REV22.20 TEST`, and click once. Verify the new label spacing in FRONT and NW ISOMETRIC, `STEP10_INDIVIDUAL_MEMBER_ANNOTATION_COUNT` (target 130 for TEST5), `STEP10_HORIZONTAL_TOS_LABEL_COUNT`, `STEP10_MEMBER_ANNOTATIONS_CHECK`, and all ten native view titles. Send the `*_REV22_20_RESULT.txt` and screenshot.

**Caution:** This is an experimental Autodesk-internal native Ortho generator with custom paper-space, nonassociative MText and leaders. The blank line changes space consumption; if a sheet cannot fit every annotation, the command logs PARTIAL rather than overlapping. Source geometric Z values are still marked approximate (`TOS ~`) and must be verified with the model's actual Top of Steel properties. REV22.19 remains the fallback.

---

# REV 22.19 — CORRECTION: TOS elevation for HORIZONTAL structural steel ONLY

**New isolated command:** `SPTC_ORTHO22_19_HORIZONTAL_TOS_TEST` in AutoCAD Plant 3D 2025. Keep REV22.18 and REV22.17 commands unchanged as fallbacks. Test only in a COPIED registered Plant 3D project; authorize with `REV22.19 TEST`.

**User correction:** The previous REV22.18 added Top of Steel (TOS) beneath VERTICAL steel. That is not the intended requirement. REV22.19 **removes TOS from ALL vertical structural member annotations**; a second line `TOS EL. +... mm` appears ONLY beneath the Size/handle annotation of confirmed HORIZONTAL structural members. Members whose orientation is clearly inclined are excluded as well. All members, vertical/horizontal/inclined, continue to get their own separate true PnP `Structure Member.Size` labels and projected leader lines across the ten native views.

**Authoritative horizontal test:** Use native StartPoint/EndPoint or exposed Start X/Y/Z, End X/Y/Z to determine member direction: horizontal span at least 150 mm and absolute Z component at most 15% of axis length. If native start/end endpoints are absent, a strongly horizontal bounding extent (XY span at least 150 mm and more than 1.8× Z extent) is used as an explicitly logged fallback, never the rule `not vertical = horizontal`. This means a vertical column never receives an elevation merely because it has a reported TopOfSteel field. User should inspect `HORIZONTAL` and `DIRECTION_SOURCE` entries for representative members in the new result TXT.

**TOS source:** As before, read the genuine `TopOfSteel` value exposed through Plant 3D's structural member or the Piping `DataLinksManager.GetAllProperties`. Exact project values appear as `TOS EL. +502.7872 mm`; if not retrievable, display upper geometric Z for a **confirmed horizontal member only** as visibly approximate `TOS~ +502.7872 mm`. No TOS for vertical/clearly inclined steel. Unit assumption mm (TEST5 project units). Top of Steel is an elevation, not a tag-length or projected paper coordinate.

**Presentation:** Retain REV22.18's improved orthogonal per-member leader lanes, increased label spacing, and one native editable paper-space MText per structure member per view, with paragraph break before horizontal-only TOS. Preserve ten native Ortho views, REV22.13 titles, automatic SupportFocus/compact cube, pipe clipping, saved native camera presets, scale and one-click placement. Do not create a combined BOM legend.

**Run in copied project:** Exit Plant 3D 2025, NETLOAD new DLL, select the TEST5 source with same 21 support objects (13 Plant native Structure Members), optionally 3 nearby pipes, choose empty registered native Ortho seed, type `REV22.19 TEST`, click once. Send `*_REV22_19_RESULT.txt` and closeups of FRONT/NW. Check `REV22_19_HORIZONTAL_SOURCE_MEMBER_COUNT`, `REV22_19_MEMBER_TOS`, `STEP10_HORIZONTAL_TOS_LABEL_COUNT`, `STEP10_HORIZONTAL_TOS_EXPECTED`, `STEP10_MEMBER_ANNOTATIONS_CHECK`. Target 130 member labels if safely placed; horizontal-only TOS line count = 10 × eligible member count, if all labels fit. Check actual TOS numbers against Properties. Project-data property retrieval and leader appearance require live runtime verification.

**Limitations:** Member labels and leaders are custom, nonassociative paper-space entities, not native associative `PLANTORTHOANNOTATE`; source updates may not reposition them. Geometric fallback must not be confused with verified Plant 3D Top of Steel. The internal native Ortho generation call is not part of Autodesk's public supported API: test only in copied projects.

---

# REV 22.18 — Improved individual structural-member leader routing and vertical TOS elevations

**New isolated test command:** `SPTC_ORTHO22_18_VERTICAL_TOS_TEST` on AutoCAD Plant 3D **2025**. Use a copied, registered project and type `REV22.18 TEST`. The prior REV22.17 command is unchanged and remains available as the verified 130-leader baseline.

**User request:** Refine REV22.17's long overlapping leader lines and annotate *each* individual structural member; additionally, for members oriented VERTICALLY, insert **TOS (Top of Steel) elevation on a separate line BELOW that specific member's original steel-size tag**. Horizontal or sloping-horizontal members retain their existing Size line without TOS. This is not a single summary box.

**Actual source facts:** TEST5 REV22.17 has 21 selected support objects, 13 true Plant 3D `Structure Member` entities, 13 matching project database `Size` values. Examples: `Angle 75 thck 9`, `Round 80`, and `Sloping channel 100x50 web-5 fl-7.5`. REV22.18 uses exactly these per-entity PnP `Size` values, selected native ObjectId/handle, and each member's WCS 3D extents midpoint. It does not estimate steel profile sizes from bounds.

**Vertical detection:** Prefer the actual native member's `StartPoint` / `EndPoint` or reflected start/end XYZ Property palette values. Require a near-vertical member axis (vertical component at least 98% and length at least 150 source-mm). If the Plant API does not expose the endpoints, fall back conservatively to strongly vertical bounding extents, where Z exceeds 1.8× both X/Y and exceeds 150 source-mm; the result log distinctly flags `STRONG_VERTICAL_BOUNDS`. All nonvertical members receive NO TOS line.

**Top of Steel source:** Read `TopOfSteel` / `Top of Steel` from the actual native entity's public/PropertyDescriptor properties or the selected entity's `Piping.DataLinksManager.FindAcPpRowId` and `GetAllProperties(rowId,true)` Project Data records. If this is found, the two-line annotation shows e.g.:

`[521] Sloping channel 100x50 web-5 fl-7.5`
`TOS EL. +502.7872 mm`

If an exact Top of Steel property is unavailable, the bounding upper Z elevation is used **only for a confidently vertical member** and is explicitly marked approximately `TOS~ +502.7872 mm`; the source is logged `GEOMETRIC_TOP_APPROX`. Never present a geometry-derived number as exact PnP TOS. Missing/invalid values show `TOS: N/A`, not a fabricated number. Actual units assume source 3D drawing coordinates are millimeters, as in TEST5.

**Improved leader layout:** After generating the same ten native views and the same ten REV22.13 view titles, read each saved native viewport's direction, twist, view center and scale. Project the source member centroid onto that actual viewport exactly as in REV22.17. Sort source members by *projected Y* so their corresponding label order does not reverse; distribute MText labels over a larger dedicated vertical band outside the right side of the viewport, preserving other viewports, paper keepouts and view titles. Add one independently offset 3-segment, mostly orthogonal leader lane per actual member (source → horizontal lane → vertical landing → label). The old aggregated REV22.16 information panel and REV22.17 diagonal leader fan are not created by the REV22.18 command. Text height 2.1mm; individual TOS line below name uses native MText paragraph break. No forced metadata truncation, overlapping label rectangles, or fake per-member positions. If a label cannot fit, skip and explicitly log `PARTIAL`.

**Tests:** Exit Plant3D, NETLOAD new DLL, open the same TEST5 source drawing in a COPIED project, run `SPTC_ORTHO22_18_VERTICAL_TOS_TEST`, select SupportFocus and the same 21 support objects, optionally select the same 3 nearby pipes, choose an EMPTY registered native Ortho seed, enter `REV22.18 TEST`, then one click. Verify output TXT:
- `REV22_18_VERTICAL_SOURCE_MEMBER_COUNT` (native members recognized as vertical)
- `REV22_18_TOS_EXACT_PROPERTY_COUNT`
- `REV22_18_TOS_GEOMETRIC_APPROX_COUNT`
- `REV22_18_MEMBER_TOS` per handle, orientation source, exact vs approximate
- `STEP10_INDIVIDUAL_MEMBER_ANNOTATION_COUNT=130` expected if all 13 native members in all 10 views can safely receive labels
- `STEP10_VERTICAL_TOS_LABEL_COUNT=10 × REV22_18_VERTICAL_SOURCE_MEMBER_COUNT` expected when all TOS labels fit
- `STEP10_MEMBER_ANNOTATIONS_CHECK=PASS/PARTIAL`
- `STEP10_ALL_TEN_NATIVE_VIEWS_REGISTERED=True`, `STEP10_VIEW_TITLE_COUNT=10`

**Limits:** TOS project linkage/exact values require live Plant 3D runtime validation. A bounding-box orientation fallback can misclassify irregular steel shapes; confirm on each representative vertical member and inspect `DIRECTION_SOURCE`. Source midpoints can be hidden by other 3D members; this custom paper-space annotation is **not native PLANTORTHOANNOTATE**, does not provide occlusion-aware edges, and does not automatically update when model or native Ortho views change. The Autodesk internal `GenerateOrthos` call remains experimental, so copied project only. REV22.17 stays the functional rollback.

---

# REV 22.17 — Annotation leader for EVERY individual structural member in EACH Ortho view

**Why this revision:** The user validated REV22.16 on TEST5: 21 separate selected support objects, of which 13 are Plant 3D native Structure Members, all **13/13** linked to PnP properties. Distinct actual `Structure Member.Size` values are `Angle 75 thck 9`, `Round 80`, `Sloping channel 100x50 web-5 fl-7.5`. But REV22.16 printed only a single summary panel beside TOP VIEW; the user's screenshot explicitly requires a separate leader **from each structural member in the view**, not a shared size legend.

**New isolated command:** `SPTC_ORTHO22_17_MEMBER_LEADERS_TEST`. REV22.16, 22.15, 22.14, 22.13 remain unchanged and available. Use COPIED Plant 3D 2025 project only; authorize with `REV22.17 TEST`.

**Per-member identities:** `SupportOrthoMemberSource17` reads the exact per-source `PNP_STRUCTURE_SIZE handle=<handle> row=<row> SIZE=<value>` evidence successfully produced by REV22.16's Piping `DataLinksManager.FindAcPpRowId` / `GetAllProperties`. The identical ObjectId's geometric extents midpoint is used **only to locate the annotated member in 3D**, never to infer steel size. Each callout is `[<source handle>] <real member Size>` (e.g. `[55D] Sloping channel 100x50 web-5 fl-7.5`). All 13 are distinct even where several share the same steel profile size.

**Projected actual viewport targets:** After all ten native Ortho views have been generated and their axial/ISO camera-roll repairs validated, read each saved viewport `ViewDirection`, `ViewCenter`, `TwistAngle`, `CustomScale`, and paper `CenterPoint` directly. Use AutoCAD's arbitrary-axis (WorldToPlane) orientation to transform each native 3D member center into the correct WCS-derived viewport/display coordinates, then transform with twist/scale into paper units. A member is labeled only if its projected 3D anchor lands inside the corresponding saved viewport. This is the member's actual projected source midpoint, **not** an assembly center and not an arbitrary viewport corner. Exact source handle and 3D/paper positions are logged.

**Layout:** For each of TOP/BOTTOM/LEFT/RIGHT/FRONT/BACK/NW/NE/SW/SE, arrange the projected member labels in separate non-overlapping, top-to-bottom rows to the RIGHT of that Ortho viewport (but LEFT of the next column's native view or the right title-block keepout), sorted by member's projected vertical coordinate. A two-segment lightweight leader links the member anchor to its own label. Editable AutoCAD `MText`, text height 2.1mm, on `SPTC_MEMBER_CALLOUTS`; no resizing/repositioning of REV22.13 10 views/titles, no new OrthoCube. Paper-safe skips are explicitly logged rather than creating a false callout or allowing overlaps. Expected for TEST5: **13 source members × 10 Ortho views = 130 callouts** if all projected targets fit. Overlapping members in 3D may still have different leader lines to nearby targets; **occlusion is not fully verified**, so in heavily crowded viewports the user must visually inspect whether the leader targets truly appear on visible edges. This is not Autodesk associative `PLANTORTHOANNOTATE`.

**No BOM panel:** Remove the REV22.16 aggregated `STRUCTURAL SUPPORT / MEMBER: Angle..., Round..., Channel...` summary in this isolated revision only. Source record evidence is preserved in `*_REV22_17_RESULT.txt`. On selecting only a generic AutoCAD BlockReference without independently accessible PnP member objects, the plugin logs `NO_INDIVIDUAL_NATIVE_MEMBERS` rather than inventing nested steel identities or sizes. To annotate actual native structural members, select them individually (as the tested TEST5 21-object selection already does).

**Diagnostics:** `REV22_17_ANCHOR_SOURCE_MEMBER_COUNT`, `REV22_17_SOURCE_MEMBER`, `REV22_17_CAMERA_TOP/FRONT/...`, `STEP10_INDIVIDUAL_SOURCE_MEMBER_COUNT`, `STEP10_INDIVIDUAL_MEMBER_ANNOTATION_COUNT`, `STEP10_MEMBER_LABEL` for each paper leader, `STEP10_MEMBER_ANNOTATION_SKIP` for any source member outside view or skipped for space, `STEP10_MEMBER_ANNOTATION_EXPECTED`, `STEP10_MEMBER_ANNOTATIONS_CHECK=PASS/PARTIAL`. `PASS` requires every retrieved member to be annotated in all 10 views; unlike earlier logs, `PARTIAL` explicitly exposes omissions. The native Ortho view count and title count must stay 10 each.

**Test:** EXIT Plant 3D, NETLOAD new DLL, open registered source 3D TEST5 in copied project, run `SPTC_ORTHO22_17_MEMBER_LEADERS_TEST`. Choose SupportFocus; select the same **21** structural-support objects (not pipe routes); optionally select the same 3 crossing pipes; select a verified EMPTY native Ortho seed; type `REV22.17 TEST`; click once. Inspect the generated Ortho drawing: each of the 13 native members should have its own PnP size label/leader per view; source handles, positions and text must match. Share `*_REV22_17_RESULT.txt` and screenshots, including FRONT and one ISO view closeup. Save/reopen and manually run Update View twice to verify annotation survival separately.

**Limits:** The per-member labels are **custom nonassociative paper-space annotations**. They are not automatically reprojected or deleted after moving source geometry, changing a source Size, or running native `Update View`; rerun the generation or a future dedicated refresh command for reannotation. Also the reference screenshot portrays one large label; annotating 13 members in a view requires smaller (2.1mm) text and label rows. Avoid using this experimental internal `GenerateOrthos` API in production projects.

---

# REV 22.16 — Read Plant 3D Structure Member.Size From Project DataLinks

**Purpose:** The user's REV22.15 test selected 21 separate structural objects, including 13 actual Plant 3D Structure Members, but the compact panel incorrectly printed `MEMBER: SIZE NOT RECORDED`. A subsequent Properties screenshot confirms those native members *do* expose **Plant 3D > Structural > Size**, e.g. `Sloping channel 100x50 web-5 fl-7.5`, and `Long Description (Size)`. This information is stored in Plant 3D's PnP project database, not reliably in ordinary AutoCAD BlockReference attributes or .NET public entity properties.

**New isolated command:** `SPTC_ORTHO22_16_PNP_STEEL_SIZE_TEST`. REV22.15, REV22.14 and REV22.13 test commands remain present and unchanged.

**Read-only fix:** `SupportOrthoPlantProperties16.Inspect` keeps REV22.15's source CAD metadata inspection and additionally obtains `PlantApplication.CurrentProject.ProjectParts["Piping"].DataLinksManager`, invokes `FindAcPpRowId(ObjectId)` for selected **native StructureMember objects only**, then `GetAllProperties(rowId, true)`. It takes the actual project-recorded `Size` property (case-insensitive), preserving textual section designation exactly. It does not use Length/Top of Steel/Bottom of Steel or geometric bounding dimensions as a substitute; it never sets properties or writes the PnP DB. No additional third-party DLL is packaged; Autodesk's runtime is queried via reflection to preserve the existing standalone plugin build.

**Paper-space display:** The single compact `STRUCTURAL SUPPORT` panel from REV22.15 remains in the safe central gap, with `ASSEMBLY: 21 OBJECTS` or an actual `BLOCK: ...` identity. If the 13 Structure Members share a Size, show it once as `MEMBER: Sloping channel 100x50 web-5 fl-7.5`; if different channel/angle sizes exist, show their distinct values. Unknown sizes are still marked `MEMBER: SIZE NOT RECORDED`; no guess is made. The ten native views, view titles, original native view metadata, group placement, automatic SupportFocus and long-pipe clipping are unchanged.

**Diagnostics written in TXT:**
- `REV22_16_PNP_STRUCTURE_CANDIDATES` — count of structure members eligible for DataLinks.
- `REV22_16_PNP_LINKED_ROW_COUNT` — members linked to PnP rows.
- `REV22_16_PNP_MEMBERS_WITH_SIZE` — number with actual Size property.
- `REV22_16_MEMBER_SIZES` and `REV22_16_SOURCE_EVIDENCE` — exact values and their source handles.
- `REV22_16_PNP_DIAGNOSTIC` — read-only diagnostics if a Plant API is absent or a row is not linked.
- `STEP10_SUPPORT_PANEL_STATUS` and existing ten-view/title invariants.

**Test:** Completely exit Plant 3D 2025, extract and NETLOAD REV22.16 DLL, use a COPIED registered project, run the new command, select `SupportFocus`, select the same 21 structural support objects (including the 13 native structure members), optionally select the three pipe routes, choose an EMPTY registered native Ortho seed, enter `REV22.16 TEST`, click once. Inspect the PnP linked-row count, steel-size values, final compact panel, and ten Ortho views. Share `*_REV22_16_RESULT.txt` and screenshot. Save/reopen and test native Update View independently.

**Autodesk reference:** Autodesk Community confirms `DataLinksManager.GetAllProperties(partRowId, true)` returns the structural-member `Size` and `Length` fields: https://forums.autodesk.com/t5/autocad-plant-3d-forum/how-to-read-the-structural-member-profile-information-using-api/td-p/10201143. Autodesk also documents the native **Structure Member.Size** field for customized `PLANTORTHOANNOTATE` styles, as an alternative to this custom sheet annotation: https://www.autodesk.com/support/technical/article/caas/sfdcarticles/sfdcarticles/How-to-create-an-orthographic-annotation-that-shows-the-structure-member-s-size-in-AutoCAD-Plant-3D.html.

**Status:** The CI smoke suite can validate compilation, existing pure layout calculations, PnP access guards and native viewer code signatures, but cannot execute Autodesk PnP project retrieval without a live Plant 3D instance. Runtime validation is required. REV22.13 remains the original visually verified fallback. The experimental internal `GenerateOrthos` API is still used, so copied projects only.

---

# REV 22.15 — Compact Structural Annotations for Blocks AND Individual Members

**Isolated command:** `SPTC_ORTHO22_15_COMPACT_STEEL_ANNOTATIONS_TEST` (Plant 3D 2025, copied/test project only). REV22.13 and REV22.14 commands remain unchanged and available in the DLL.

## Correction from REV22.14

Runtime REV22.14 successfully registered 10 Ortho views, 10 view titles and 10 annotation callouts, but when the user selected 21 separate support objects (instead of one BlockReference), the labels repeated `NOT A BLOCK REFERENCE` and `NOT AVAILABLE (NO SECTION METADATA)` beside every view. This was both misleading and visually cluttered.

REV22.15 reads the **actual selected entities** and distinguishes:
- **Block Reference:** show the actual effective BlockReference name (dynamic block canonical name where possible).
- **Separate structural objects:** `ASSEMBLY: 21 OBJECTS` (or actual selected count). Never mislabel the selected objects as a block.
- **Recognized structural member sizes:** explicit values from section-size attributes, nested block or layer names, native structural-member section properties, and additional explicit `SECTION_SIZE=...` style XData or section-named extension-dictionary Xrecords. All metadata is read-only and documented in the result TXT with evidence.
- **No size metadata:** display the short `MEMBER: SIZE NOT RECORDED` **once**. Steel sections cannot be determined reliably from an anonymous 3D solid's overall bounding extents, and the program does not invent any C100/L75 sizes.

## Professional paper layout

The old ten repeated annotation text blocks are replaced in REV22.15 with **ONE** compact editable paper-space support information panel, in the unused gap between left and right view columns:
- Header: `STRUCTURAL SUPPORT`
- Body: `BLOCK: <actual block name>` or `ASSEMBLY: <count> OBJECTS`
- Body: `MEMBER: <actual CAD section sizes>` or `MEMBER: SIZE NOT RECORDED`
- Native editable `MText` on `SPTC_SUPPORT_ANNOTATIONS`, body 2.8 mm; compact border and header separator.
- View label `TOP VIEW`, etc., remains below each of the ten native views on `SPTC_VIEW_TITLES`. One-click placement, compact SupportFocus cube, long-pipe XY clipping, correct six axial/four iso cameras, native Ortho metadata, common scale, and title-block keepout remain unchanged.

The summary planner checks actual viewport and title frames before drawing the note; if a particular seed has no safe central gap, it skips **the annotation only** and logs `STEP10_SUPPORT_PANEL_STATUS=SKIPPED_NO_SAFE_SPACE`. It never disrupts the already-generated native ten Ortho views and titles.

**Test:** Exit Plant 3D, NETLOAD the new REV22.15 DLL, run `SPTC_ORTHO22_15_COMPACT_STEEL_ANNOTATIONS_TEST` in a copied project, select SupportFocus, select ONE BlockReference or its individual structural members, optionally select nearby pipes, pick a registered EMPTY native Ortho seed, authorize `REV22.15 TEST`, one click to place views. Verify `STEP10_SUPPORT_PANEL_STATUS=PASS` and that the source identity, available section sizes, title alignment and leader-free single note are visually correct. Share the `*_REV22_15_RESULT.txt` and screenshot. Save/reopen and test native Update View persistence separately.

**Scope:** This is a sheet-level source-data annotation, **not** native associative `PLANTORTHOANNOTATE` for each member. To label individual 3D steel elements with a leader to their precisely projected position, we would need additional per-component 3D-to-viewport projection and stable model association; this revision does not fabricate that link. The Autodesk internal GenerateOrthos invocation remains experimental. Use copied projects only.

---

# SPTC Support Drawings — REV22.14: Block Name + Structural Member Size Annotations

**Isolated command:** `SPTC_ORTHO22_14_STRUCTURE_ANNOTATIONS_TEST`. The previously visually verified REV22.13 command and REV22.12 SupportFocus stay unchanged. Test in **COPIED** AutoCAD Plant 3D 2025 project only.

**Requested behavior:** After ten native Ortho views and view titles are generated, create ten new compact paper-space callout notes, each beside its associated viewport, with a short paper-space leader pointing to the viewport frame. On separate editable MText layer `SPTC_SUPPORT_ANNOTATIONS`, text height 2.8 mm.

Each callout shows:
- `BLOCK: <real BlockReference name>` (dynamic blocks resolve through their canonical DynamicBlockTableRecord, not anonymous `*U##` name).
- `MEMBER SIZE: <CAD section-size metadata>`, multiple distinct values if present; e.g. `C100x50x5, L75x75x6`.

The program reads section values conservatively from declared source `SECTION`, `SECTION_SIZE`, `SIZE`, `MEMBER_SIZE`, `PROFILE`, `CHANNEL_SIZE`, `ANGLE_SIZE`, `PLATE_SIZE`, etc. block attribute tags; constant attributes; nested block attributes/names and structure-member section-named properties, plus explicit C/L/H/CH/ANGLE/CHANNEL tokens on member layers. It inspects **up to six block definition levels**, never explodes entities or modifies the source. It does NOT infer structural sections from world bounding-box sizes (which would misrepresent actual rolled-steel profiles). If no trustworthy section designation is stored, the note reads `MEMBER SIZE: NOT AVAILABLE (NO SECTION METADATA)`. In this case, inspect the `REV22_14_SOURCE_EVIDENCE` log and add a proper block attribute to the source if desired. It still annotates the true block name.

Callouts reference the overall structural-support VIEW, not a particular internal steel piece; no unsupported associative Plant 3D native annotation or 3D member-to-leader link is claimed. Each short paper-space leader ends at the view frame. The existing 10 native view definitions, clipping, rotation fixes, 1:30 scale, one-click positioning and previous title annotations are untouched. Callout paper bounds are calculated and collision-checked against all ten viewports and other notes; no text may enter the right 22% or bottom 12% title-block keepout. Geometry, source block, Ortho.dcf, and native view metadata are not edited.

**Runtime test:** Restart Plant 3D, `NETLOAD` the new DLL, open a copied model, run the command, choose SupportFocus, select the **3D structural-support block reference**, optionally select nearby pipes, select an EMPTY registered native Ortho seed, type `REV22.14 TEST` and click once. Inspect ten view titles, ten callouts, actual block name, exact listed structural size(s), and that there are no overlaps. Save/reopen, Update View twice and check labels persist. Share the `*_REV22_14_RESULT.txt` and a screenshot.

**Important:** If the support block was created from anonymous 3D solids and contains no explicit section data, CAD does not retain a reliable C/L/H section size automatically. In that case the model must be labeled in its block attributes or another trusted metadata store; the plugin deliberately does not guess from geometric extents. Non-production Autodesk internal `GenerateOrthos` API is still invoked.

---

# SPTC Support Drawings — REV22.13: Ten Native Ortho View Titles

**User request:** Automatically place an editable, printed view title on every view created by SupportFocus. Example: `TOP VIEW`. Do not alter the working REV22.12 automatic SupportFocus/Block Reference handling, native OrthoCube crop, 10 view presets, 6 axial and 4 ISO rotations, 1-click placement, scale or project-linked native views.

**Isolated command:** `SPTC_ORTHO22_13_VIEW_TITLES_TEST` in an AutoCAD Plant 3D 2025 **copied test project**. It retains the exact REV22.12 workflow (choose SupportFocus or Original; select structural support members or a valid 3D Block Reference; optionally select crossing long pipes; choose EMPTY registered Ortho seed; authorize with `REV22.13 TEST`; one click places ten views). No numeric clearance inputs.

**Titles created (one per saved native view):**
- `TOP VIEW`, `BOTTOM VIEW`, `LEFT VIEW`, `RIGHT VIEW`, `FRONT VIEW`, `BACK VIEW`.
- `NW ISOMETRIC VIEW`, `NE ISOMETRIC VIEW`, `SW ISOMETRIC VIEW`, `SE ISOMETRIC VIEW`.

**Presentation:** All titles are editable native AutoCAD paper-space `MText`, centered **below** their corresponding Ortho viewport, 4 mm text height, on a separate `SPTC_VIEW_TITLES` layer using the current drawing's text style. Labels are independent paper-space annotations: not part of model geometry, OrthoCube, viewport content or native view-definition metadata.

**Layout protection:** Pure `NativeOrthoViewTitlePlan` computes a 4 mm gap plus a reserved 6 mm title band below each viewport. If the group is too close to the native paper layout's bottom 12% title-block keepout, REV22.13 shifts the WHOLE 10-view group upward (one translation, not per-view movement) before native generation. The original 10 viewport relative spacing, X coordinates, sizes, scale, and 22% right keepout remain unchanged. The plan checks ten expected native preset labels exactly, bounds, all title/viewport and title/title intersections. An impossible sheet layout is rejected *before* any native views are generated. After successfully registering ten views, it checks the actual paper-space viewport frames then inserts the ten text objects atomically. The result TXT logs `STEP5_TITLE_RESERVED_SHIFT_Y_MM`, `STEP10_VIEW_TITLE_COUNT=10`, `STEP10_TITLE_TOP=TOP VIEW...`, and `STEP10_TITLE_PLACEMENT_CHECK=PASS`.

**Runtime test:** Restart Plant 3D and `NETLOAD` the REV22.13 DLL, generate in a copied registered project using the command. Confirm all ten labels appear centered under their respective views, have readable text and do not cover the title block, neighboring views, pipes or supports. Edit a title using AutoCAD text editing to confirm it is normal MText. Save/reopen, then update the native Ortho views twice and ensure labels persist in paper space. Check the four NW/NE/SW/SE isometrics, six standard directions, and compact SupportFocus cube remain correct. Send `*_REV22_13_RESULT.txt` and screenshots before and after Update View.

**Safety:** REV22.12 command remains unchanged as the verified fallback. REV22.13 still uses an unsupported Autodesk internal `GenerateOrthos` method and is NOT production-validated. Do not edit `Ortho.dcf` directly.

---

# SPTC Support Drawings — REV22.12: Automatic SupportFocus Clearance

**User request:** Preserve the working REV22.11 SupportFocus compact OrthoCube but remove manual XY clearance and pipe Z-reach numeric inputs. REV22.12 is a new independent test command; REV22.11 remains available unchanged for a manually tuned comparison.

**Command:** `SPTC_ORTHO22_12_AUTO_SUPPORT_CUBE_TEST` (Plant 3D 2025; backed-up/copied registered project only).

**Prompts now:** Choose `SupportFocus` (default) or `Original`; select **support members only**; optionally select crossing pipe route objects (Enter to skip); choose an **empty registered native Ortho seed**; authorize by typing `REV22.12 TEST`; place all ten views with one click. **No numeric clearance/reach values are requested** in SupportFocus.

**New deterministic auto-settings, based on source DWG units:**
- XY clearance **per side** = clamp(12% of the *smaller* selected support X/Y dimension, 50 mm, 160 mm).
- Vertical pipe reach **each side** = clamp(45% of the selected support Z height, 300 mm, 1200 mm).
- The existing NativeOrthoLayout 10%-per-axis padding (minimum 10 mm) still applies after this support-focused envelope.

Example from tested source TEST5 support raw dimensions ~703.275 × 924.229 × 1382.489 mm: auto XY ~84.393 mm per side, auto pipe vertical reach ~622.12 mm; actual dimensions are computed from each selected support, **not hardcoded to TEST5**. Pipe X/Y extents are used only for intersection checks, never added to the cube footprint. The Z extents of selected pipes that intersect that footprint and height range can expand the cube vertically, subject to the automatic Z bound.

**Logging:** `REV22_12_CUBE_MODE=SupportFocus`, `REV22_12_AUTOMATIC_CLEARANCE=True`, `REV22_12_AUTO_XY_CLEARANCE_MM`, `REV22_12_AUTO_PIPE_REACH_MM`, accepted/rejected pipe bounds, the actual focused and padded native cube bounds; the preserved axial+ISO camera, rotation, ten native view, scale and collision checks continue to run.

**Caution:** Although there is no numeric input, the optional pipe selection is intentionally preserved for source-linked nearby pipe geometry. This is not automatic discovery of every pipe across all model references or Xrefs. The algorithm uses entity bounding boxes, which may overstate intersections for bent/disconnected objects. If a selected pipe is far above the support and outside the derived Z reach, the test fails without writing a new Ortho; use REV22.11 manually tuned mode if necessary. Inspect visually, verify clipping, and test native Update View before treating new output as production-ready.

**Test procedure:** Fully restart Plant 3D, load REV22.12 DLL with `NETLOAD`, run the command on a copied project. Select the same support and crossing pipes as the successful REV22.11 case. Compare output to the REV22.11 model, and send the `*_REV22_12_RESULT.txt` and a 3D OrthoCube screenshot. Do not manually update before capturing the initial results. The code still invokes Autodesk's internal `GenerateOrthos` API; do not use this experiment on production projects.

---

# SPTC Support Drawings — REV22.11: Compact Support-Focused OrthoCube with Local Pipe Runs

**Requested behavior (November 9, 2026):** A long horizontal pipe crosses the top of a steel pipe support; the OrthoCube should wrap the **support and only the local crossing pipe section**, not the entire length of the pipe route. The native OrthoCube is a 3D crop and does not modify/shorten the 3D pipe itself.

**New isolated command:** `SPTC_ORTHO22_11_SUPPORT_CUBE_TEST`. This retains `SPTC_ORTHO22_10_AXIAL_ORIENTATION_TEST` and all earlier commands, all ten native view presets, six axial corrections, four successful isometric corrections, common scale, one-click PAPER placement, copied native registered Ortho seed, and the safety guards against frame overlap.

**Choose mode after running the command:**
- `SupportFocus` (default): select structural support members only; do **not** window-select a long pipe as part of the support selection, or its full GeometricExtents would invalidate a compact support envelope. The command rejects selected objects whose managed type ends in `Pipe`, when recognizable.
- `Original`: reuse REV22.10 original selection-bound box behavior, without any new pipe checks or clearance settings.

In `SupportFocus`, enter horizontal clearance per side (default **100 mm**) and maximum nearby pipe reach above/below support (default **500 mm**). Optionally select one or more pipes whose local section crosses the support. This selection contributes **Z only** to the focused envelope; the source model reference continues to include source DWG geometry, which the native OrthoCube should clip by its bounds. Pipe X/Y are only tested for intersection with the selected support footprint ± clearance; **the pipe route's remote X/Y endpoints can never enlarge the cube**. A pipe outside that footprint or beyond the configured Z reach is rejected. Pipe Z extents are capped to the reach in case the selected object also has remote vertical geometry. The standard NativeOrthoLayout 10% padding is then applied. Pipe selection can be skipped; this yields a support-only compact cube, possibly requiring adjustment of the vertical clearance to include pipes above the support.

**Limitation:** Axis-aligned CAD `GeometricExtents` are conservative boxes, not exact line-vs-box intersections. A distant bent pipe whose overall bounding box overlaps the support can appear to qualify even if no actual pipe segment passes through the cube. Selected route entities must be checked visually. This mode works when the structural support and pipe geometry are reachable in the current source DWG; pipe geometry in other Xrefs not enumerated by this command is not scanned or auto-selected. Native generator's support for clipping after Update View must be validated in a copied project before production.

**Test procedure (copied/backup Plant 3D 2025 project only):**
1. Restart Plant 3D, `NETLOAD` REV22.11 `SPTC.SupportDrawings.dll`, and open the saved registered support source DWG with the long pipe.
2. Run `SPTC_ORTHO22_11_SUPPORT_CUBE_TEST`, choose `SupportFocus` (Enter). Select **only structural support members**, set XY clearance (e.g. 100 mm) and maximum local pipe reach (e.g. 500 mm). Select the long pipe entity/entities crossing the support, or press Enter to skip.
3. Select an EMPTY native Project Manager Ortho seed, confirm new test drawing, type `REV22.11 TEST`, and click **once** inside the paper layout for all ten views.
4. Before manually updating, verify that the native OrthoCube is compact in X/Y, encloses the entire support and the nearby pipe diameter, and clips the long pipe ends. Verify TOP/BOTTOM/LEFT/RIGHT/FRONT/BACK and NW/NE/SW/SE orientations still match the reference.
5. Save `*_REV22_11_RESULT.txt` and screenshots of the source 3D support with long pipe and of the native Ortho Editor cube/ten views. Then manually Update View twice and verify the same clipping persists.

**Expected diagnostics:** `REV22_11_CUBE_MODE=SupportFocus`, `REV22_11_SUPPORT_RAW_MIN/MAX`, `REV22_11_SELECTED_PIPE_BOUNDS_COUNT`, `REV22_11_PIPE_BBOX_ACCEPTED`, `REV22_11_PIPE_BBOX_REJECTED`, `REV22_11_PIPE_Z_CLIPPED`, `REV22_11_FOCUSED_MIN/MAX`, `NATIVE_CUBE_MIN/MAX`, and the existing ten native registration, paper size, iso/axial readback and collision checks. All such PASS values confirm internal metadata consistency, not that the pipe rendered or cropped correctly; visual runtime comparison remains essential.

**No source geometry or Ortho.dcf direct edits.** This is still an experimental command because it invokes Autodesk's non-public `GenerateOrthos` API. Test only in a copied project.

---

# SPTC Support Drawings — REV22.10: Six Axial Orientation Test

**Evidence, 2026-11-09:** REV22.9 generated all ten model-linked views with one click and a common 1:5 scale; all ten saved preset names passed. The plugin-generated TOP/BOTTOM/LEFT/RIGHT/FRONT/BACK geometry was visible, but the screenshots compared with those same views after manual native Update View indicate a consistent 180° difference. Unlike REV22.7 NW/NE/SW/SE, the six axial viewports did **not** have initial TwistAngle or ViewCenter recorded. REV22.9's attempted automatic `PLANTORTHOUPDATE All` failed with `eInvalidInput` and is **not used** in this revision.

**New test command:** `SPTC_ORTHO22_10_AXIAL_ORIENTATION_TEST`. It derives from the proven independent REV22.8 ten-view generator and preserves the 4 working isometric camera vectors, NW/NE/SW/SE native saved types, ISO half-turn + ViewCenter correction, all six saved axial ViewType names, 1-click layout, 1:5 fit and collision checks.

After each `GenerateOrthos` call **only for TOP/BOTTOM/LEFT/RIGHT/FRONT/BACK**, it reads the actual camera direction, `TwistAngle`, and `ViewCenter`. If the initial twist is exactly 0° or 180° modulo 360°, the isolated experiment applies an additional **180°** screen rotation and negates BOTH `ViewCenter` axes atomically on this *freshly generated test viewport*. It verifies that the camera direction remains unchanged and the twist/center values persist; otherwise it aborts rather than guessing. Diagnostics per axial view: `STEP8_<VIEW>_AXIAL_CAMERA_MATCH_BEFORE`, `AXIAL_TWIST_BEFORE_DEG`, `AXIAL_VIEWCENTER_BEFORE`, `AXIAL_TWIST_AFTER_DEG`, `AXIAL_VIEWCENTER_AFTER`, `AXIAL_CAMERA_MATCH_AFTER`, `AXIAL_FRAME_ROTATION_CHECK`. REV22.7 ISO logs remain intact, so successful ISO geometry can be rechecked.

**Why experimental:** The screenshots establish a *visual* half-turn but do not independently prove the 6 initial native viewport twist/center differences. A viewport's projected geometry can also differ for other reasons (native view definitions, source transform, clipping). REV22.10 is a narrowly scoped test hypothesis, **not yet a confirmed fix**, and it does not change any native view-definition metadata or `Ortho.dcf`. It retains older working commands for comparison.

**Test (COPY project only):**
1. Completely close Plant 3D 2025, reopen the backed-up/copied project, `NETLOAD` the fresh REV22.10 DLL.
2. Open the saved registered 3D support model, run `SPTC_ORTHO22_10_AXIAL_ORIENTATION_TEST`; select the same support members, choose an empty native registered Ortho seed, type `REV22.10 TEST`, and click once in the paper layout.
3. **Do not manually Update View.** Screenshot the six orthographic projections immediately after the command and compare each with the manually updated REV22.9 reference screenshot. Capture NW/NE/SW/SE too, verifying they stay correct.
4. Inspect `*_REV22_10_RESULT.txt` for six `AXIAL_FRAME_ROTATION_CHECK=PASS`, saved native view types, ten registered views and zero frame overlap. A PASS in the TXT only establishes that stored viewport numbers match the experimental operation, not that visible geometry is correct.
5. Only after recording an initial screenshot, run native Edit View / Update View on all ten, save/reopen, capture a second screenshot and note if geometry moved/rotated again.

**Acceptance:** All ten present immediately with their correct reference orientations and fit; no extra manual update required to reach correct appearance; all native presets and the four ISO views remain correct. If any frame remains wrong, send the TXT and both screenshots, and investigate native ViewTarget/ViewCenter mapping rather than applying further arbitrary flips.

---

# SPTC Support Drawings — REV22.9: Automatically Finalize All Ten Views

**Runtime finding (2026-11-09):** The REV22.8 generated TXT reports all six TOP/BOTTOM/LEFT/RIGHT/FRONT/BACK strings recognized by the installed Plant 3D 2025 native Edit View parser, saved IDwgViewDef.ViewType matches each short name, ten views registered, and all four NW/NE/SW/SE isometrics correctly centered. Nevertheless, the user still has to invoke manual **Update View** after initial generation to see the standard view results correctly. This means the remaining requirement is automatic *native content regeneration/finalization*, not another camera-vector or ViewType-string change.

**Official Plant 3D 2025 mechanism:** Autodesk documents `PLANTORTHOUPDATE` with the `All` option and the `PLANTORTHOSILENTUPDATE` registry-backed system variable. `PLANTORTHOSILENTUPDATE=1` forces all views to regenerate even when considered up to date, and suppresses missing-model/proxy warnings; it is **only** temporarily enabled on a confirmed, backed-up project and restored afterward. Sources: https://help.autodesk.com/cloudhelp/2025/ENU/Plant3D-UserGuide/files/GUID-527A9774-33FD-4423-8D5E-F01ABC16DF5E.htm and https://help.autodesk.com/cloudhelp/2025/ENU/Plant3D-UserGuide/files/GUID-E1CAA1D6-C6C6-44D1-8A5E-1BFB9C0204CA.htm

**New independent command:** `SPTC_ORTHO22_9_AUTO_UPDATE_TEST`. It is a copy of the working REV22.8 isolated generator, preserving the six native preset names `Top/Bottom/Left/Right/Front/Back`, correct four isometric presets, REV22.7 ISO-only `TwistAngle=0` with ViewCenter compensation, and one-click ten-view placement. Once all ten views are registered with no detected initial overlaps, it executes a **second command-context phase** on the active native Ortho DWG:
1. Save the generated, project-registered Ortho drawing using `QSAVE`.
2. Read the current user setting `PLANTORTHOSILENTUPDATE`, set it temporarily to 1.
3. Run `PLANTORTHOUPDATE` with the `All` option synchronously through the document editor.
4. Restore the setting in both success and failure paths.
5. Save the updated drawing, verify the ten native view names remain registered, check ten paper-space viewports, and report any overlap/off-sheet condition.

**Diagnostic log:** `*_REV22_9_RESULT.txt`. In particular, inspect `STEP11_PLANTORTHOUPDATE_ALL_ENTERED`, `STEP11_PLANTORTHOUPDATE_ALL_RETURNED`, `STEP11_SILENTUPDATE_RESTORED=True`, `STEP11_ALL_TEN_NATIVE_VIEWS_STILL_REGISTERED=True`, `STEP11_POSTUPDATE_NEW_VIEWPORT_COUNT=10`, `STEP11_POSTUPDATE_VIEWPORT_OVERLAP_OR_OFFSHEET=False`, `STEP11_AUTO_UPDATE_COMPLETED=True`, and `FINAL_STATUS=TEN_NATIVE_VIEWS_AUTO_UPDATED_CHECK_VISIBLE_GEOMETRY`. A failure is logged as `STEP11_AUTO_UPDATE_FAILURE`; do not interpret other prior generation PASS messages as proof of auto-update success.

**Safety:** Test only in a duplicate project. Native `PLANTORTHOUPDATE All` may alter all current Ortho views, and silent mode suppresses warnings about missing files and proxies. This isolated command creates a **fresh** ten-view native drawing and performs only this one extra native update; it never touches the earlier test DWGs and does not edit `Ortho.dcf` directly. Do not deploy to production until the resulting ten views are visually correct immediately after the command **without any manual Update View**.

**Install/test:**
1. Close Plant 3D 2025; load the new DLL with `NETLOAD` in a copied registered project.
2. Open source support model, run `SPTC_ORTHO22_9_AUTO_UPDATE_TEST`; select two support members, choose an empty registered Ortho seed, type `REV22.9 TEST`, and click **once**.
3. Do **not** manually select Update View. Inspect TOP/BOTTOM/LEFT/RIGHT/FRONT/BACK as well as NW/NE/SW/SE immediately; capture an overall drawing screenshot and the native Edit View selection for one axial and one ISO view.
4. Send the REV22.9 TXT and screenshot. Check that the PLANTORTHOSILENTUPDATE variable was restored.
5. If the internal native updater reports failure/unsupported command, preserve the TXT and partial copied test drawing. Do not keep trying in a production project.

**Scope:** The supported Autodesk Update All command is used only to finalize new views; the underlying `GenerateOrthos` entry point remains unsupported/internal and needs live Plant 3D verification. A successful CI build does not prove Update All behavior in the native editor.

---

# SPTC Support Drawings — REV22.8: TOP/BOTTOM/LEFT/RIGHT/FRONT/BACK Native Edit View Presets

**Runtime user finding:** REV22.7 successfully shows all four isometrics (NW, NE, SW, SE) with full support geometry after correcting native viewport rotation and center. The remaining six orthographic/axial views appear as **Current View** when opened in the Plant 3D 2025 native Ortho Editor.

**Root-cause candidate from source:** Earlier commands saved `TopView`, `BottomView`, `LeftView`, `RightView`, `FrontView`, `BackView` into `GenerateOrthos` `strViewType`, but only confirmed the parser recognition of the four isometric strings `NW`, `NE`, `SW`, `SE`. Actual native `UIUtils.GetViewTypeFromName(string)` recognition of the six standard strings was never verified. REV22.8 does not assume any undocumented name is correct. It probes the *installed* Plant 3D 2025 Edit View parser for each axial view (short enum name first, previous `*View` form, and other labels), selects the first name mapping exactly to its expected native `ViewType` enum, and aborts before any geometry generation if no candidate matches.

**New isolated command:** `SPTC_ORTHO22_8_AXIAL_PRESET_TEST` (copied project/empty registered native Ortho seed only). The earlier `SPTC_ORTHO22_7_ISO_FRAME_TEST` command and working NW/NE/SW/SE camera, native type identifiers, twist and ViewCenter compensation, one-click layout and six standard camera vectors are unchanged. REV22.8 additionally checks parser matches for **all ten views** and reads back each saved `IDwgViewDef.ViewType` for all six axial views; a saved type mismatch stops the test. Logs include `STEP5_<AXIAL>_AXIAL_CANDIDATES`, `STEP5_<AXIAL>_EDITOR_PRESET`, `STEP8_<AXIAL>_SAVED_VIEWDEF_TYPE` and `STEP8_<AXIAL>_SAVED_TYPE_CHECK`.

**How to test:** Fully restart Plant 3D 2025 and `NETLOAD` the REV22.8 DLL. In a backed-up/copied project, open the same registered source model DWG as REV22.7 and run `SPTC_ORTHO22_8_AXIAL_PRESET_TEST`. Select the same support members and an empty project-registered native Ortho seed. Accept the warning and type `REV22.8 TEST`, click **once** in the paper layout, and save the new `_ORTHO_R28AXIAL_` drawing. Verify the **Edit View preset selector** individually for TOP/BOTTOM/LEFT/RIGHT/FRONT/BACK and all NW/NE/SW/SE; capture screenshots. Run native **Update View twice**, save and reopen, and check that the six axial labels remain correct, four isometrics remain fully visible, and no viewport overlaps occur. Send `*_REV22_8_RESULT.txt` and after-update screenshot(s).

**Limitations:** Native Edit View parser and `GenerateOrthos` are Autodesk internal APIs. Parser matching and readback tests do not prove Edit View UI behavior or Update View persistence: only actual Plant 3D 2025 runtime acceptance can do that. If an axial candidate is not recognized, this test intentionally fails without inventing a type identifier or altering previous working commands.

---

# SPTC Support Drawings — REV22.7 Native Isometric Frame Center Fix (Experimental)

**Evidence:** REV22.6.1 comparison against manually created native NW/NE/SW/SE shows matching normalized `ViewDirection`, 0° ≡ 360° `TwistAngle`, and 1:5 `CustomScale`, but the plugin-generated views appear blank or partly clipped while native reference views show geometry. Previously uploaded REV22.4 audit files also show this repeatable view-center reversal on all four views after native Update View:

| ISO | REV22.5 center immediately after generation | Native Update View center |
|---|---|---|
| NW | (347.236462, -17.816316) | (-347.236462, 17.816316) |
| NE | (-129.819230, 107.709582) | (129.819230, -107.709582) |
| SW | (129.819230, -293.244548) | (-129.819230, 293.244548) |
| SE | (-347.236462, -167.718651) | (347.236462, 167.718651) |

**Hypothesis:** REV22.5 changed each native isometric paper viewport's `TwistAngle` from 180° to 0° but retained its `ViewCenter` in the previous rotated display coordinate system. That reorients the viewport without compensating the center, often sending the support off screen. The native Update View appears to compensate by reversing `ViewCenter` along both axes.

**New independent experiment command:** `SPTC_ORTHO22_7_ISO_FRAME_TEST`. It copies a registered empty Ortho seed and generates all 10 native views in one click, preserving the working REV22.3 sizes, native short NW/NE/SW/SE view types, WCS camera vectors, native OrthoCube bounds, 1:5 scaling (or selected common scale), and the six standard axial views. Immediately after `GenerateOrthos` **for NW, NE, SW, SE only**, it reads the native viewport twist/center, verifies the WCS-up projection, and either:
- If twist=180°: sets both `TwistAngle=0` and `ViewCenter=(-oldX,-oldY)` in one transaction on that newly created paper viewport.
- If twist already equals 0° modulo 360°: preserves `ViewCenter` and normalizes twist.
- If twist is neither 180° nor 0°: stops without editing the viewport, rather than guessing.

Logs: `STEP8_<ISO>_ISO_TWIST_BEFORE_DEG`, `ISO_VIEWCENTER_BEFORE`, `ISO_TWIST_AFTER_DEG`, `ISO_VIEWCENTER_AFTER`, `VIEWCENTER_COMPENSATED`, `ISO_FRAME_COORDINATES_CHECK`. The existing actual paper-size, 10 native-view catalog, camera quadrant and saved native preset checks remain enabled. A failed check aborts further generation. Older REV22.5 and REV22.6.1 commands are **unchanged**.

**CAUTION:** The script uses Autodesk's unsupported internal `GenerateOrthos` method. This is a controlled, test-only framing hypothesis, *not* a proven native Ortho source link or regeneration fix. Do not run against production files or edit `Ortho.dcf`.

**Test procedure in a copied project:**
1. Fully close Plant 3D 2025; reopen copied project; load new `SPTC.SupportDrawings.dll` with `NETLOAD`.
2. Open the same registered source 3D support DWG, run `SPTC_ORTHO22_7_ISO_FRAME_TEST`.
3. Pick the same two support members, empty registered native Ortho seed; consent with `REV22.7 TEST`; click once to place the group.
4. **Before Edit View or Update View**, capture the NW/NE/SW/SE screenshot. Confirm all 4 supports are actually visible and centered. Save `*_REV22_7_RESULT.txt`.
5. Use native Edit View / Update View twice, save and reopen, capture another screenshot and run `SPTC_ORTHO22_4_ISO_AUDIT`.
6. Compare to your native gold reference screenshot. Send generation TXT, audit TXT, and before/after screenshots.

**Acceptance:** All ten views registered with no collisions; 4 ISO `ISO_FRAME_COORDINATES_CHECK=PASS`; all four supports visible *immediately*; no disappearance after repeated Update View; native Edit View preset still shows NW/NE/SW/SE. Successful compilation alone is not runtime confirmation.

---

# REV22.6.1 — Fix JIT Crash in Native Iso Reference Capture

**AutoCAD 2025 runtime failure:** `System.InvalidOperationException: Use SetRejectMessage first.` inside `NativeOrthoIsoReferenceComparison.TrySelectFour`, reproduced by `SPTC_ORTHO22_6_CAPTURE_NATIVE_ISO`. The cause was `PromptEntityOptions.AddAllowedClass(typeof(Viewport),false)` preceding `SetRejectMessage(...)`.

**Scope of fix:** In `TrySelectFour`, call `SetRejectMessage(...)` first and `AddAllowedClass(...)` second. No other viewport selection, drawing mutation, camera, twist, native view metadata, source reference, or one-click Ortho logic was altered. The **existing** `SPTC_ORTHO22_6_CAPTURE_NATIVE_ISO` and `SPTC_ORTHO22_6_COMPARE_NATIVE_ISO` command names stay unchanged. CI verifies prompt initialization order and builds a separate `SPTC-SupportDrawings-Plant3D2025-REV22_6_1` DLL artifact.

**Install/test:** Fully close Plant 3D 2025 to unload the previously loaded plugin, reopen a copied project, `NETLOAD` the DLL from REV22.6.1, then run `SPTC_ORTHO22_6_CAPTURE_NATIVE_ISO` in an Ortho **paper layout** on a reference sheet containing manually created NW, NE, SW, SE viewports. Click the paper-space viewport borders in that order. Repeat with `SPTC_ORTHO22_6_COMPARE_NATIVE_ISO` on the plugin-generated sheet. Confirm no JIT dialog and inspect the reference/comparison TXT reports. DLL compilation does not prove Plant 3D runtime behavior until this test passes.

---

# SPTC Support Drawings — REV22.6: Compare Native Isometric Presets (Read-Only)

**Evidence, 2026-11-09:** REV22.5 generated 10 native views using one click, with no detected viewport overlap. The four native isometric view names and WCS camera directions passed. REV22.4 audits before and after native Update View show that the viewports now retain **0° / 360°** twist, not the earlier 180° error. However, these logs do **not** establish that NW/NE/SW/SE generated linework matches manually created Plant 3D preset views. REV22.6 gathers an independent native reference before changing any more code.

**Existing REV22.5 generation and all earlier commands remain unchanged. REV22.6 is strictly read-only to AutoCAD DWGs/Plant project.** It only writes diagnostic TXT files alongside the DWG.

**Step A — capture true native presets:**
1. In a **copied/backed-up** Plant 3D 2025 project, create four reference views manually through **Plant 3D Ortho Editor** using the built-in **NW**, **NE**, **SW**, **SE** options (same project-registered 3D source support; same model orientation). Save this registered Ortho DWG, switch to the paper layout. Capture screenshots of the native Edit View selector for each.
2. `NETLOAD` the compiled REV22.6 `SPTC.SupportDrawings.dll` and run `SPTC_ORTHO22_6_CAPTURE_NATIVE_ISO`.
3. Select the four **paper-space viewport borders** in order: **NW, NE, SW, SE**. The command saves `*_REV22_6_NATIVE_REFERENCE_<timestamp>.txt`. The labels come from your selection order, not from automatically verified view metadata, so select carefully.

**Step B — compare to REV22.5:**
1. Open the existing REV22.5-generated test Ortho DWG with NW/NE/SW/SE. Save and switch to the paper layout.
2. Run `SPTC_ORTHO22_6_COMPARE_NATIVE_ISO`. Choose the captured native reference TXT.
3. Select the four **plugin-generated viewport borders** in the same **NW, NE, SW, SE** order. A report `*_REV22_6_COMPARE_<timestamp>.txt` is written alongside the DWG.

**Comparison:** The reference and generated `ViewDirection` vectors are normalized before comparison (tolerance 0.1°); `TwistAngle` differences are compared modulo 360° (tolerance 0.5°). Actual scale is informational, because native reference and plugin-generated viewports can use different scales. The report explicitly distinguishes **CAMERA_MATCH**, **ROLL_MATCH**, **SCALE_MATCH**, and **VIEWPORT_CAMERA_AND_ROLL_MATCH** for each view. This does **not** compare generated linework or prove that Autodesk's `IDwgViewDef.ViewType` metadata matches; visually compare the native and generated images and inspect Edit View presets.

**Send:** the native reference TXT, plugin comparison TXT, and paired native-versus-generated isometric screenshots. If camera or roll mismatches are found, correct only the mapping implicated by those measurements; do not alter six working standard directions or the REV22.3 paper layout. If all properties match but output linework differs, investigate the native Ortho model link, view metadata, clipping, and geometry regeneration rather than guessing at the twist.

---
# SPTC Support Drawings — REV22.5: Four-Isometric Viewport Twist Test

**Observed from uploaded 2026-11-09 logs:** `*_REV22_3_RESULT.txt` confirms all ten native views registered with one click and correct actual viewport frames, scale 1:5, `ACTUAL_VIEWPORT_OVERLAP_DETECTED=False`, and no title-block collision. It also confirms saved native `NW`, `NE`, `SW`, `SE` view type strings and correct WCS camera quadrants. A separate `*_REV22_4_ISO_AUDIT_*.txt` reports **`VIEW_TWIST_DEG=180` on every isometric viewport**, despite matching view directions. Since camera up vectors were explicitly chosen as the projection of WCS +Z, a half-turn in the paper viewport makes the displayed object inverted.

**New isolated generation test command:** `SPTC_ORTHO22_5_ISO_TWIST_TEST`. Does not replace or modify REV22.3 generation or REV22.4 read-only audit commands. The new command uses the exact same ten-view native generation, per-view paper-frame and collision validation, and four short native isometric preset identifiers. After `GenerateOrthos` completes for NW, NE, SW, SE **only**, it logs the actual `Viewport.TwistAngle`, checks that the requested `upVector` matches projected WCS +Z, and sets `TwistAngle=0` on the **newly generated paper-space viewport**. It reads back the value, logs `TWIST_NORMALIZED_CHECK=PASS`, and preserves the existing camera, native metadata, scale and placement tests. TOP/BOTTOM/LEFT/RIGHT/FRONT/BACK are unchanged.

**WARNING:** Directly changing the generated viewport twist in a fresh test Ortho DWG is an **experimental workaround**, not a supported native Ortho API. Native Update View or Edit View might recompute the twist or reapply internal sheet metadata. Do not use in production, do not directly edit `Ortho.dcf`, and do not treat the initial `PASS` as proof of post-update persistence.

**Test in a copied backed-up project only:** Restart Plant 3D 2025, `NETLOAD` the new DLL, open the registered source 3D support DWG, run `SPTC_ORTHO22_5_ISO_TWIST_TEST`; select the same steel members, an empty registered Project Manager Ortho seed, accept the warning and type **`REV22.5 TEST`**. Click once on the paper space. Confirm isometric display is no longer upside-down. Save the `_ORTHO_R25TWIST_` drawing and run `SPTC_ORTHO22_4_ISO_AUDIT` before and again after two native **Edit View / Update View** operations. Upload the `*_REV22_5_RESULT.txt`, both `*_REV22_4_ISO_AUDIT_*.txt`, and before/after screenshots showing each native view preset in Edit View.

**Success requires more than build:** ten model-linked views, 1-click group, zero overlap, native view types, all four `TWIST_NORMALIZED_CHECK=PASS`, correctly oriented visible projections, and 0-degree twist with stable Edit View presets **after** saving, reopening and repeated Update View. If Update View returns to 180 degrees, inspect the native view definition's stored rotation rather than rewriting the viewport repeatedly.

---

# SPTC Support Drawings — REV22.4: Read-Only Isometric Audit After Update

**Observed REV22.3 screenshot (2026-11-09):** The four NW/NE/SW/SE frames are now compact and separate, but the user reports that their resulting projections remain incorrect. The screenshot does not show the native **Edit View** preset selection, the original Plant 3D viewport metadata, or the REV22.3 TXT log. Previous REV22.2 reports verified short native IDs and a matching camera *immediately after generation*, which does not prove that those values are retained after Edit View or Update View.

**REV22.4 diagnostic command (does not generate views):** `SPTC_ORTHO22_4_ISO_AUDIT`.

1. In a copied/backed-up Plant 3D 2025 project, open the generated native Ortho DWG. **Before** clicking Update View, run the audit command and click the real paper-space viewport border for **NW**, then **NE**, **SW**, **SE**, in that order.
2. The command writes `<DWG>_REV22_4_ISO_AUDIT_<date>.txt` alongside the DWG; it does not modify the DWG, its view metadata, or `Ortho.dcf`.
3. Use Plant 3D's native **Edit View** selector on each of the four. Capture whether it shows the actual preset or **Current View**. Do not alter the selection just to collect this evidence.
4. Run native **Update View**, save/reopen as needed, then run `SPTC_ORTHO22_4_ISO_AUDIT` a second time in the same way. Supply both audit TXT files, the REV22.3 generation `*_REV22_3_RESULT.txt`, and the Edit View selector screenshot.

**Recorded values:** user-identified viewport handles, WCS view directions and expected named isometric quadrants, twist angles (degrees), view targets, model view centers, paper-space positions and sizes, custom scales, and view heights. The audit does not assume NW/NE/SW/SE based on sorting or sheet coordinates. It checks camera direction against the standard WCS quadrant, but does **not** falsely equate that check with proof of correct native Edit View persistence or generated geometry.

**Scope:** This is intentionally read-only investigation, not a claim to have fixed the incorrect isometric views. Do **not** change camera vectors, twist angles, metadata strings or existing production drawings without verifying the post-update state against a view produced by Plant 3D's native editor. REV22.3 generation and REV22.2 metadata fixes remain unchanged; the successful six axial views and ten-view one-click placement are preserved. The command may require clicking viewport *borders* rather than model linework.

---

# SPTC Support Drawings — REV22.3: Native Paper-Space Viewport Scale and Layout Test

**User evidence:** `SPTC_TEST3_3EA_ORTHO_R22META_20261109_004924_0e0c_REV22_2_RESULT.txt` shows ten registered views, one click, four saved native isometric presets (`NW`, `NE`, `SW`, `SE`), and five overlapping viewport pairs. A TOP view planned at **76.163 × 19.950 mm** was actually **362.680 × 95.000 mm** on the 594 × 841 mm sheet. The same actual dimensions appeared in all ten viewports. The previous non-public `GenerateOrthos` call received unscaled `bounds.SizeX, bounds.SizeY` (362.68, 95) in its `viewportWidth` and `viewportHeight` parameters.

**Separate experimental command:** `SPTC_ORTHO22_3_SCALE_LAYOUT_TEST`. REV22.2 and all earlier commands are preserved. REV22.3 passes the **per-view planned paper-space frame width/height** (`item.Width`, `item.Height`) as those two non-public API arguments instead of raw source-model bounds, preserving the original cube bounds, view direction/up vector, native preset, view scale, single placement click, and project registration logic. This is a hypothesis from observed runtime measurements, **not a documented Autodesk API contract**.

**Important runtime guard:** Immediately after each native generation, REV22.3 reads the **actual** viewport `CenterPoint`, `Width`, `Height`, `CustomScale`, and `ViewHeight` from the DWG. It rejects a significant difference from the planned frame or scale, safe-sheet violations, and overlaps with views already generated in this test. A failure stops further view creation, logs the detailed reason, and leaves the partially registered DWG for inspection. Do not manually delete registered drawings or edit `Ortho.dcf`. The existing final ten-viewport report remains supplemental, not a substitute for per-view validation.

**Testing:** Only in a separate copied/backed-up Plant 3D 2025 project: restart, `NETLOAD` the new DLL, open/save a project-registered 3D source, run `SPTC_ORTHO22_3_SCALE_LAYOUT_TEST`, select one support, choose an empty native registered Ortho seed, explicitly confirm the warning, type **`REV22.3 TEST`**, and click **once** inside the A1 paper. Save the new `_ORTHO_R23SIZE_` DWG; verify frame dimensions, title-block clearance, saved isometric presets, and run native **Edit View / Update View twice** on representative standard and NW/NE/SW/SE views. Upload the new `*_REV22_3_RESULT.txt` and before/after screenshots. Only the real Plant 3D runtime can validate whether the native internal API honors these corrected paper dimensions or preserves them after Update View.

**Expected diagnostic:** `STEP8_<VIEW>_ACTUAL_PAPER_LAYOUT_CHECK=PASS` for each of the ten views. `NATIVE_FRAME_SIZE_CHANGED`, `NATIVE_CENTER_CHANGED`, `NATIVE_VIEWPORT_SCALE_CHANGED`, `NATIVE_VIEWPORT_OFF_SAFE_SHEET`, or `NATIVE_VIEWPORT_OVERLAPS_*` is a test failure, not a successful layout. All earlier native iso stored-type and camera diagnostics remain enabled.

---

# SPTC Support Drawings — REV22.2: Fix Ortho Editor 'Current View' Metadata

**Confirmed root cause from the user's original PnP3dOrthoDrawingsUI.dll 16.0.58.1102 .NET 8 assembly, not an assumption:** `UIUtils::.cctor` stores `s_NWView = "NW"`, `s_NEView = "NE"`, `s_SWView = "SW"`, `s_SEView = "SE"`, while `GetViewTypeFromName(string)` recognizes only those exact preset short names (or its localized resources). It has a separate `Current` class. `PlantOrthoView.GenerateOrthos` writes its `strViewType` argument directly to native `IDwgViewDef.ViewType`. Earlier REV22.1 passed `NWIsometric`/`NEIsometric`/`SWIsometric`/`SEIsometric`: those do not match the native parser, explaining why reopening Edit View displays Current View even when the viewport looks isometric.

**REV22.2 correction:** Independent command `SPTC_ORTHO22_2_EDITVIEW_FIX_TEST` passes the *exact native stored view type* `NW`, `NE`, `SW`, or `SE` for those four views; TOP/BOTTOM/LEFT/RIGHT/FRONT/BACK remain unchanged. Before generating, it dynamically calls the installed `UIUtils.GetViewTypeFromName()` to verify that each short name yields its expected native ViewType enum. It stops if the runtime DLL disagrees. After each isometric creation, it attempts a read-only native `IDwgViewDef.ViewType` readback through `GetDwgSheetFromOrthoView` and `GetViewDefByViewName`, logging `STEP8_NW_SAVED_VIEWDEF_TYPE=NW` and `STEP8_NW_SAVED_TYPE_CHECK=PASS` when confirmed; unavailable metadata is explicitly `INCONCLUSIVE`. It continues to inspect viewport camera orientation and register all ten views. No direct Ortho.dcf modification.

**Run only in a COPIED Plant 3D 2025 project.** Restart Plant 3D, NETLOAD the new DLL, open the registered 3D source, run `SPTC_ORTHO22_2_EDITVIEW_FIX_TEST`, select one support and an empty registered native Ortho seed, acknowledge the unsupported internal API warning, type `REV22.2 TEST`, and click once to place the ten-view group. Save the resulting `*_ORTHO_R22META_*.dwg`, open the NW/NE/SW/SE views with **Edit View**, and check whether the ribbon now displays each correct preset rather than Current View. Test **Update View** on all four isometric views and provide the `*_REV22_2_RESULT.txt` plus screenshot.

**Warning:** REV22.2 uses Autodesk's non-public `GenerateOrthos()` and creates a new registered Ortho test clone. Do not run in production; existing wrongly classified views from REV22/22.1 are not automatically repaired by this command. GitHub compilation verifies code, not Edit View's runtime behavior. REV19.4, REV20, REV21.1, REV22 and REV22.1 source commands remain unchanged.

---
# SPTC Support Drawings — REV22.1: Native Isometric Current View Fix (Experimental)

The user reports that REV22 NW, NE, SW and SE Isometric output is using Current View. In REV22 all four camera vectors and ViewType enums were distinct, but the unsupported generator's `strViewType` string was `NWIsometricView`, `NEIsometricView`, `SWIsometricView` or `SEIsometricView`. Those strings are not validated against Autodesk's native naming convention. Autodesk documents `NWIsometric`, `NEIsometric`, `SWIsometric`, `SEIsometric` as isometric scheme identifiers. REV22.1 tests these canonical strings as a possible fix; runtime proof is still required.

REV22.1 is a NEW command `SPTC_ORTHO22_1_ISO_FIX_TEST`, preserving REV22 and all earlier commands unchanged. It changes only the native `strViewType` argument for NW, NE, SW, SE. Six standard views retain their exact type strings and vectors. REV22.1 logs (a) selected native enum, (b) previous type name and canonical native type name, (c) actual paper viewport camera before internal `GenerateOrthos`, and (d) actual camera after. It stops rather than reporting success if the viewport camera changes to Current View or any other incorrect direction.

Test ONLY in a COPIED Plant 3D 2025 project: restart, NETLOAD REV22.1 DLL, open registered source 3D model, run `SPTC_ORTHO22_1_ISO_FIX_TEST`, select a support and an EMPTY Project Manager-created native Ortho seed, confirm warning, type `REV22.1 TEST`, click once on the A1 Ortho sheet. Review all 10 views and native Update View. Upload `*_REV22_1_RESULT.txt`, and screenshots of NW NE SW SE orientations. All native registrations and Update View are unverified until tested.

Important: REV22.1 still uses the unsupported internal `GenerateOrthos` 16-argument method. Naming alone may not fix Current View if native internals use the editor's active camera; a green build indicates only C# compatibility, not native view correctness. Do not modify production Ortho.dcf or delete failed registered clones manually.

---
# SPTC Support Drawings — REV22: All Ten Views in One Experimental Group

REV22 is a separate, unsupported native Plant 3D 2025 Ortho experiment for a COPIED TEST PROJECT ONLY. It does not replace REV19.4, REV20 or REV21.1.

New command: SPTC_ORTHO22_TENVIEW_ONECLICK_TEST

Views from the supplied Plant 3D menu, in order: TOP, BOTTOM, LEFT, RIGHT, FRONT, BACK, NW Isometric, NE Isometric, SW Isometric, SE Isometric. REV22 constructs ten correctly normalized view/up directions and validates the exact native enum type for every view before native generation.

A1 sheet design: two columns by five rows, one common automatically chosen scale for all ten projected envelopes. Each diagonal isometric view is separately projected to compute its required width and height. A preliminary 22% right and 12% bottom title-block keepout are reserved. One click sets the TOP anchor for the entire group; if the group cannot fit there, it shifts together and logs the adjusted coordinates. Real template collisions remain subject to user visual confirmation.

Native safety: reuse the REV21 exact 16-parameter unsupported internal GenerateOrthos signature check, source/seed registration checks, working database verification, strict per-view OrthoCube extent checks, independent viewport ObjectId creation, and exact native view registration checks after each call. REV22 removes the previous 8-view truncation in catalog diagnostic so all ten can be checked. No raw Ortho.dcf writes, no keystroke scripts. A partially registered failure remains in copied project for inspection.

Test: Restart AutoCAD Plant 3D 2025. NETLOAD the REV22 DLL in a copied test project. Open and save a registered 3D source model. Run SPTC_ORTHO22_TENVIEW_ONECLICK_TEST. Select one support, then a registered EMPTY Project Manager Ortho seed, accept warning, type REV22 TEST, and click once on the Ortho paper. If generated, save; visually check all ten view directions, scale, borders/title block, and perform native Update View for each. Upload the new *_REV22_RESULT.txt log and a screenshot.

Important: Compiling REV22 does not establish that the unsupported internal generator creates updateable model links or that it handles all ten native isometric types. REV21.1 internal generation has not yet been runtime-validated beyond the one-click preflight. User must test in a completely separate project backup.

---
# SPTC Support Drawings — REV21.1: One-Click Auto-Fit Group Anchor

**REV21 runtime evidence:** The user clicked paper point (115, 370) mm on an A1 portrait sheet (594×841 mm). Native internal `GenerateOrthos` signature preflight passed, but the FRONT view's calculated position fell inside the bottom reserved margin. The original REV21 safety guard correctly stopped **before creating any native views**.

**REV21.1 fix:** Continue using the same separate `SPTC_ORTHO21_ONECLICK_INTERNAL_TEST` command, but allow the user to click **anywhere inside the physical paper sheet**. If the selected TOP position would push FRONT or SIDE off the allowed paper/title-block area, calculate the feasible TOP-anchor interval from **all three projected frame dimensions** and shift the entire group to the closest feasible position. No additional placement click is requested. Maintain the same relative TOP/FRONT/SIDE offsets, common scale, clipping checks, collision checks, and native registration checks.

For the previously failing anchor (115,370), the group will move **upward together**; the logged effective TOP anchor will differ from the clicked point. This deliberate translation is displayed in the command line and logged as `STEP5_ONE_CLICK_EFFECTIVE_TOP_ANCHOR`, `STEP5_ONE_CLICK_GROUP_TRANSLATION`, `STEP5_ONE_CLICK_AUTO_SHIFTED`, and the feasible anchor ranges. A click outside physical paper or an impossible-to-fit group still fails closed before native calls. The conservative right/bottom keepout is an approximation; inspect the actual title block after creation.

**How to test:** Restart Plant 3D 2025; NETLOAD REV21.1; open a **copied test project**, registered source model and empty native Project Manager Ortho seed; run `SPTC_ORTHO21_ONECLICK_INTERNAL_TEST`; select support and seed; confirm `REV21 TEST`; click **once** near the previously failing (115,370) location. If the API generates views, save the new `_ORTHO_R21ONCE_` drawing and test native **Update View** on all TOP/FRONT/SIDE views. Upload the new `_REV21_RESULT.txt` and a screenshot.

**Still experimental:** Autodesk's internal `GenerateOrthos` is unsupported and may fail even with valid placement. This patch fixes the observed preflight refusal only. Never use in production until model links, visual geometry, and Update View are proven. REV19.4 and REV20 remain unchanged and available as fallback.

---

# SPTC Support Drawings — REV21: One-Click Placement Test

**Experimental and unsupported: COPY OF PROJECT ONLY.** Do not run this against your production Plant 3D project.

REV20 succeeded in creating and registering native TOP, FRONT, SIDE views but called the public \`PlantOrthoView.CreateOrthos\` three times, which prompted for **three placement clicks**. REV21 is a separate test command that attempts the same work from **one** paper-space TOP anchor.

## Run \`SPTC_ORTHO21_ONECLICK_INTERNAL_TEST\`

1. Start AutoCAD Plant 3D 2025 and use the **copied test project**. Close the blank native Ortho seed's DWG tab.
2. \`NETLOAD\` the REV21 DLL, open/save any registered 3D model containing the support, run \`SPTC_ORTHO21_ONECLICK_INTERNAL_TEST\`.
3. Select the steel members belonging to one support, then choose a Project Manager-created, **empty and registered** native Ortho seed.
4. Read the explicit unsupported-API warning, approve, and type **\`REV21 TEST\`** to authorize creating a new registered experimental Ortho DWG.
5. On the paper-space sheet **click exactly once** near the displayed TOP anchor point. FRONT and SIDE positions are calculated automatically from the selected point, the actual paper size, and a shared view scale. For A1 portrait (594×841 mm), the default TOP suggestion is approximately (166, 601) mm.
6. If validation fails, inspect \`*_REV21_RESULT.txt\`. Leave the failed registered test DWG in the copied project for diagnosis. Do not delete project files or edit \`Ortho.dcf\` manually.
7. If three native views appear, save the drawing, inspect the title block and projections, and run **\`PLANTORTHOUPDATE\` → All** (or Update View on each view). **Only mark a test successful if model links and Update View work.** Upload the result log and an Ortho sheet screenshot.

## Internal implementation and risk

The public \`CreateOrthos\` API triggers \`ProcessViewportPlacement\` / \`Editor.Drag\` per call and has **no insertion point**. REV21 experiments with the **NON-PUBLIC** \`PlantOrthoView.GenerateOrthos\` entry point, requiring exactly 16 parameters including a **paper-space Viewport ObjectId** and explicit paper-space \`Point3d ptIns\`. It refuses to invoke a method whose *full runtime parameter signature* differs from the analyzed AutoCAD Plant 3D 2025 DLL.

The code creates three proposed paper viewports in the new registered Ortho DWG and invokes internal generation once for each view using calculated centers. It verifies the working database, model bounds, actual OrthoCube extents and the registered native view catalog after each call. Since this is a non-public internal method, its initialization and required state **might not match the standalone call**; the first runtime test may fail or create an incomplete native view. The code logs the failure and stops; it does not silently delete registered drawings or patch the Project Manager database. Do not treat a compiled DLL as evidence that Update View works.

**Fallback:** The fully validated REV19.4 TOP-only command and the working REV20 three-click TOP/FRONT/SIDE command remain **unchanged**.

---

# SPTC Support Drawings — REV20: Native TOP + FRONT + SIDE

**REV19.4 is preserved as the confirmed working one-command TOP-only baseline.** Its original command remains \`SPTC_ORTHO19_TOP_ONECMD_TEST\`. The user confirmed native TOP creation without a placement click and Update View.

## New isolated experiment

Command: \`SPTC_ORTHO20_THREEVIEW_ONECMD_TEST\`.

Run it **only in a copied test project** with a registered source 3D drawing and a native Project Manager-created *empty* Ortho seed. Select one support, choose the seed and type \`REV20 TEST\` to authorize a new, uniquely named project-registered native Ortho DWG. The test reuses REV19.4's verified read/share, registration and target-document command-context strategy. It then initializes a real native OrthoCube for each view and calls only the **public PlantOrthoView.CreateOrthos** API sequentially for TOP (look +Z, up +Y), FRONT (look −Y, up +Z) and SIDE (look +X, up +Z). Each newly registered view is verified against the native project view catalog before proceeding.

The sheet planner reads the **actual title-block paper size** and calculates one common standard scale, plus non-overlapping *proposed* TOP-above-FRONT / SIDE-next-to-FRONT positions. The public CreateOrthos signature does not accept insertion coordinates. **Native placement may not follow those proposals**; REV20 does not claim otherwise and never moves linked native viewport objects. It probes paper-space viewports after generation, logging possible overlaps and conservative right/bottom title-block keepout conflicts. These warnings require visual verification of the actual template.

If any view fails, REV20 stops subsequent view generation and preserves the registered partially completed test DWG for inspection; it never edits Ortho.dcf or calls private GenerateOrthos.

**Test steps:** Restart Plant 3D 2025; NETLOAD the new DLL; open your registered 3D model in the copied project; run \`SPTC_ORTHO20_THREEVIEW_ONECMD_TEST\`; select your support; pick your EMPTY registered Ortho seed; approve and type \`REV20 TEST\`. Open \`*_ORTHO_R20THREE_*.dwg\`, inspect TOP/FRONT/SIDE geometry, orientation, placement and title-block clearance. Save and test **Update View** individually or \`PLANTORTHOUPDATE\` → All. Send \`*_REV20_RESULT.txt\` and a screenshot.

**Runtime status:** Pending user validation. GitHub compilation does not prove that all three native views are correctly positioned or updateable.

---

# SPTC Support Drawings — REV19.4: Working Database Synchronization

## REV19.4 — Working database mismatch diagnosis and scoped synchronization

**Observed in REV19.3 runtime:** Entered the registered target Ortho document command context (`IsApplicationContext=False`), the active Document matched the registered target, but `HostApplicationServices.WorkingDatabase` did **not** pass a `ReferenceEquals` comparison with the target document database. The command stopped before native OrthoCube initialization. This result alone does not prove that the underlying AutoCAD database was wrong: different .NET wrappers may represent the same native drawing.

**Change:** First log the working and target database filenames. Compare **normalized full DWG paths**, not only .NET wrapper object identity. Only when the paths differ, set `HostApplicationServices.WorkingDatabase = targetDoc.Database` in the verified target document command context. Immediately verify the effective working database filename. Stop if it still differs. After the operation, synchronize working database to whichever AutoCAD drawing is active so a previous source-drawing database is not left active while the generated Ortho drawing is the active tab. Keep exact REV19.2 native OrthoCube bounds validation, copied-test-project consent, public native CreateOrthos only, and the prior exception logging guard.

**Why:** Autodesk's managed API documentation says the working database supplies drawing context for many DB operations and should stay synchronized with document switching. Reference: https://help.autodesk.com/cloudhelp/2026/ENU/OARX-ManagedRefGuide/files/OARX-ManagedRefGuide-Autodesk_AutoCAD_DatabaseServices_HostApplicationServices_WorkingDatabase.html

**Retest only in copied project:** Restart AutoCAD Plant 3D 2025; NETLOAD REV19.4; open any registered source 3D model, save it; run `SPTC_ORTHO19_TOP_ONECMD_TEST`, select the support, choose the native registered **empty** Ortho seed, and type `REV19 TEST`. Upload the fresh `_REV19_RESULT.txt` and a TOP view screenshot if generated. Do not select an earlier failed clone or populated REV18 output as the seed.

**Key new log entries:** `STEP5_WORKING_DB_BEFORE`, `STEP5_EXPECTED_WORKING_DB`, `STEP5_WORKING_DB_WRAPPER_EQUALS`, `STEP5_WORKING_DB_SYNC_ENTERED` (only if filenames differ), `STEP5_WORKING_DB_EFFECTIVE`, `STEP5_WORKING_DB_VERIFIED_TARGET=True`, and `STEP5_NATIVE_CUBE_EXTENTS_MATCHED=True` (if native cube works). If the cube still returns sentinel values, report and diagnose rather than bypass the geometry check.

**Status:** REV19.4 is unverified in the Plant 3D runtime until the user tests the compiled build. REV18 automatic TOP generation and Update View remain the confirmed working baseline. No private API or direct `Ortho.dcf` editing is introduced.

## REV19.3 — Fix native OrthoCube initialization after switching drawings

**Observed:** REV19.2 created/registered/opened the native Ortho clone and calculated the same valid padded support bounds as REV18, but `PlantOrthoCube.InitializeCube(Extents3d)` returned uninitialized `Extents` with `Min=(+1e20,+1e20,+1e20)` and `Max=(-1e20,-1e20,-1e20)`. These are empty-envelope sentinel values, not structural support geometry. The previous code correctly stopped before generating a TOP view.

**Likely root cause:** REV19 uses `CommandFlags.Session` (AutoCAD application context) to switch drawings. REV18 successfully ran as a document-context command inside an already open native Ortho drawing. The REV19.2 session code switched `MdiActiveDocument` but called the native OrthoCube API before entering the newly opened drawing's **document command context**.

**Change:** REV19.3 retains the registered source/seed checks, all unique native drawing registration behavior, and the strict cube dimension comparison. After opening/activating the new registered Ortho drawing, it uses **`DocumentManager.ExecuteInCommandContextAsync`** to schedule the native cube initializer and public `CreateOrthos` on AutoCAD's target document context. It logs and **fails closed** if the document is not active or its working database does not match the target. All delegate exceptions are caught and written to `_REV19_RESULT.txt`; no direct editor writes to the inactive source document.

**Important:** The new context handoff is an *unverified hypothesis* until tested in the actual Plant 3D 2025 runtime. It neither bypasses geometry validation nor modifies the confirmed REV18 command. The public TOP API may still show native dialogs depending on project state.

**Test:** Restart Plant 3D, load the REV19.3 DLL with `NETLOAD`, open the registered 3D source in your fully copied test project, run `SPTC_ORTHO19_TOP_ONECMD_TEST`, select one support, select an empty native Project Manager seed under `Orthos/DWGs`, and enter `REV19 TEST`. Inspect the new unique `_ORTHO_R19AUTO_` drawing and upload the new `_REV19_RESULT.txt`.

**Key new log fields:**
- `STEP5_TARGET_COMMAND_CONTEXT_REQUESTED`
- `STEP5_TARGET_COMMAND_CONTEXT_ENTERED`
- `STEP5_TARGET_CONTEXT_IS_APPLICATION` (expected `False`)
- `STEP5_TARGET_CONTEXT_ACTIVE_DOC_MATCH` (expected `True`)
- `STEP5_TARGET_CONTEXT_WORKING_DB_MATCH` (expected `True`)
- `STEP5_NATIVE_CUBE_EXTENTS_MATCHED` (expected `True`)
- `STEP8_PUBLIC_CREATEORTHOS_RETURNED` if native TOP generation completes.

Never delete a failed clone directly from the project folder or edit `Ortho.dcf` manually. Do not reuse a previous failed `_ORTHO_R19AUTO_` DWG as the empty seed. Test only in the copied project.

Autodesk documentation: https://help.autodesk.com/cloudhelp/2022/ENU/OARX-ManagedRefGuide/files/OARX-ManagedRefGuide-Autodesk_AutoCAD_DatabaseServices_HostApplicationServices_WorkingDatabase.html ; https://blog.autodesk.io/creating-a-sample-autocad-plugin-with-net-80-and-ccli-for-autocad-2025/

## REV19.1 — Fix for `eFileSharingViolation` on the empty seed

The first REV19 runtime log stopped on `Database.ReadDwgFile(seed, FileShare.Read, true, null)` before creating the native clone. No Ortho view generation occurred. REV19.1 changes **only the seed-read overload** to `Database.ReadDwgFile(seed, FileOpenMode.OpenForReadAndAllShare, true, null)`, followed by `CloseInput(true)` before `SaveAs`. Autodesk documents this open mode as permitting reading a DWG already open for write. The rest of the REV19 native clone/register/TOP sequence remains unchanged.

**Re-test:** Close any open seed drawing tab and save the blank seed first, then restart Plant 3D 2025, `NETLOAD` REV19.1, open the registered 3D source, run the same `SPTC_ORTHO19_TOP_ONECMD_TEST` command, choose the empty native Ortho seed, and type `REV19 TEST` in the backup project. Attach the new `_REV19_RESULT.txt`. If the file-sharing error persists, check other AutoCAD instances, Windows Explorer preview or network file locks; do not edit `Ortho.dcf` or delete the seed.

Source: https://help.autodesk.com/cloudhelp/2022/ENU/OARX-ManagedRefGuide/files/OARX-ManagedRefGuide-Autodesk_AutoCAD_DatabaseServices_FileOpenMode.html

**Target:** AutoCAD Plant 3D 2025, Windows x64, .NET 8.

## Key validated user result

The user confirmed REV18's public `PlantOrthoView.CreateOrthos(...)` generated a genuine native TOP view **without any manual viewport-placement click**, and **Update View worked** on the resulting Ortho. The runtime log contained `STEP5_PUBLIC_CREATEORTHOS_RETURNED` and `POST_CALL_ORTHO_VIEW_CATALOG=EXISTING_NATIVE_VIEWS:SPTC_TOP_R18_060123`.

**REV19 integrates the REV16 native seed-cloning pathway and the REV18 public native generation pathway into a separate end-to-end command.** This new integrated sequence has compiled, but its cross-document switch is not yet confirmed in the installed Plant 3D runtime. No claim of production readiness.

## Command

`SPTC_ORTHO19_TOP_ONECMD_TEST` — **COPIED/INDEPENDENT TEST PROJECT ONLY.**

The command is started in the **active, registered 3D model**. Unlike REV18, it does **not** require the user to create an empty REV16 clone manually or save/read a REV17 JSON report.

### What it does

1. Inspect the currently active registered source model and select **one support's steel members**.
2. Compute accurate extents and the per-axis-padded native OrthoCube. Use the source model's physical units dynamically.
3. Choose a native **empty Project Manager-created Ortho seed** (an actual registered DWG under the active project's Ortho root such as `Orthos/DWGs`). Reject raw DWTs, outside-project drawings, native-invalid seeds, or seeds with any registered views.
4. Ask for backup-project consent (**Yes**) and exact command-line phrase **`REV19 TEST`**.
5. Clone the native seed into a **new unique** `SPTC_<model>_<handle>_ORTHO_R19AUTO_<date>_<random>.dwg` alongside the seed (never overwrite), register it via `OrthoProject.AddPnPDrawingFile`, verify `OrthoDocUtils.IsValidOrthoDrawing`, and verify native view catalog reports **zero** views.
6. Use a **Session** command to open and activate the newly registered drawing. Find and switch to its actual paper-space plot layout (e.g. A1 portrait `Title Block`) and read its live configured paper size.
7. Compute the TOP scale candidate from the actual sheet and current support OrthoCube. In the previous test source this was approximately **1:2** for an A1 portrait sheet; no project or test geometry names are hard-coded.
8. Initialize native `PlantOrthoCube` using `InitializeCube(Extents3d)`, compare its extents against the computed support cube, create `Solid3d` via `MakeCubeCopy()`, construct public `PlantOrthoView(false,false)`, and invoke the **public** `CreateOrthos(... Top ...)` against the freshly opened registered drawing.
9. Check the native registered view catalog. Leave the target drawing open for visual inspection, save and manually confirm **Update View**.

### Limits / safeguards

- REV19 needs one support selection, an empty native seed selection, and a copied-project confirmation. **One command ≠ zero prompts.** The *native viewport placement* was reported automatic in REV18, but the newly integrated command is a separate runtime experiment, not yet verified to be dialog-free.
- **No private** `GenerateOrthos`, no source/project name/handle hard-coding, no DCF/SQLite writes, no simulated mouse or keyboard input. The native public API owns all model-link metadata and view generation.
- The public `PlantOrthoView` constructor could still open a native dialog or switch drawings; the code verifies the active document and aborts if it changed unexpectedly.
- The estimated TOP paper center is logged but is **not** passed as an insertion coordinate; the public method does not expose one. View location and title-block clearance must be inspected.
- The OrthoCube limits the projected region, not the exact set of objects to render: nearby 3D objects in the source may appear.
- If a failure occurs after registration, the new registered DWG is left for manual inspection; it is **not** deleted or removed from the project database automatically.
- Before running, **save source model changes**, so the Ortho view links to the same on-disk geometry selected for the extent calculation.
- Previous REV16 / REV17 / REV18 commands remain intact; do not test REV19 on the existing successful REV18 output.

## Installation and test

1. Make a full separate backup/copy of the Plant 3D project. Open the copy in Plant 3D 2025.
2. Ensure a **blank, registered native Ortho drawing** created through Project Manager exists. `SPTC_NATIVE_SEED_01.dwg` is a prior example; any active project/filename works. A seed containing a view is not acceptable.
3. Restart Plant 3D, `NETLOAD` the compiled REV19 `SPTC.SupportDrawings.dll`.
4. Open the registered **source 3D model**, save any changes, and type `SPTC_ORTHO19_TOP_ONECMD_TEST`.
5. Select all support steel members, then press Enter.
6. Choose the **blank native Ortho seed** in the project's Ortho drawing folder.
7. Confirm **Yes** only in the copied test project and type `REV19 TEST`.
8. Check for a newly generated `_ORTHO_R19AUTO_` DWG in Project Manager. The command should try to open it and call the TOP native API, without a manual viewport placement click (to be verified). Do not switch away manually during the API call.
9. In the new DWG, inspect geometry and sheet location, save, and test **Update View**.
10. Upload the `*_REV19_RESULT.txt` log from the same Ortho folder, plus a screenshot of the TOP result. The log is written before each risky native call, so an abnormal exit still leaves a partial report.

### Log meanings

| Entry | Meaning |
|---|---|
| `STEP1_NATIVE_SEED_CLONED=PASS` | New physical DWG written from native empty seed |
| `STEP2_PROJECT_REGISTERED_EMPTY_ORTHO=PASS` | Native Ortho Project registration and validator both succeeded |
| `STEP3_ORTHO_DOCUMENT_ACTIVE=PASS` | Active source switched to the new Ortho drawing |
| `STEP4_SHEET_MM` | Actual selected layout's physical plot sheet size |
| `STEP5_NATIVE_CUBE_INITIALIZED_AND_VALIDATED=PASS` | Real OrthoCube created from selected support extents |
| `STEP6_NATIVE_CUBE_SOLID=PASS` | Native cube solid created in memory |
| `STEP8_PUBLIC_CREATEORTHOS_RETURNED` | Public Ortho generation method returned; output still requires validation |
| `STEP9_NATIVE_VIEW_CATALOG=EXISTING_NATIVE_VIEWS:...` | Native Ortho project registered a view |
| `FINAL_STATUS=TOP_API_RETURNED_CHECK_UPDATE_AND_GEOMETRY` | API returned; native Update View / visible geometry not automatically tested |
| `NATIVE_EXCEPTION` or `ERROR` | Failure details; upload report |

## Future

After **REV19 one-command TOP** succeeds with visible geometry and Update View, the next step is independent FRONT and SIDE views with safe sheet layout positioning, without replacing the proven TOP generator.

References: `docs/Plant3D_2025_Ortho_API_DLL_Analysis_REV10.md`, `NativeOrthoSeedCloneTest.cs`, `NativeOrthoTopGenerationTest.cs`.

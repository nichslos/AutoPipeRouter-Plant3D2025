using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;
using WinForms = System.Windows.Forms;

namespace SPTC.SupportDrawings;

/// <summary>
/// REV09 diagnostic (read-only): identify available Plant 3D Ortho SDK types,
/// project context, Ortho drawing registration APIs and selected model envelope.
/// Does not launch Ortho, change project databases, or simulate UI clicks.
/// </summary>
public sealed class SupportNativeOrthoProbe
{
    [CommandMethod("SPTC_ORTHOAUTOPROBE", CommandFlags.Modal)]
    public void Run()
    {
        Document? doc = AcadApp.DocumentManager.MdiActiveDocument;
        if (doc == null) return;
        Editor ed = doc.Editor;

        PromptSelectionResult selected = ed.GetSelection(new PromptSelectionOptions
        {
            MessageForAdding = "\nSelect the support members intended for automatic native Ortho: "
        });
        if (selected.Status != PromptStatus.OK) return;

        using var dialog = new WinForms.SaveFileDialog
        {
            Title = "SPTC REV09 - Save Native Ortho API Feasibility Report",
            Filter = "Text report (*.txt)|*.txt",
            FileName = "SPTC_Ortho_API_Probe_REV09.txt",
            DefaultExt = "txt",
            AddExtension = true,
            OverwritePrompt = true
        };
        if (dialog.ShowDialog() != WinForms.DialogResult.OK) return;

        try
        {
            string report = CreateReport(doc, selected.Value.GetObjectIds());
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(dialog.FileName))!);
            File.WriteAllText(dialog.FileName, report, new UTF8Encoding(false));
            ed.WriteMessage("\nREV09 Ortho API diagnostic saved: " + dialog.FileName);
            ed.WriteMessage("\nNo Ortho drawing was modified. No dialog was bypassed.");
            ed.WriteMessage("\nThis report identifies the API dependencies needed for a safe unattended Ortho implementation.");
        }
        catch (System.Exception ex)
        {
            ed.WriteMessage("\nREV09 diagnostic failed: " + ex);
        }
    }

    private static string CreateReport(Document doc, ObjectId[] selection)
    {
        var result = new StringBuilder(12000);
        result.AppendLine("SPTC SUPPORT DRAWINGS REV09 - PLANT 3D NATIVE ORTHO API PROBE");
        result.AppendLine("READ ONLY / NO UI AUTOMATION / NO PROJECT WRITE");
        result.AppendLine("Date: " + DateTime.Now.ToString("O", CultureInfo.InvariantCulture));
        result.AppendLine("Source model: " + doc.Database.Filename);
        result.AppendLine("Units: " + doc.Database.Insunits);
        result.AppendLine();

        ExtractBounds(doc, selection, result);

        result.AppendLine("== ORTHO DLL FILES IN INSTALLED APPLICATION FOLDER ==");
        string programPath = "";
        try { programPath = Process.GetCurrentProcess().MainModule?.FileName ?? ""; }
        catch (System.Exception ex) { result.AppendLine("Application executable unavailable: " + ex.Message); }

        if (!string.IsNullOrEmpty(programPath))
        {
            result.AppendLine("Executable: " + programPath);
            try
            {
                string folder = Path.GetDirectoryName(programPath)!;
                foreach (string path in Directory.EnumerateFiles(folder, "*Ortho*.dll",
                    SearchOption.TopDirectoryOnly).Take(70))
                {
                    try
                    {
                        var an = AssemblyName.GetAssemblyName(path);
                        result.AppendLine(Path.GetFileName(path) + " | " + an.FullName);
                    }
                    catch (BadImageFormatException)
                    {
                        result.AppendLine(Path.GetFileName(path) + " | native/non-.NET DLL");
                    }
                    catch (System.Exception ex)
                    {
                        result.AppendLine(Path.GetFileName(path) + " | " + ex.GetType().Name);
                    }
                }
            }
            catch (System.Exception ex)
            {
                result.AppendLine("DLL enumeration unavailable: " + ex.Message);
            }
        }

        var loaded = AppDomain.CurrentDomain.GetAssemblies()
            .OrderBy(a => a.GetName().Name, StringComparer.OrdinalIgnoreCase).ToArray();
        result.AppendLine();
        result.AppendLine("== ORTHO AND PLANT PROJECT ASSEMBLIES LOADED INTO THE RUNNING HOST ==");
        Assembly[] matching = loaded.Where(a =>
        {
            string name = a.GetName().Name ?? "";
            return name.Contains("Ortho", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("PnPProjectManagerMgd", StringComparison.OrdinalIgnoreCase);
        }).ToArray();

        if (matching.Length == 0)
            result.AppendLine("NONE currently loaded. Open normal PLANTORTHOCREATE once, cancel it, then repeat this probe.");
        foreach (Assembly assembly in matching)
        {
            result.AppendLine("Assembly: " + assembly.FullName);
            try { result.AppendLine("Location: " + assembly.Location); }
            catch (System.Exception ex) { result.AppendLine("Location unavailable: " + ex.GetType().Name); }
            try
            {
                IEnumerable<Type> interestingTypes = GetTypesSafely(assembly)
                    .Where(t => t != null &&
                        (t.Name.Contains("Ortho", StringComparison.OrdinalIgnoreCase) ||
                         t.FullName?.Contains("PlantApplication", StringComparison.OrdinalIgnoreCase) == true))
                    .OfType<Type>().Take(85);
                foreach (Type type in interestingTypes)
                {
                    result.AppendLine("  Type: " + type.FullName);
                    var methods = type.GetMethods(BindingFlags.Public |
                                                  BindingFlags.Instance | BindingFlags.Static |
                                                  BindingFlags.DeclaredOnly)
                        .Where(m => m.Name.Contains("Ortho", StringComparison.OrdinalIgnoreCase) ||
                                    m.Name.Contains("Create", StringComparison.OrdinalIgnoreCase) ||
                                    m.Name.Contains("Drawing", StringComparison.OrdinalIgnoreCase) ||
                                    m.Name.Contains("View", StringComparison.OrdinalIgnoreCase))
                        .Take(35);
                    foreach (MethodInfo method in methods)
                        result.AppendLine("    " + method);
                }
            }
            catch (System.Exception ex)
            {
                result.AppendLine("  Reflection unavailable: " + ex);
            }
        }

        result.AppendLine();
        result.AppendLine("== PROJECT CONTEXT (READ ONLY) ==");
        try
        {
            var plantApplicationType = loaded.SelectMany(GetTypesSafely)
                .FirstOrDefault(t => t?.FullName ==
                    "Autodesk.ProcessPower.PlantInstance.PlantApplication");
            if (plantApplicationType == null)
            {
                result.AppendLine("PlantApplication type not loaded.");
            }
            else
            {
                object? project = plantApplicationType.GetProperty("CurrentProject",
                    BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
                if (project == null) result.AppendLine("No active Plant 3D project.");
                else
                {
                    Type t = project.GetType();
                    result.AppendLine("PlantProject type: " + t.FullName);
                    foreach (string prop in new[] { "ProjectFolderPath", "ProjectName", "Name" })
                    {
                        try
                        {
                            PropertyInfo? pi = t.GetProperty(prop);
                            if (pi?.GetIndexParameters().Length == 0)
                                result.AppendLine(prop + ": " + (pi.GetValue(project) ?? "null"));
                        }
                        catch (System.Exception ex)
                        {
                            result.AppendLine(prop + " unavailable: " + ex.GetType().Name);
                        }
                    }
                    try
                    {
                        object? parts = t.GetProperty("ProjectParts")?.GetValue(project);
                        result.AppendLine("ProjectParts type: " + parts?.GetType().FullName);
                        if (parts != null)
                        {
                            PropertyInfo? item = parts.GetType().GetProperty("Item");
                            object? ortho = item?.GetValue(parts, new object[] { "Ortho" });
                            result.AppendLine("Ortho project part: " + ortho?.GetType().FullName);
                            if (ortho != null)
                            {
                                foreach (MethodInfo m in ortho.GetType().GetMethods(
                                    BindingFlags.Public | BindingFlags.Instance)
                                    .Where(m => m.Name.Contains("Drawing", StringComparison.OrdinalIgnoreCase) ||
                                                m.Name.Contains("Create", StringComparison.OrdinalIgnoreCase) ||
                                                m.Name.Contains("Add", StringComparison.OrdinalIgnoreCase))
                                    .Take(50))
                                    result.AppendLine("  Ortho project method: " + m);
                            }
                        }
                    }
                    catch (System.Exception ex)
                    {
                        result.AppendLine("Ortho project-part retrieval unavailable: " + ex);
                    }
                }
            }
        }
        catch (System.Exception ex)
        {
            result.AppendLine("Project reflection failure: " + ex);
        }

        result.AppendLine();
        result.AppendLine("== AUTOMATION BLOCKER ==");
        result.AppendLine("The usual PLANTORTHOCREATE workflow opens drawing and OrthoCube dialogs.");
        result.AppendLine("To bypass these, require a verified native Ortho SDK API for:");
        result.AppendLine("1) creating/registering a project Ortho DWG;");
        result.AppendLine("2) selecting source model, OrthoCube bounds, view and scale;");
        result.AppendLine("3) placing and committing the generated view without a UI prompt.");
        result.AppendLine("The method name CreateOrthos alone does not prove all steps can be automated.");
        result.AppendLine("No automatic project modification has been performed.");
        return result.ToString();
    }

    private static IEnumerable<Type?> GetTypesSafely(Assembly assembly)
    {
        try { return assembly.GetTypes(); }
        catch (ReflectionTypeLoadException ex) { return ex.Types; }
        catch { return Array.Empty<Type>(); }
    }

    private static void ExtractBounds(Document doc, ObjectId[] selected, StringBuilder output)
    {
        int count = 0, missing = 0;
        Extents3d? bounds = null;
        using Transaction tx = doc.Database.TransactionManager.StartTransaction();
        foreach (ObjectId id in selected.Distinct())
        {
            try
            {
                if (tx.GetObject(id, OpenMode.ForRead, false) is not Entity entity)
                    continue;
                count++;
                try
                {
                    Extents3d extent = entity.GeometricExtents;
                    if (bounds.HasValue)
                    {
                        Extents3d union = bounds.Value;
                        union.AddExtents(extent);
                        bounds = union;
                    }
                    else bounds = extent;
                }
                catch { missing++; }
            }
            catch { missing++; }
        }

        output.AppendLine("== SELECTED SUPPORT ENVELOPE ==");
        output.AppendLine("Selected entity count: " + count);
        output.AppendLine("Entities missing extents: " + missing);
        if (!bounds.HasValue)
        {
            output.AppendLine("No bounding box available; OrthoCube cannot be sized automatically.");
            return;
        }

        Extents3d b = bounds.Value;
        Vector3d size = b.MaxPoint - b.MinPoint;
        double largest = Math.Max(size.X, Math.Max(size.Y, size.Z));
        double margin = Math.Max(0.001, largest * 0.10);
        Point3d min = new Point3d(b.MinPoint.X - margin,
            b.MinPoint.Y - margin, b.MinPoint.Z - margin);
        Point3d max = new Point3d(b.MaxPoint.X + margin,
            b.MaxPoint.Y + margin, b.MaxPoint.Z + margin);
        string F(Point3d p) => string.Format(CultureInfo.InvariantCulture,
            "({0:0.###},{1:0.###},{2:0.###})", p.X, p.Y, p.Z);
        output.AppendLine("Model min: " + F(b.MinPoint));
        output.AppendLine("Model max: " + F(b.MaxPoint));
        output.AppendLine("Suggested OrthoCube min (+10% longest-span clearance): " + F(min));
        output.AppendLine("Suggested OrthoCube max (+10% longest-span clearance): " + F(max));
        output.AppendLine("Suggested center: " + F(new Point3d(
            (min.X + max.X) / 2, (min.Y + max.Y) / 2, (min.Z + max.Z) / 2)));
        output.AppendLine("Suggested view directions: TOP (0,0,1); FRONT (0,-1,0); SIDE (1,0,0)");
        output.AppendLine("These are proposed coordinates only; no OrthoCube was altered.");
    }
}

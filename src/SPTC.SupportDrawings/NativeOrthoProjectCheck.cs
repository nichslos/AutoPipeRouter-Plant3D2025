using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace SPTC.SupportDrawings;

/// <summary>
/// REV12 read-only Plant 3D 2025 source registration check, using
/// OrthoDocUtils.IsValid3dDrawing when the installed Ortho API exposes it.
/// Never adds, renames, or writes a project drawing.
/// </summary>
internal static class NativeOrthoProjectCheck
{
    internal sealed record Result(string ProjectSummary, string OrthoProjectPart,
        bool SourceRegistrationVerified, string RegistrationStatus,
        IReadOnlyList<string> ExistingOrthoDrawingNames,
        string ValidationProjectPart, string ValidationApiSignature);

    internal static Result Inspect(string sourceDwg)
    {
        const BindingFlags publicStatic = BindingFlags.Public | BindingFlags.Static;
        var existing = new List<string>();

        try
        {
            Assembly[] loaded = AppDomain.CurrentDomain.GetAssemblies();
            Type? plant = FindType(loaded, "Autodesk.ProcessPower.PlantInstance.PlantApplication");
            if (plant == null)
                return Fail("PlantApplication API not loaded; cannot verify project.");
            object? project = plant.GetProperty("CurrentProject", publicStatic)?.GetValue(null);
            if (project == null)
                return Fail("No active Plant 3D project. Open the TOSHIBA project in Project Manager.");

            Type projectType = project.GetType();
            string name = GetPublicProperty(project, "ProjectName") ??
                GetPublicProperty(project, "Name") ?? "(name unavailable)";
            string folder = GetPublicProperty(project, "ProjectFolderPath") ?? "(path unavailable)";
            string projectSummary = name + " | " + folder;

            object? parts = projectType.GetProperty("ProjectParts")?.GetValue(project);
            object? ortho = null;
            object? piping = null;
            string orthoType = "(not found)";
            string pipingType = "(not found)";
            if (parts != null)
            {
                try
                {
                    PropertyInfo? item = parts.GetType().GetProperty("Item");
                    if (item != null)
                    {
                        try { ortho = item.GetValue(parts, new object[] { "Ortho" }); }
                        catch (System.Exception e) { orthoType = "Ortho lookup: " + e.GetType().Name; }
                        try { piping = item.GetValue(parts, new object[] { "Piping" }); }
                        catch (System.Exception e) { pipingType = "Piping lookup: " + e.GetType().Name; }
                    }
                    if (ortho != null) orthoType = ortho.GetType().FullName ?? "(unknown)";
                    if (piping != null) pipingType = piping.GetType().FullName ?? "(unknown)";
                }
                catch (System.Exception e)
                {
                    orthoType = "ProjectParts API unavailable: " + e.GetType().Name;
                    pipingType = orthoType;
                }
            }

            if (ortho != null)
            {
                try
                {
                    MethodInfo? getDrawings = ortho.GetType().GetMethod("GetPnPDrawingFiles",
                        BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null);
                    if (getDrawings?.Invoke(ortho, null) is IEnumerable drawings)
                    {
                        foreach (object? item in drawings)
                        {
                            if (item == null || existing.Count >= 100) break;
                            string filename = GetPublicProperty(item, "FileName") ??
                                GetPublicProperty(item, "Name") ?? item.ToString() ?? "(unknown)";
                            existing.Add(filename);
                        }
                    }
                }
                catch (System.Exception e) { existing.Add("Drawing listing failed: " + e.GetType().Name); }
            }

            Type? docUtils = FindType(loaded, "Autodesk.ProcessPower.P3dProjectParts.OrthoDocUtils");
            if (docUtils == null)
                return new Result(projectSummary, orthoType, false,
                    "OrthoDocUtils is not loaded. Open and cancel PLANTORTHOCREATE, then retry.",
                    existing, pipingType, "(unavailable)");

            // The actual user-supplied .NET 8 Ortho SDK shows that
            // PlantApplication.CurrentProject is a PlantProject (NOT Project).
            // OrthoDocUtils.IsValid3dDrawing requires a P3d ProjectPart Project,
            // specifically the Piping project part for 3D model registration.
            MethodInfo[] named = docUtils.GetMethods(publicStatic).Where(m =>
                m.Name == "IsValid3dDrawing").ToArray();
            MethodInfo? validation = named.FirstOrDefault(m =>
            {
                if (m.ReturnType != typeof(bool)) return false;
                ParameterInfo[] args = m.GetParameters();
                return args.Length == 3 &&
                    args[0].ParameterType == typeof(string) &&
                    args[2].ParameterType == typeof(string).MakeByRefType();
            });
            string available = named.Length == 0 ? "(not exposed)" :
                string.Join(" | ", named.Select(m => m.ToString()));

            if (validation == null)
                return new Result(projectSummary, orthoType, false,
                    "IsValid3dDrawing signature unavailable. Discovered: " + available,
                    existing, pipingType, available);

            ParameterInfo[] signature = validation.GetParameters();
            if (piping == null)
                return new Result(projectSummary, orthoType, false,
                    "Cannot validate a 3D model without the Piping project part; current PlantProject is not the required Project type.",
                    existing, pipingType, available);

            Type expected = signature[1].ParameterType;
            if (!expected.IsInstanceOfType(piping))
                return new Result(projectSummary, orthoType, false,
                    "Piping project part does not match native API. Required=" +
                    expected.FullName + "; actual=" + piping.GetType().FullName + ".",
                    existing, pipingType, available);

            // Actual native method is read-only; do not add, rename or modify DWGs.
            object?[] parameters = {sourceDwg, piping, ""};
            object? value = validation.Invoke(null, parameters);
            bool verified = value is bool success && success;
            string detail = parameters[2]?.ToString() ?? "";
            string status = (verified ?
                "VALID: native IsValid3dDrawing accepted the source file using the Piping project part." :
                "NOT VALIDATED: native IsValid3dDrawing returned false using the Piping project part.") +
                (string.IsNullOrWhiteSpace(detail) ? "" : " API reason: " + detail);
            return new Result(projectSummary, orthoType, verified, status,
                existing, pipingType, available);
        }
        catch (TargetInvocationException e)
        {
            return Fail("Ortho source validation failed: " +
                (e.InnerException?.Message ?? e.Message));
        }
        catch (System.Exception e) { return Fail("Ortho project read failed: " + e.Message); }
    }

    private static Type? FindType(IEnumerable<Assembly> assemblies, string fullName)
    {
        foreach (Assembly assembly in assemblies)
        {
            try
            {
                Type? result = assembly.GetType(fullName, false);
                if (result != null) return result;
            }
            catch { /* Ignore assembly type load errors, report missing API. */ }
        }
        return null;
    }

    private static string? GetPublicProperty(object item, string name)
    {
        try
        {
            PropertyInfo? property = item.GetType().GetProperty(name,
                BindingFlags.Public | BindingFlags.Instance);
            if (property == null || property.GetIndexParameters().Length > 0) return null;
            return property.GetValue(item)?.ToString();
        }
        catch { return null; }
    }

    private static Result Fail(string reason) =>
        new("(project unavailable)", "(Ortho project part unavailable)",
            false, reason, Array.Empty<string>(),
            "(Piping project part unavailable)", "(native signature not inspected)");
}

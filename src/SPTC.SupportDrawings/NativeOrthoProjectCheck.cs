using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace SPTC.SupportDrawings;

/// <summary>
/// REV11 read-only Plant 3D 2025 source registration check, using
/// OrthoDocUtils.IsValid3dDrawing when the installed Ortho API exposes it.
/// Never adds, renames, or writes a project drawing.
/// </summary>
internal static class NativeOrthoProjectCheck
{
    internal sealed record Result(string ProjectSummary, string OrthoProjectPart,
        bool SourceRegistrationVerified, string RegistrationStatus,
        IReadOnlyList<string> ExistingOrthoDrawingNames);

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
            string orthoType = "(not found)";
            if (parts != null)
            {
                try
                {
                    PropertyInfo? item = parts.GetType().GetProperty("Item");
                    ortho = item?.GetValue(parts, new object[] { "Ortho" });
                    orthoType = ortho?.GetType().FullName ?? "(not found)";
                }
                catch (System.Exception e) { orthoType = "Ortho project part unavailable: " + e.GetType().Name; }
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
                    "OrthoDocUtils not loaded. Run normal PLANTORTHOCREATE once, cancel, then retry.", existing);

            MethodInfo? validation = docUtils.GetMethods(publicStatic).FirstOrDefault(m =>
            {
                if (m.Name != "IsValid3dDrawing" || m.ReturnType != typeof(bool)) return false;
                ParameterInfo[] args = m.GetParameters();
                return args.Length == 3 &&
                    args[0].ParameterType == typeof(string) &&
                    args[1].ParameterType.IsInstanceOfType(project) &&
                    args[2].ParameterType.IsByRef;
            });
            if (validation == null)
                return new Result(projectSummary, orthoType, false,
                    "Read-only IsValid3dDrawing(string, Project, out string) API unavailable/incompatible.", existing);

            object?[] parameters = {sourceDwg, project, ""};
            object? value = validation.Invoke(null, parameters);
            bool verified = value is bool b && b;
            string detail = parameters[2]?.ToString() ?? "";
            string status = (verified ? "VALID registered 3D source drawing." :
                "NOT VALIDATED as an eligible registered 3D source drawing.") +
                (string.IsNullOrWhiteSpace(detail) ? "" : " API reason: " + detail);
            return new Result(projectSummary, orthoType, verified, status, existing);
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
            false, reason, Array.Empty<string>());
}

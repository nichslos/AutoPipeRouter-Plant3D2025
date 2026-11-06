using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SPTC.SupportDrawings;

/// <summary>
/// REV14 model-agnostic test DWG naming and destination guards.
/// Does not mutate any Autodesk file or Plant 3D project.
/// </summary>
public static class NativeOrthoRegistrationNames
{
    public static string NewOrthoDrawingFileName(string sourceDwg,
        IEnumerable<string> selectedHandles, string uniqueSuffix)
    {
        if (string.IsNullOrWhiteSpace(sourceDwg))
            throw new ArgumentException("Active source drawing is required.", nameof(sourceDwg));
        if (selectedHandles == null) throw new ArgumentNullException(nameof(selectedHandles));
        if (string.IsNullOrWhiteSpace(uniqueSuffix))
            throw new ArgumentException("Unique suffix is required.", nameof(uniqueSuffix));
        string baseName = Path.GetFileNameWithoutExtension(sourceDwg);
        string firstHandle = selectedHandles.FirstOrDefault(h => !string.IsNullOrWhiteSpace(h))
            ?? throw new ArgumentException("At least one support object must be selected.", nameof(selectedHandles));
        return "SPTC_" + Sanitize(baseName, 44) + "_" + Sanitize(firstHandle, 16) +
            "_ORTHO_R14_" + Sanitize(uniqueSuffix, 20) + ".dwg";
    }

    public static bool IsInsideProjectDirectory(string path, string projectRoot)
    {
        if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(projectRoot)) return false;
        try
        {
            string actualRoot = Path.GetFullPath(projectRoot);
            string actualPath = Path.GetFullPath(path);
            string relative = Path.GetRelativePath(actualRoot, actualPath);
            return relative.Length > 0 && !Path.IsPathRooted(relative) &&
                relative != "." && relative != ".." &&
                !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) &&
                !relative.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal);
        }
        catch { return false; }
    }

    private static string Sanitize(string name, int maxLength)
    {
        string cleaned = new string(name.Select(c =>
            char.IsLetterOrDigit(c) || c == '_' || c == '-' ? c : '_').ToArray()).Trim('_');
        if (string.IsNullOrWhiteSpace(cleaned)) return "UNNAMED";
        return cleaned.Length <= maxLength ? cleaned : cleaned[..maxLength];
    }
}

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Autodesk.AutoCAD.ApplicationServices;

namespace SPTC.ProductionIso;

// REV13: read-only folder/line metadata from the actual Plant Piping project.
// Never enumerate arbitrary network DWGs or open drawings, and never write to PnP.
internal static class ProjectFolderLineCatalog
{
    public sealed record FolderChoice(string Key, string Caption, int DrawingCount);
    public sealed record FolderLines(string Folder, IReadOnlyList<string> Tags, int DrawingCount, string Note);
    private sealed record Drawing(string RelativePath, string Folder, int DrawingId);

    private static readonly List<Drawing> Registered = new();
    private static readonly Dictionary<string, FolderLines> CachedLines = new(StringComparer.OrdinalIgnoreCase);
    private static object? _dataLinks;
    private static object? _pnpDatabase;
    private static string _projectRoot = "";
    private static string _diagnostic = "Not initialized";
    private static bool _ready;

    public static string Diagnostic => _diagnostic;
    public static bool Ready => _ready;

    public static IReadOnlyList<FolderChoice> Folders
    {
        get
        {
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Drawing drawing in Registered)
            {
                string folder = drawing.Folder;
                if (string.IsNullOrWhiteSpace(folder)) continue;
                // Include parent folders so their child DWGs can be filtered too.
                while (!string.IsNullOrEmpty(folder))
                {
                    paths.Add(folder);
                    int end = folder.LastIndexOf('\\');
                    folder = end < 0 ? "" : folder[..end];
                }
            }
            return paths.OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                .Select(p => new FolderChoice(p, p.Replace('\\', ' '),
                    Registered.Count(d => IsWithin(d.Folder, p)))).ToList();
        }
    }

    public static void Reset()
    {
        Registered.Clear();
        CachedLines.Clear();
        _dataLinks = null;
        _pnpDatabase = null;
        _projectRoot = "";
        _diagnostic = "Not initialized";
        _ready = false;
    }

    public static void Initialize(Document doc)
    {
        Reset();
        try
        {
            Type? plantApp = FindPlantType("Autodesk.ProcessPower.PlantInstance.PlantApplication");
            object? plantProject = plantApp?.GetProperty("CurrentProject",
                BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
            object? parts = GetProperty(plantProject, "ProjectParts");
            object? piping = GetIndexed(parts, "Piping");
            _dataLinks = GetProperty(piping, "DataLinksManager");
            _pnpDatabase = Invoke(_dataLinks, "GetPnPDatabase");
            _projectRoot = Convert.ToString(GetProperty(plantProject, "ProjectFolderPath")) ?? "";

            if (piping is null || _dataLinks is null || _pnpDatabase is null)
                throw new InvalidOperationException("Could not open the current project's Piping data links.");

            // GetPnPDrawingFiles returns only registered project drawings.
            object? fileList = Invoke(piping, "GetPnPDrawingFiles");
            if (fileList is not IEnumerable files)
                throw new InvalidOperationException("Piping.GetPnPDrawingFiles is not available.");

            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (object? file in files)
            {
                string raw = Convert.ToString(GetProperty(file, "RelativeFilePath")) ?? "";
                if (string.IsNullOrWhiteSpace(raw))
                    raw = Convert.ToString(GetProperty(file, "ResolvedFilePath")) ?? "";
                string relative = NormalizeProjectPath(raw);
                if (!relative.EndsWith(".dwg", StringComparison.OrdinalIgnoreCase)) continue;
                if (!paths.Add(relative)) continue;
                if (paths.Count > 3000)
                    throw new InvalidOperationException("More than 3,000 registered DWGs; folder picker disabled to protect responsiveness.");
            }
            if (paths.Count == 0)
                throw new InvalidOperationException("No registered Plant 3D drawing paths were found.");

            // PnPDrawings contains the project-registered DWG IDs, not extra
            // orphan files on the mapped network drive.
            object? table = GetIndexed(GetProperty(_pnpDatabase, "Tables"), "PnPDrawings");
            if (table is null)
                throw new InvalidOperationException("Piping project has no PnPDrawings table.");

            var rows = ReadRows(table, "PnPID > 0").ToList();
            if (rows.Count > 6000)
                throw new InvalidOperationException("PnPDrawings exceeds safe indexing limit.");

            var databasePaths = new List<(string Path, int Id)>();
            foreach (object row in rows)
            {
                int id = ReadInt(row, "PnPID");
                if (id <= 0) continue;
                string path = ReadCell(row, "PnPRelativePath");
                if (!path.EndsWith(".dwg", StringComparison.OrdinalIgnoreCase))
                {
                    string name = ReadCell(row, "DwgName");
                    if (string.IsNullOrEmpty(name)) name = ReadCell(row, "PnPName");
                    if (string.IsNullOrEmpty(name)) name = ReadCell(row, "Name");
                    if (name.EndsWith(".dwg", StringComparison.OrdinalIgnoreCase))
                        path = Path.Combine(path, name);
                }
                string normalized = NormalizeProjectPath(path);
                if (normalized.EndsWith(".dwg", StringComparison.OrdinalIgnoreCase))
                    databasePaths.Add((normalized, id));
            }

            foreach (string relative in paths)
            {
                var matches = databasePaths.Where(d =>
                    d.Path.Equals(relative, StringComparison.OrdinalIgnoreCase) ||
                    d.Path.EndsWith("\\" + relative, StringComparison.OrdinalIgnoreCase) ||
                    relative.EndsWith("\\" + d.Path, StringComparison.OrdinalIgnoreCase)).ToList();
                // Never assign a drawing ID if the path resolution is ambiguous.
                if (matches.Count != 1) continue;
                string folder = FolderOf(relative);
                Registered.Add(new Drawing(relative, folder, matches[0].Id));
            }

            _ready = Registered.Count > 0;
            _diagnostic = _ready
                ? $"Indexed {Registered.Count} registered Plant 3D drawings in {Folders.Count} folders."
                : "Folder names could not be matched to unique PnPDrawings IDs; no filtering applied.";
            doc.Editor.WriteMessage("\nSPTC REV13: " + _diagnostic);
        }
        catch (Exception ex)
        {
            _ready = false;
            _diagnostic = "Folder index unavailable: " + ex.Message;
            doc.Editor.WriteMessage("\nSPTC REV13: " + _diagnostic);
        }
    }

    public static FolderLines GetLiveTags(string folder)
    {
        if (!_ready || _dataLinks is null || _pnpDatabase is null)
            return new FolderLines(folder, Array.Empty<string>(), 0, _diagnostic);

        if (CachedLines.TryGetValue(folder, out FolderLines? cached))
            return cached;

        var drawings = Registered.Where(d => IsWithin(d.Folder, folder)).ToList();
        if (drawings.Count > 120)
            return new FolderLines(folder, Array.Empty<string>(), drawings.Count,
                "Folder has more than 120 DWGs; automatic list filtering is disabled to avoid UI lag.");

        var tags = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            object? linksTable = GetIndexed(GetProperty(_pnpDatabase, "Tables"), "PnPDataLinks");
            if (linksTable is null)
                return new FolderLines(folder, Array.Empty<string>(), drawings.Count,
                    "PnPDataLinks table unavailable; cannot verify current drawing membership.");

            MethodInfo? related = _dataLinks.GetType().GetMethods()
                .FirstOrDefault(m => m.Name == "GetRelatedRowIds" && m.GetParameters().Length == 4);
            MethodInfo? properties = _dataLinks.GetType().GetMethods()
                .FirstOrDefault(m => m.Name == "GetAllProperties" && m.GetParameters().Length == 2);
            if (related is null || properties is null)
                return new FolderLines(folder, Array.Empty<string>(), drawings.Count,
                    "Required Piping line-group relationship methods unavailable.");

            int rowBudget = 10000;
            foreach (Drawing drawing in drawings)
            {
                // Verify that a line group has at least one LIVE part in THIS DWG,
                // not merely an old project-wide/renamed line-group record.
                var livePartIds = new HashSet<int>();
                foreach (object row in ReadRows(linksTable, "DwgId=" + drawing.DrawingId))
                {
                    if (--rowBudget <= 0)
                        throw new InvalidOperationException("Piping data exceeds safe 10,000-record read limit.");
                    int partRow = ReadInt(row, "RowId");
                    if (partRow > 0) livePartIds.Add(partRow);
                }
                if (livePartIds.Count == 0) continue;

                object? groups = related.Invoke(_dataLinks,
                    new object[] { "P3dDrawingLineGroupRelationship", "Drawing", drawing.DrawingId, "LineGroup" });
                if (groups is not IEnumerable groupIds) continue;
                foreach (object? groupObj in groupIds)
                {
                    if (groupObj is null) continue;
                    int groupId = Convert.ToInt32(groupObj);
                    if (groupId <= 0) continue;
                    object? members = related.Invoke(_dataLinks,
                        new object[] { "P3dLineGroupPartRelationship", "LineGroup", groupId, "Part" });
                    if (members is not IEnumerable memberIds) continue;

                    bool hasLivePart = false;
                    foreach (object? member in memberIds)
                    {
                        if (--rowBudget <= 0)
                            throw new InvalidOperationException("Piping data exceeds safe 10,000-record read limit.");
                        if (member != null && livePartIds.Contains(Convert.ToInt32(member)))
                        {
                            hasLivePart = true;
                            break;
                        }
                    }
                    if (!hasLivePart) continue;

                    object? props = properties.Invoke(_dataLinks, new object[] { groupId, false });
                    string tag = ReadKeyValue(props, "Tag");
                    if (!string.IsNullOrWhiteSpace(tag) &&
                        !tag.Equals("Unassigned", StringComparison.OrdinalIgnoreCase))
                        tags.Add(tag.Trim());
                }
            }

            var result = new FolderLines(folder, tags.ToArray(), drawings.Count,
                $"Verified {tags.Count} live line tag(s) in {drawings.Count} registered DWG(s).");
            CachedLines[folder] = result;
            return result;
        }
        catch (Exception ex)
        {
            return new FolderLines(folder, Array.Empty<string>(), drawings.Count,
                "Unable to verify live lines: " + ex.GetBaseException().Message);
        }
    }

    private static bool IsWithin(string candidate, string folder) =>
        candidate.Equals(folder, StringComparison.OrdinalIgnoreCase) ||
        candidate.StartsWith(folder + "\\", StringComparison.OrdinalIgnoreCase);

    private static string NormalizeProjectPath(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return "";
        string s = input.Replace('/', '\\').Trim().Trim('"');
        try
        {
            if (Path.IsPathRooted(s) && !string.IsNullOrWhiteSpace(_projectRoot))
                s = Path.GetRelativePath(_projectRoot, s);
        }
        catch { }
        while (s.StartsWith(".\\", StringComparison.Ordinal)) s = s[2..];
        return s.TrimStart('\\');
    }

    private static string FolderOf(string path)
    {
        var segments = path.Split('\\', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length < 2) return "";
        int root = Array.FindIndex(segments, x =>
            x.Equals("Plant 3D Models", StringComparison.OrdinalIgnoreCase) ||
            x.Equals("Plant 3D Drawings", StringComparison.OrdinalIgnoreCase));
        int first = root < 0 ? 0 : root + 1;
        int count = segments.Length - first - 1;
        if (count <= 0) return "";
        return string.Join("\\", segments.Skip(first).Take(count));
    }

    private static Type? FindPlantType(string name)
    {
        foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type? match = assembly.GetType(name, false);
            if (match != null) return match;
        }
        try { return Assembly.Load("PnPProjectManagerMgd").GetType(name, false); }
        catch { return null; }
    }

    private static object? GetProperty(object? item, string name)
    {
        if (item is null) return null;
        return item.GetType().GetProperty(name)?.GetValue(item);
    }

    private static object? GetIndexed(object? container, string key)
    {
        if (container is null) return null;
        var prop = container.GetType().GetProperties()
            .FirstOrDefault(p => p.Name == "Item" &&
                p.GetIndexParameters().Length == 1 &&
                p.GetIndexParameters()[0].ParameterType == typeof(string));
        return prop?.GetValue(container, new object[] { key });
    }

    private static object? Invoke(object? item, string name)
    {
        if (item is null) return null;
        return item.GetType().GetMethod(name, Type.EmptyTypes)?.Invoke(item, null);
    }

    private static IEnumerable<object> ReadRows(object table, string filter)
    {
        // PnPTable.Select(string) is available on standard Plant 3D databases.
        // Recheck DwgId on every returned row. An unfiltered fallback may NEVER
        // be interpreted as belonging to one selected drawing.
        var select = table.GetType().GetMethod("Select", new[] { typeof(string) });
        object? rows = select?.Invoke(table, new object[] { filter });
        rows ??= GetProperty(table, "Rows");
        if (rows is not IEnumerable sequence)
            yield break;

        int requestedDwg = -1;
        if (filter.StartsWith("DwgId=", StringComparison.OrdinalIgnoreCase))
            int.TryParse(filter["DwgId=".Length..], out requestedDwg);

        foreach (object? row in sequence)
        {
            if (row == null) continue;
            if (requestedDwg > 0 && ReadInt(row, "DwgId") != requestedDwg)
                continue;
            if (filter.Contains("PnPID > 0", StringComparison.OrdinalIgnoreCase)
                && ReadInt(row, "PnPID") <= 0)
                continue;
            yield return row;
        }
    }

    private static string ReadCell(object row, string column)
    {
        try
        {
            var item = row.GetType().GetProperties()
                .FirstOrDefault(p => p.Name == "Item" &&
                    p.GetIndexParameters().Length == 1 &&
                    p.GetIndexParameters()[0].ParameterType == typeof(string));
            return Convert.ToString(item?.GetValue(row, new object[] { column })) ?? "";
        }
        catch { return ""; }
    }

    private static int ReadInt(object row, string column)
    {
        string value = ReadCell(row, column);
        return int.TryParse(value, out int id) ? id : -1;
    }

    private static string ReadKeyValue(object? values, string key)
    {
        if (values is not IEnumerable collection) return "";
        foreach (object? value in collection)
        {
            if (value is null) continue;
            object? name = GetProperty(value, "Key");
            if (!string.Equals(Convert.ToString(name), key, StringComparison.OrdinalIgnoreCase))
                continue;
            return Convert.ToString(GetProperty(value, "Value")) ?? "";
        }
        return "";
    }
}

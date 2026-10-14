using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Dawn;
using Dawn.Utils;

namespace Operator.Cassette;

internal static class CassetteLibrary
{
    public sealed class Entry
    {
        public string Id = string.Empty;
        public string FileName = string.Empty;
        public string Timestamp = string.Empty;
        public float Duration;
        public int ScrapValue;
        public string Breakdown = string.Empty;
        public DateTime SavedAt;
        public string Folder = string.Empty;
    }

    private static readonly Dictionary<string, Entry> _entries = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, string> _mergedRedirects = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, (string Merged, UnityEngine.Object Owner)> _pendingMerges = new(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> _droppedMerges = new(StringComparer.OrdinalIgnoreCase);
    private static readonly NamespacedKey LibraryKey =
        NamespacedKey.From(Constants.PluginMetadata.ContentNamespace, "cassette_library");
    private static readonly NamespacedKey RedirectsKey =
        NamespacedKey.From(Constants.PluginMetadata.ContentNamespace, "cassette_merged_redirects");
    private static bool _loaded;

    public static void RegisterMergedRedirect(string segmentFileName, string mergedFileName)
    {
        if (string.IsNullOrWhiteSpace(segmentFileName) || string.IsNullOrWhiteSpace(mergedFileName)) return;
        if (string.Equals(segmentFileName, mergedFileName, StringComparison.OrdinalIgnoreCase)) return;
        if (_droppedMerges.Contains(mergedFileName)) return;

        var stale = new List<string>();
        foreach (KeyValuePair<string, string> pair in _mergedRedirects)
        {
            if (string.Equals(pair.Value, segmentFileName, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(pair.Key, mergedFileName, StringComparison.OrdinalIgnoreCase))
            {
                stale.Add(pair.Key);
            }
        }
        foreach (string key in stale)
        {
            _mergedRedirects[key] = mergedFileName;
        }

        _mergedRedirects[segmentFileName] = mergedFileName;
        FlushRedirects();
    }

    public static string GetMergedName(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName)) return fileName;
        if (_mergedRedirects.TryGetValue(fileName, out string merged)) return merged;
        return TryGetPendingMerge(fileName, out string pending) ? pending : fileName;
    }

    public static void AddPendingMerge(string segmentFileName, string mergedFileName, UnityEngine.Object owner)
    {
        if (string.IsNullOrWhiteSpace(segmentFileName) || string.IsNullOrWhiteSpace(mergedFileName)) return;
        if (string.Equals(segmentFileName, mergedFileName, StringComparison.OrdinalIgnoreCase)) return;
        _pendingMerges[segmentFileName] = (mergedFileName, owner);
    }

    public static void ClearPendingMerge(string mergedFileName)
    {
        var done = new List<string>();
        foreach (KeyValuePair<string, (string Merged, UnityEngine.Object Owner)> pair in _pendingMerges)
        {
            if (string.Equals(pair.Value.Merged, mergedFileName, StringComparison.OrdinalIgnoreCase)) done.Add(pair.Key);
        }
        foreach (string key in done) _pendingMerges.Remove(key);
    }

    public static void DropMergedFilm(string mergedFileName)
    {
        if (string.IsNullOrWhiteSpace(mergedFileName)) return;
        _droppedMerges.Add(mergedFileName);
        ClearPendingMerge(mergedFileName);

        var redirected = new List<string>();
        foreach (KeyValuePair<string, string> pair in _mergedRedirects)
        {
            if (string.Equals(pair.Value, mergedFileName, StringComparison.OrdinalIgnoreCase)) redirected.Add(pair.Key);
        }
        if (redirected.Count == 0) return;
        foreach (string key in redirected) _mergedRedirects.Remove(key);
        FlushRedirects();
    }

    public static bool IsMergePending(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName)) return false;
        foreach (KeyValuePair<string, (string Merged, UnityEngine.Object Owner)> pair in _pendingMerges)
        {
            if (pair.Value.Owner == null) continue;
            if (string.Equals(pair.Key, fileName, StringComparison.OrdinalIgnoreCase)
                || string.Equals(pair.Value.Merged, fileName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    private static bool TryGetPendingMerge(string fileName, out string merged)
    {
        merged = fileName;
        if (!_pendingMerges.TryGetValue(fileName, out var pending)) return false;
        if (pending.Owner == null)
        {
            _pendingMerges.Remove(fileName);
            return false;
        }
        merged = pending.Merged;
        return true;
    }

    private static void FlushRedirects()
    {
        try
        {
            var snapshot = new Dictionary<string, string>(_mergedRedirects, StringComparer.OrdinalIgnoreCase);
            Plugin.Instance.GetPersistentDataContainer().Set(RedirectsKey, snapshot);
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError($"Failed to save merged recording redirects: {ex}");
        }
    }

    public static IReadOnlyDictionary<string, Entry> All => _entries;

    public static void Load()
    {
        if (_loaded) return;
        _loaded = true;

        try
        {
            var container = Plugin.Instance.GetPersistentDataContainer();
            if (container.TryGet(LibraryKey, out Dictionary<string, Entry>? stored) && stored != null)
            {
                foreach (var kv in stored)
                {
                    if (kv.Value != null && !string.IsNullOrWhiteSpace(kv.Value.Id))
                    {
                        _entries[kv.Value.Id.ToLowerInvariant()] = kv.Value;
                    }
                }
            }

            if (container.TryGet(RedirectsKey, out Dictionary<string, string>? storedRedirects) && storedRedirects != null)
            {
                foreach (var kv in storedRedirects)
                {
                    if (!string.IsNullOrWhiteSpace(kv.Key) && !string.IsNullOrWhiteSpace(kv.Value))
                    {
                        _mergedRedirects[kv.Key] = kv.Value;
                    }
                }
            }

            CheckFiles();
            Plugin.Log.LogInfo($"Cassette library loaded {_entries.Count} entries from DawnLib store.");

            foreach (var kv in _entries)
            {
                TerminalCassettePlayback.RegisterCassetteId(kv.Value.Id);
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError($"Failed to load cassette library: {ex}");
        }
    }

    private static void CheckFiles()
    {
        if (_entries.Count == 0) return;

        try
        {
            string root = Recording.Recorder.GetOutputDirectory();
            if (!Directory.Exists(root)) return;

            string lobbies = Path.Combine(root, "Lobbies");
            string[] lobbyDirs = Directory.Exists(lobbies) ? Directory.GetDirectories(lobbies) : Array.Empty<string>();
            var missing = new List<Entry>();
            int moved = 0;
            bool changed = false;

            foreach (Entry entry in _entries.Values)
            {
                string fileName = GetMergedName(entry.FileName);
                if (!GetCandidatePaths(fileName, root).Any(File.Exists))
                {
                    string? stranded = Recording.ClipName.IsSafe(fileName)
                        ? lobbyDirs.Select(dir => Path.Combine(dir, fileName)).FirstOrDefault(File.Exists)
                        : null;
                    if (stranded == null)
                    {
                        if (string.IsNullOrEmpty(entry.Folder) || string.Equals(entry.Folder, root, StringComparison.OrdinalIgnoreCase)) missing.Add(entry);
                        continue;
                    }

                    try
                    {
                        File.Move(stranded, Path.Combine(root, fileName));
                        moved++;
                    }
                    catch (Exception ex)
                    {
                        Plugin.Log.LogWarning($"Failed to move '{fileName}' into the recordings folder: {ex.Message}");
                    }
                }

                if (string.Equals(entry.Folder, root, StringComparison.Ordinal)) continue;
                entry.Folder = root;
                changed = true;
            }

            if (moved > 0) Plugin.Log.LogInfo($"Moved {moved} saved recordings out of lobby folders.");
            if (missing.Count > 0 && missing.Count == _entries.Count)
            {
                Plugin.Log.LogWarning($"None of the {missing.Count} saved cassettes have a recording in '{root}'. Keeping them in case the folder moved.");
                return;
            }

            foreach (Entry entry in missing)
            {
                _entries.Remove(entry.Id.ToLowerInvariant());
                Plugin.Log.LogDebug($"Removed saved cassette '{entry.Id}', '{entry.FileName}' is not on disk.");
            }
            if (missing.Count > 0) Plugin.Log.LogInfo($"Removed {missing.Count} saved cassettes whose recording is gone.");
            if (missing.Count > 0 || changed) Flush();
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"Failed to check saved cassette recordings: {ex.Message}");
        }
    }

    public static bool HasEntryFor(string fileName)
    {
        return _entries.Values.Any(e => string.Equals(e.FileName, fileName, StringComparison.OrdinalIgnoreCase));
    }

    public static bool TrySave(Entry entry)
    {
        if (entry == null || string.IsNullOrWhiteSpace(entry.Id)) return false;
        entry.Id = entry.Id.ToLowerInvariant();
        entry.Folder = Recording.Recorder.GetOutputDirectory();
        if (entry.SavedAt == default) entry.SavedAt = DateTime.UtcNow;
        _entries[entry.Id] = entry;
        Flush();
        TerminalCassettePlayback.RegisterCassetteId(entry.Id);
        return true;
    }

    public static bool TryRemove(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return false;
        bool removed = _entries.Remove(id.ToLowerInvariant());
        if (removed) Flush();
        return removed;
    }

    public static void Flush()
    {
        try
        {
            var snapshot = new Dictionary<string, Entry>(_entries, StringComparer.OrdinalIgnoreCase);
            Plugin.Instance.GetPersistentDataContainer().Set(LibraryKey, snapshot);
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError($"Failed to save the cassette library: {ex}");
        }
    }

    public static string? FindLocalPath(Entry entry)
    {
        if (entry == null || string.IsNullOrWhiteSpace(entry.FileName)) return null;

        foreach (string path in GetCandidatePaths(entry.FileName))
        {
            if (File.Exists(path)) return path;
        }

        return null;
    }

    public static IEnumerable<string> GetCandidatePaths(string fileName, string? lobbyDir = null)
    {
        if (!Recording.ClipName.IsSafe(fileName)) yield break;
        if (Recording.Recorder.IsWriting(fileName)) yield break;

        string persistentDir = Recording.Recorder.GetOutputDirectory();
        string unsavedDir = Recording.Recorder.GetUnsavedOutputDirectory();
        lobbyDir ??= Recording.Recorder.GetLobbyOutputDirectory();

        yield return Path.Combine(persistentDir, fileName);
        yield return Path.Combine(lobbyDir, fileName);
        yield return Path.Combine(unsavedDir, fileName);
        yield return Path.Combine(persistentDir, "Received", fileName);
        yield return Path.Combine(unsavedDir, "Received", fileName);

        foreach (string p in FindPrefixedCopies(Path.Combine(persistentDir, "Received"), fileName))
        {
            yield return p;
        }
        foreach (string p in FindPrefixedCopies(Path.Combine(unsavedDir, "Received"), fileName))
        {
            yield return p;
        }
    }

    private static IEnumerable<string> FindPrefixedCopies(string receivedDir, string fileName)
    {
        string[] matches = Array.Empty<string>();
        try
        {
            if (Directory.Exists(receivedDir))
            {
                matches = Directory.GetFiles(receivedDir, "*_" + fileName);
            }
        }
        catch
        {
            matches = Array.Empty<string>();
        }

        foreach (string m in matches)
        {
            yield return m;
        }
    }

    public static bool TryMoveToSaved(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName)) return false;

        string persistent = Path.Combine(Recording.Recorder.GetOutputDirectory(), fileName);
        if (File.Exists(persistent)) return true;

        foreach (string src in new[]
        {
            Path.Combine(Recording.Recorder.GetLobbyOutputDirectory(), fileName),
            Path.Combine(Recording.Recorder.GetUnsavedOutputDirectory(), fileName),
        })
        {
            if (File.Exists(src))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(persistent)!);
                File.Move(src, persistent);
                return true;
            }
        }

        return false;
    }

    public static int DeleteRecordingFiles(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName)) return 0;
        if (!Recording.ClipName.IsSafe(fileName))
        {
            Plugin.Log.LogWarning("Refused to delete recording by unsafe name.");
            return 0;
        }

        int deleted = 0;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string path in GetCandidatePaths(fileName))
        {
            if (!seen.Add(path)) continue;

            try
            {
                if (!File.Exists(path)) continue;
                File.Delete(path);
                deleted++;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"Failed to delete '{path}': {ex.Message}");
            }
        }

        return deleted;
    }

    public static HashSet<string> GetReferencedFiles(bool includeLibrary)
    {
        var referenced = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var cassette in UnityEngine.Object.FindObjectsOfType<CassetteItem>())
        {
            if (cassette != null && !string.IsNullOrWhiteSpace(cassette.FileName))
            {
                referenced.Add(cassette.FileName);
            }
        }
        VideoCameraItem.AddPendingFiles(referenced);
        CameraPersistence.AddFiles(referenced);
        if (includeLibrary)
        {
            foreach (var kv in _entries)
            {
                if (!string.IsNullOrWhiteSpace(kv.Value.FileName)) referenced.Add(kv.Value.FileName);
            }
        }
        return referenced;
    }

    public static int ReconcileLobbyFolder()
    {
        string lobbyDir = Recording.Recorder.GetLobbyOutputDirectory();
        if (!Directory.Exists(lobbyDir)) return 0;

        string root = Recording.Recorder.GetOutputDirectory();
        if (SamePath(lobbyDir, root))
        {
            return 0;
        }

        HashSet<string> referenced = GetReferencedFiles(includeLibrary: true);
        if (referenced.Count == 0)
        {
            return 0;
        }

        int deleted = 0;
        try
        {
            foreach (string path in Directory.EnumerateFiles(lobbyDir, "*.mp4", SearchOption.TopDirectoryOnly))
            {
                if (referenced.Contains(Path.GetFileName(path))) continue;
                try { File.Delete(path); deleted++; }
                catch (Exception ex) { Plugin.Log.LogWarning($"Failed to delete orphaned recording '{path}': {ex.Message}"); }
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"Failed to clear lobby recordings folder: {ex}");
        }
        if (deleted > 0) Plugin.Log.LogInfo($"Deleted {deleted} orphaned recordings from the lobby folder.");
        return deleted;
    }

    private static bool SamePath(string left, string right)
    {
        try
        {
            string a = Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string b = Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return true;
        }
    }

    public static int PruneReceivedClip(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName)) return 0;
        if (VideoCameraConfig.KeepReceivedClips.Value) return 0;
        if (IsFileReferenced(fileName)) return 0;

        int deleted = DeleteReceivedCopies(fileName);
        if (deleted > 0) Plugin.Log.LogDebug($"Deleted {deleted} received copies of '{fileName}'.");
        return deleted;
    }

    public static int PruneAllReceived()
    {
        if (VideoCameraConfig.KeepReceivedClips.Value) return 0;

        HashSet<string> referenced = GetReferencedFiles(includeLibrary: true);
        int deleted = 0;
        foreach (string baseDir in new[] { Recording.Recorder.GetOutputDirectory(), Recording.Recorder.GetUnsavedOutputDirectory() })
        {
            string receivedDir = Path.Combine(baseDir, "Received");
            if (!Directory.Exists(receivedDir)) continue;
            string[] files;
            try { files = Directory.GetFiles(receivedDir, "*.mp4", SearchOption.TopDirectoryOnly); }
            catch { continue; }

            foreach (string path in files)
            {
                if (referenced.Contains(StripReceivedPrefix(Path.GetFileName(path)))) continue;
                try { File.Delete(path); deleted++; }
                catch (Exception ex) { Plugin.Log.LogWarning($"Failed to delete the received clip '{path}': {ex.Message}"); }
            }
        }
        if (deleted > 0) Plugin.Log.LogInfo($"Deleted {deleted} unreferenced received clips.");
        return deleted;
    }

    private static bool IsFileReferenced(string fileName)
    {
        return GetReferencedFiles(includeLibrary: true).Contains(fileName);
    }

    private static int DeleteReceivedCopies(string fileName)
    {
        int deleted = 0;
        foreach (string baseDir in new[] { Recording.Recorder.GetOutputDirectory(), Recording.Recorder.GetUnsavedOutputDirectory() })
        {
            string receivedDir = Path.Combine(baseDir, "Received");
            if (!Directory.Exists(receivedDir)) continue;

            string exact = Path.Combine(receivedDir, fileName);
            if (TryDeleteFile(exact)) deleted++;
            foreach (string path in FindPrefixedCopies(receivedDir, fileName))
            {
                if (TryDeleteFile(path)) deleted++;
            }
        }
        return deleted;
    }

    private static bool TryDeleteFile(string path)
    {
        try
        {
            if (!File.Exists(path)) return false;
            File.Delete(path);
            return true;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"Failed to delete the received clip '{path}': {ex.Message}");
            return false;
        }
    }

    private static string StripReceivedPrefix(string receivedFileName)
    {
        if (receivedFileName.StartsWith("client", StringComparison.OrdinalIgnoreCase))
        {
            int us = receivedFileName.IndexOf('_');
            if (us > 0 && us + 1 < receivedFileName.Length) return receivedFileName.Substring(us + 1);
        }
        return receivedFileName;
    }

    public static int CleanupUnsaved()
    {
        if (!VideoCameraConfig.KeepUnsavedPerLobby.Value)
        {
            return 0;
        }

        string unsavedDir = Recording.Recorder.GetUnsavedOutputDirectory();
        if (!Directory.Exists(unsavedDir))
        {
            return 0;
        }

        int deleted = 0;
        int failed = 0;

        try
        {
            foreach (string filePath in Directory.EnumerateFiles(unsavedDir, "*.mp4", SearchOption.AllDirectories))
            {
                if (Recording.Recorder.IsWriting(Path.GetFileName(filePath))) continue;

                try
                {
                    File.Delete(filePath);
                    deleted++;
                }
                catch (Exception ex)
                {
                    failed++;
                    Plugin.Log.LogWarning($"Failed to delete the unsaved recording '{filePath}': {ex.Message}");
                }
            }

            foreach (string dirPath in Directory.EnumerateDirectories(unsavedDir, "*", SearchOption.AllDirectories)
                                             .OrderByDescending(p => p.Length))
            {
                try
                {
                    if (!Directory.EnumerateFileSystemEntries(dirPath).Any())
                    {
                        Directory.Delete(dirPath);
                    }
                }
                catch
                {
                }
            }

            try
            {
                if (!Directory.EnumerateFileSystemEntries(unsavedDir).Any())
                {
                    Directory.Delete(unsavedDir);
                }
            }
            catch
            {
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"Failed to clear the unsaved recordings folder: {ex}");
        }

        if (deleted > 0 || failed > 0)
        {
            Plugin.Log.LogInfo($"Deleted {deleted} unsaved recordings, {failed} could not be removed.");
        }

        return deleted;
    }

    private static readonly Regex PartName = new(@"^(client\d+_)?VideoCamera_.+\.mp4\.part$", RegexOptions.IgnoreCase);

    public static int CleanupPartFiles(string reason)
    {
        string sweepRoot = Recording.Recorder.GetOutputDirectory();
        if (!Directory.Exists(sweepRoot))
        {
            return 0;
        }

        var folders = new List<string> { sweepRoot, Path.Combine(sweepRoot, Constants.Recording.UnsavedFolderName), Path.Combine(sweepRoot, "Received") };
        int deleted = 0;
        try
        {
            string lobbies = Path.Combine(sweepRoot, "Lobbies");
            if (Directory.Exists(lobbies))
            {
                folders.AddRange(Directory.GetDirectories(lobbies));
            }

            foreach (string folder in folders)
            {
                if (!Directory.Exists(folder)) continue;
                foreach (string filePath in Directory.EnumerateFiles(folder, "*.mp4.part", SearchOption.TopDirectoryOnly))
                {
                    if (!PartName.IsMatch(Path.GetFileName(filePath))) continue;
                    try
                    {
                        File.Delete(filePath);
                        deleted++;
                    }
                    catch (Exception ex)
                    {
                        Plugin.Log.LogDebug($"Failed to delete the leftover file '{Path.GetFileName(filePath)}': {ex.Message}");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.LogDebug($"Failed to sweep leftover part files: {ex.Message}");
        }

        if (deleted > 0)
        {
            Plugin.Log.LogDebug($"Removed {deleted} leftover part files on {reason}.");
        }
        return deleted;
    }
}

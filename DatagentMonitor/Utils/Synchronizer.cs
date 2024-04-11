using DatagentMonitor.FileSystem;
using DatagentShared;
using Microsoft.Data.Sqlite;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DatagentMonitor.Utils;

internal class Synchronizer
{
    private readonly SynchronizationSourceManager _sourceManager;
    private SynchronizationSourceManager _targetManager;

    public Synchronizer(SynchronizationSourceManager manager)
    {
        _sourceManager = manager;
    }

    public void Run(string targetRoot)
    {
        _targetManager = new SynchronizationSourceManager(targetRoot);

        Console.Write($"Resolving target changes... ");
        var targetDelta = GetTargetDelta(targetRoot);
        Console.WriteLine($"Total: {targetDelta.Count}");

        Console.Write($"Resolving source changes... ");
        var sourceDelta = GetSourceDelta();
        Console.WriteLine($"Total: {sourceDelta.Count}");

        var appliedDelta = new List<FileSystemEntryChange>();
        var failedDelta = new List<FileSystemEntryChange>();
        Console.Write("Target latest sync timestamp: ");
        var lastSyncTimestamp = GetTargetLastSyncTimestamp(_targetManager.EventsDatabase);
        Console.WriteLine(lastSyncTimestamp != null ? lastSyncTimestamp.Value.ToString(CustomFileInfo.DateTimeFormat) : "N/A");

        void TryApplyChange(string sourceRoot, string targetRoot, FileSystemEntryChange change)
        {
            // TODO: use database instead of in-memory dicts
            var status = ApplyChange(sourceRoot, targetRoot, change);
            if (status)
                appliedDelta!.Add(change);
            else
                failedDelta!.Add(change);
            Console.WriteLine($"Status: {(status ? "applied" : "failed")}");
        }

        // Both source and target deltas have to be enumerated in an insertion order;
        // otherwise Created directory contents can get scheduled before that directory creation.
        // 
        // targetDelta is sorted by default, as it is a List.
        foreach (var targetChange in targetDelta)
        {
            if (sourceDelta.Remove(targetChange.Path, out var sourceChange))
            {
                // Entry change is present on both source and target
                // -> determine which of two changes is actual
                Console.Write($"Common: {targetChange.Path}; Strategy: ");
                if (lastSyncTimestamp == null || sourceChange.Timestamp >= lastSyncTimestamp)
                {
                    // TODO:
                    // If lastSyncTimestamp == null
                    // -> target does not contain any info of last sync
                    // -> it is impossible to determine which changes are actual and which are outdated
                    // -> only 2 options, akin to Git:
                    //      1. Employ a specific strategy (ours, theirs, etc.)
                    //      2. Perform a manual merge
                    //
                    // This also holds if both timestamps are equal (which is highly unlikely, but not improbable).

                    // Source is actual
                    Console.Write("S->T; ");
                    TryApplyChange(_sourceManager.Root, targetRoot, sourceChange);
                }
                else
                {
                    // Target is actual
                    Console.Write("T->S; ");
                    TryApplyChange(targetRoot, _sourceManager.Root, targetChange);
                }
            }
            else
            {
                // Entry change is present only on target
                // -> propagate the change to source
                Console.Write($"From target: {targetChange.Path}; ");
                TryApplyChange(targetRoot, _sourceManager.Root, targetChange);
            }
        }

        // The remaining entries in sourceDelta have to be sorted by timestamp, same as insertion order.
        foreach (var sourceEntry in sourceDelta.OrderBy(kvp => kvp.Value.Timestamp))
        {
            // Entry change is present only on source
            // -> propagate the change to target
            Console.Write($"From source: {sourceEntry.Key}; ");
            TryApplyChange(_sourceManager.Root, targetRoot, sourceEntry.Value);
        }

        Console.WriteLine($"Total changes applied: {appliedDelta.Count}.\n");

        // Generate the new index based on the old one, according to the rule:
        // s(d(S_0) + d(ΔS)) = S_0 + ΔS
        // where s(x) and d(x) stand for serialization and deserialization routines resp
        //
        // TODO: deserialization is happening twice: here and in GetTargetDelta;
        // re-use the already deserialized index
        var index = _sourceManager.DeserializeIndex();
        index.MergeChanges(appliedDelta);
        _sourceManager.SerializeIndex(index);

        Console.WriteLine($"Total changes failed: {failedDelta.Count}.");

        // TODO: propose possible workarounds for failed changes

        SetTargetLastSyncTimestamp(_targetManager.EventsDatabase);

        ClearEventsDatabase();

        Console.WriteLine("Synchronization complete.");
    }

    private static bool ApplyChange(string sourceRoot, string targetRoot, FileSystemEntryChange change)
    {
        Console.WriteLine($"{sourceRoot} -> {targetRoot}: [{change.Action}] {change.Path})");
        var sourceName = change.Properties.RenameProps?.Name;
        string Rename(string path) => sourceName == null ? path : ReplaceName(path, sourceName);

        var changeProps = change.Properties.ChangeProps;
        var sourcePath = Path.Combine(sourceRoot, Rename(change.Path));
        var targetPath = Path.Combine(targetRoot, change.Path);

        // Directory
        if (Path.EndsInDirectorySeparator(sourcePath))
        {
            DirectoryInfo sourceDirectory = new(sourcePath);
            DirectoryInfo targetDirectory;
            switch (change.Action)
            {
                case FileSystemEntryAction.Create:
                    // Source directory is not present -> the change is invalid
                    if (!sourceDirectory.Exists)
                        return false;

                    // Note: directory creation does not require contents comparison,
                    // as all contents are written as separate entries in database;
                    // see OnDirectoryCreated method
                    targetDirectory = new DirectoryInfo(Rename(targetPath));
                    targetDirectory.Create();
                    break;

                case FileSystemEntryAction.Change:
                    throw new DirectoryChangeActionNotAllowed();

                case FileSystemEntryAction.Delete:
                    // Source directory is present -> the change is invalid
                    if (sourceDirectory.Exists)
                        return false;

                    // Target directory is not present -> the change needs not to be applied
                    targetDirectory = new DirectoryInfo(targetPath);
                    if (!targetDirectory.Exists)
                        return true;

                    targetDirectory.Delete(recursive: true);
                    break;
            }
        }
        // File
        else
        {
            var sourceFile = new FileInfo(sourcePath);
            switch (change.Action)
            {
                case FileSystemEntryAction.Create:
                    // Source file is not present -> the change is invalid
                    if (!sourceFile.Exists)
                        return false;

                    // Source file is altered -> the change is invalid
                    if (sourceFile.LastWriteTime != changeProps!.LastWriteTime ||
                        sourceFile.Length != changeProps.Length)
                        return false;

                    sourceFile.CopyTo(Rename(targetPath));
                    break;

                case FileSystemEntryAction.Change:
                    // Source file is not present -> the change is invalid
                    if (!sourceFile.Exists)
                        return false;

                    // Source file is altered -> the change is invalid
                    if (sourceFile.LastWriteTime != changeProps!.LastWriteTime ||
                        sourceFile.Length != changeProps.Length)
                        return false;

                    sourceFile.CopyTo(targetPath, overwrite: true);

                    // Rename the target file if necessary
                    if (sourceName != null)
                        File.Move(targetPath, Rename(targetPath));
                    break;

                case FileSystemEntryAction.Delete:
                    // Source file is present -> the change is invalid
                    if (sourceFile.Exists)
                        return false;

                    // Target file is not present -> the change needs not to be applied
                    var targetFile = new FileInfo(targetPath);
                    if (!targetFile.Exists)
                        return true;

                    targetFile.Delete();
                    break;
            }
        }

        return true;
    }

    private static DateTime? GetTargetLastSyncTimestamp(Database targetDatabase)
    {
        DateTime? result = null;
        try
        {
            targetDatabase.ExecuteReader(new SqliteCommand("SELECT * FROM sync ORDER BY time DESC LIMIT 1"), reader =>
            {
                if (reader.Read())
                    result = DateTime.ParseExact(reader.GetString(1), CustomFileInfo.DateTimeFormat, null);
            });
        }
        catch (SqliteException ex)
        {
            // TODO: table not found ->
            // create it here so that timetstamp could be set later
        }
        return result;
    }

    private static void SetTargetLastSyncTimestamp(Database targetDatabase)
    {
        //var command = new SqliteCommand("INSERT INTO sync VALUES :time");
        //command.Parameters.AddWithValue(":time", DateTime.Now.ToString(CustomFileInfo.DateTimeFormat));
        //targetDatabase.ExecuteNonQuery(command);
    }

    private void ClearEventsDatabase()
    {
        var command = new SqliteCommand("DELETE FROM events");
        _sourceManager.EventsDatabase.ExecuteNonQuery(command);
    }

    private List<FileSystemEntryChange> GetTargetDelta(string targetRoot)
    {
        var sourceDir = _sourceManager.DeserializeIndex();  // last synced source data
        var targetDir = new DirectoryInfo(targetRoot);
        var builder = new StringBuilder();
        var delta = new List<FileSystemEntryChange>();
        GetTargetDelta(sourceDir, targetDir, builder, delta);
        return delta;
    }

    private void GetTargetDelta(CustomDirectoryInfo sourceDir, DirectoryInfo targetDir, StringBuilder builder, List<FileSystemEntryChange> delta)
    {
        // Note:
        // There is no way to determine whether a file was renamed on target if that info is not present.
        // Therefore, every such change is split into Delete + Create sequence. It works, but is suboptimal.
        //
        // The only workaround is to compare files not by names but by content hashes:
        // - file was changed -> its hash is changed -> copy file
        // - file wasn't changed -> its hash isn't changed -> don't copy file, only compare metadata (names, times, etc.)
        //
        // TODO: consider comparing files by content hashes
        foreach (var targetSubdir in builder.Wrap(targetDir.EnumerateDirectories(), d => d.Name + Path.DirectorySeparatorChar))
        {
            if (_targetManager.IsServiceLocation(targetSubdir.FullName))
                continue;

            if (sourceDir.Directories.Remove(targetSubdir.Name, out var sourceSubdir))
            {
                GetTargetDelta(sourceSubdir, targetSubdir, builder, delta);
            }
            else
            {
                OnDirectoryCreated(targetSubdir, builder, delta);
            }
        }

        foreach (var _ in builder.Wrap(sourceDir.Directories, d => d.Name))
        {
            delta.Add(new FileSystemEntryChange
            {
                Path = builder.ToString(),
                Action = FileSystemEntryAction.Delete
            });
        }

        foreach (var targetFile in builder.Wrap(targetDir.EnumerateFiles(), f => f.Name))
        {
            var targetLastWriteTime = targetFile.LastWriteTime.TrimMicroseconds();
            if (sourceDir.Files.Remove(targetFile.Name, out var sourceFile))
            {
                if (targetLastWriteTime != sourceFile.LastWriteTime || targetFile.Length != sourceFile.Length)
                {
                    delta.Add(new FileSystemEntryChange
                    {
                        Path = builder.ToString(),
                        Action = FileSystemEntryAction.Change,
                        Properties = new FileSystemEntryChangeProperties
                        {
                            ChangeProps = new ChangeProperties
                            {
                                LastWriteTime = targetLastWriteTime,
                                Length = targetFile.Length
                            }
                        }
                    });
                }
            }
            else
            {
                delta.Add(new FileSystemEntryChange
                {
                    Path = builder.ToString(),
                    Action = FileSystemEntryAction.Create,
                    Properties = new FileSystemEntryChangeProperties
                    {
                        ChangeProps = new ChangeProperties
                        {
                            LastWriteTime = targetLastWriteTime,
                            Length = targetFile.Length
                        }
                    }
                });
            }
        }

        foreach (var _ in builder.Wrap(sourceDir.Files, f => f.Name))
        {
            delta.Add(new FileSystemEntryChange
            {
                Path = builder.ToString(),
                Action = FileSystemEntryAction.Delete
            });
        }
    }

    private static void OnDirectoryCreated(DirectoryInfo root, StringBuilder builder, List<FileSystemEntryChange> delta)
    {
        delta.Add(new FileSystemEntryChange
        {
            Path = builder.ToString(),
            Action = FileSystemEntryAction.Create,
        });

        foreach (var directory in builder.Wrap(root.EnumerateDirectories(), d => d.Name + Path.DirectorySeparatorChar))
        {
            OnDirectoryCreated(directory, builder, delta);
        }

        foreach (var file in builder.Wrap(root.EnumerateFiles(), f => f.Name))
        {
            delta.Add(new FileSystemEntryChange
            {
                Path = builder.ToString(),
                Action = FileSystemEntryAction.Create,
                Properties = new FileSystemEntryChangeProperties
                {
                    ChangeProps = new ChangeProperties
                    {
                        LastWriteTime = file.LastWriteTime,
                        Length = file.Length
                    }
                }
            });
        }
    }

    // TODO: consider returning LookupLinkedList?
    private Dictionary<string, FileSystemEntryChange> GetSourceDelta()
    {
        var delta = new Dictionary<string, FileSystemEntryChange>();
        var names = new Dictionary<string, string>();
        _sourceManager.EventsDatabase.ExecuteReader(new SqliteCommand("SELECT * FROM events"), reader =>
        {
            while (reader.Read())
            {
                var timestamp = DateTime.ParseExact(reader.GetString(0), CustomFileInfo.DateTimeFormat, null);
                var path = reader.GetString(1);
                var action = FileSystemEntryActionExtensions.StringToAction(reader.GetString(2));
                var json = reader.IsDBNull(3) ? null : reader.GetString(3);

                if (names.ContainsKey(path))
                {
                    switch (action)
                    {
                        case FileSystemEntryAction.Rename:
                            var properties = ActionProperties.Deserialize<RenameProperties>(json)!;

                            // 1. Update the original entry to include the new name
                            var orig = names[path];
                            var origNew = string.Join('|', orig.Split('|')[0], properties.Name);
                            if (!delta.Remove(orig, out var value))
                                throw new KeyNotFoundException($"Key {orig} not found in source delta.");

                            delta.Add(origNew, value);

                            // 2. Update the reference to the original entry
                            names.Remove(path);
                            names.Add(ReplaceName(path, properties.Name), origNew);
                            continue;
                    }

                    // Continue processing the original entry
                    path = names[path];
                }

                if (delta.ContainsKey(path))
                {
                    var change = delta[path];
                    change.Timestamp = timestamp;
                    switch (action)
                    {
                        case FileSystemEntryAction.Create:
                            switch (change.Action)
                            {
                                // Create after Create or Rename or Change -> impossible
                                case FileSystemEntryAction.Create:
                                case FileSystemEntryAction.Rename:
                                case FileSystemEntryAction.Change:
                                    throw new InvalidActionSequenceException(change.Action, action);

                                // Create after Delete -> 2 options:
                                // 1. Same file got restored
                                // 2. Another file was created with the same name
                                //
                                // Either way, instead of checking files equality, we simply treat it as being changed.
                                case FileSystemEntryAction.Delete:
                                    change.Action = FileSystemEntryAction.Change;
                                    change.Properties.ChangeProps = ActionProperties.Deserialize<ChangeProperties>(json);
                                    break;
                            }
                            break;
                        case FileSystemEntryAction.Rename:
                            switch (change.Action)
                            {
                                // Rename after Create or Change -> ok
                                case FileSystemEntryAction.Create:
                                case FileSystemEntryAction.Change:
                                    var properties = ActionProperties.Deserialize<RenameProperties>(json)!;
                                    change.Properties.RenameProps = properties;

                                    // Update the original entry
                                    var orig = string.Join('|', path, properties.Name);
                                    if (!delta.Remove(path, out var value))
                                        throw new KeyNotFoundException($"Key {path} not found in source delta.");

                                    delta.Add(orig, value);

                                    // Create the reference to the original entry
                                    names.Add(ReplaceName(path, properties.Name), orig);
                                    break;

                                // Rename again -> processed earlier, must not appear here
                                // Rename after Delete -> impossible
                                case FileSystemEntryAction.Rename:
                                case FileSystemEntryAction.Delete:
                                    throw new InvalidActionSequenceException(change.Action, action);
                            }
                            break;
                        case FileSystemEntryAction.Change:
                            switch (change.Action)
                            {
                                // Change after Create -> ok but keep previous action
                                case FileSystemEntryAction.Create:
                                    change.Properties.ChangeProps = ActionProperties.Deserialize<ChangeProperties>(json);
                                    break;

                                // Change after Rename or Change -> ok
                                case FileSystemEntryAction.Rename:
                                case FileSystemEntryAction.Change:
                                    change.Action = action;
                                    change.Properties.ChangeProps = ActionProperties.Deserialize<ChangeProperties>(json);
                                    break;

                                // Change after Delete -> impossible
                                case FileSystemEntryAction.Delete:
                                    throw new InvalidActionSequenceException(change.Action, action);
                            }
                            break;
                        case FileSystemEntryAction.Delete:
                            switch (change.Action)
                            {
                                // Delete after Create -> temporary entry, no need to track it
                                case FileSystemEntryAction.Create:
                                    delta.Remove(path);
                                    break;

                                // Delete after Rename or Change -> ok
                                case FileSystemEntryAction.Rename:
                                case FileSystemEntryAction.Change:
                                    change.Action = action;
                                    change.Properties.RenameProps = null;
                                    change.Properties.ChangeProps = null;
                                    break;

                                // Delete again -> impossible
                                case FileSystemEntryAction.Delete:
                                    throw new InvalidActionSequenceException(change.Action, action);
                            }
                            break;
                    }
                }
                else
                {
                    var change = new FileSystemEntryChange
                    {
                        Timestamp = timestamp,
                        Path = path,
                        Action = action
                    };
                    switch (action)
                    {
                        case FileSystemEntryAction.Rename:
                            var properties = ActionProperties.Deserialize<RenameProperties>(json)!;
                            change.Properties.RenameProps = properties;

                            // Create the original entry
                            var orig = string.Join('|', path, properties.Name);
                            delta.Add(orig, change);

                            // Create the reference to the original entry
                            names.Add(ReplaceName(path, properties.Name), orig);
                            break;
                        case FileSystemEntryAction.Create:
                        case FileSystemEntryAction.Change:
                            change.Properties.ChangeProps = ActionProperties.Deserialize<ChangeProperties>(json);
                            delta.Add(path, change);
                            break;
                        case FileSystemEntryAction.Delete:
                            delta.Add(path, change);
                            break;
                    }

                }
            }
        });

        // Trim entries' names appendixes, as they are already stored in RenameProps
        foreach (var key in names.Values)
        {
            if (!delta.Remove(key, out var change))
                throw new KeyNotFoundException($"Key {key} not found in source delta.");

            delta.Add(key.Split('|')[0], change);
        }

        return delta;
    }

    // Replace an entry name in the given path:
    // path/to/a/file -> path/to/a/renamed-file
    // path/to/a/directory/ -> path/to/a/renamed-directory/
    private static string ReplaceName(string entry, string name)
    {
        int end = entry.Length - (Path.EndsInDirectorySeparator(entry) ? 2 : 1);
        int start = entry.LastIndexOf(Path.DirectorySeparatorChar, end, end + 1);
        return entry[..(start + 1)] + name + entry[(end + 1)..];
    }
}

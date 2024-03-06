using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.FileSystemGlobbing.Internal;
using Microsoft.Extensions.Primitives;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DatagentMonitor
{
    internal class Legacy
    {
        // Monitoring example: https://github.com/dotnet/runtime/discussions/69700
        //
        // Underlying FileSystemWatcher does not respect wildcard patterns:
        // 1. By default, IncludeSubdirectories = true
        // 2. By default, OnRenamed events for files are fired twice (for old and new paths), and at least two tokens get notified:
        //    a. token monitoring the directory where that event has appeared
        //    b. token monitoring that directory's parent
        //
        // This means tokens with '*.*' pattern can get notifications from their subdirectories.
        //
        // For now, we avoid dealing with FSW and unrelated notifications.
        // Switching to FSW is to be considered only if polling proves to severely impact performance.
        //_provider = new PhysicalFileProvider("D:/_temp")
        //{
        //    UsePollingFileWatcher = true,
        //    UseActivePolling = true,
        //};
        //SetupTracker("");
        //Console.WriteLine("Setup complete.\n");

        //var targetPath = "D:/_target";
        //var targetFiles = new Dictionary<string, FileInfo>(
        //    Directory.EnumerateFiles(targetPath, "*", SearchOption.AllDirectories)
        //             .Select(f => new KeyValuePair<string, FileInfo>(f[targetPath.Length..], new FileInfo(f)))
        //);

        //var sourceFilesDeleted = new List<FileInfo>();
        //var sourcePath = "D:/_source";
        //foreach (var file in Directory.EnumerateFiles(sourcePath, "*", SearchOption.AllDirectories))
        //{
        //    var sourceFileInfo = new FileInfo(file);
        //    var subpath = file[sourcePath.Length..];
        //    if (targetFiles.Remove(subpath, out var targetFileInfo))
        //    {
        //        // TODO: if last-write-time's are equal but lengths are not, preserve the bigger file
        //        if (sourceFileInfo.LastWriteTime != targetFileInfo.LastWriteTime ||
        //            sourceFileInfo.Length != targetFileInfo.Length)
        //            OnChanged();

        //        // If the file was not changed, everything's ok
        //    }
        //    else
        //    {
        //        // No file with the given name on the given path
        //        // -> it could be moved somewhere else
        //        // -> queue the file for a later search
        //        sourceFilesDeleted.Add(sourceFileInfo);
        //    }
        //}

        class Tracker : IDisposable
        {
            private string _subpath;
            public string Subpath => _subpath;

            private IChangeToken _token;
            private IDisposable _disposer;
            public IChangeToken Token => _token;

            private readonly Dictionary<string, IFileInfo> _directories;
            public Dictionary<string, IFileInfo> Directories => _directories;

            private readonly Dictionary<string, IFileInfo> _files;
            public Dictionary<string, IFileInfo> Files => _files;

            public Tracker(string subpath)
            {
                _subpath = subpath;
                _files = new();
                _directories = new();

                RefreshToken();
            }

            public void RefreshToken()
            {
                _token = _provider.Watch(Path.Combine(_subpath, _pattern));
                _disposer = _token.RegisterChangeCallback(OnTokenFired, this);
            }

            public override bool Equals(object? obj)
            {
                if (obj is not Tracker tracker)
                    return false;

                return Subpath == tracker.Subpath;
            }

            public override int GetHashCode() => _subpath.GetHashCode();

            public void Dispose()
            {
                _disposer.Dispose();
                GC.SuppressFinalize(this);
            }
        }

        private static readonly Dictionary<string, Tracker> _trackers = new();
        private static PhysicalFileProvider _provider;
        private static readonly string _pattern = "*";

        private static void SetupTracker(string subpath)
        {
            var contents = _provider.GetDirectoryContents(subpath);
            if (contents is null || !contents.Exists)
                throw new ArgumentException($"No directory found at {GetSubpathRepr(subpath)}");

            var tracker = new Tracker(subpath);
            if (!_trackers.TryAdd(subpath, tracker))
                throw new ArgumentException($"Duplicate tracker for {GetSubpathRepr(subpath)}");

            Console.WriteLine($"Create token at {GetSubpathRepr(subpath)}");
            foreach (var entry in contents)
            {
                if (entry.IsDirectory)
                {
                    tracker.Directories.Add(entry.Name, entry);
                    SetupTracker(Path.Combine(subpath, entry.Name));
                }
                else
                {
                    tracker.Files.Add(entry.Name, entry);
                }
            }
        }

        private static void ReleaseTracker(string subpath)
        {
            // TODO: consider using a Tree for trackers; we need to remove only a single subdirectory branch, 
            // scanning the whole dict is not the way to go
            var keys = new List<string>(_trackers.Keys.Where(k => k.StartsWith(subpath)));
            foreach (var key in keys)
            {
                _trackers.Remove(key, out var tracker);
                tracker!.Dispose();
                Console.WriteLine($"Release token at {GetSubpathRepr(subpath)}");
            }
        }

        private static void OnTokenFired(object? state)
        {
            // TODO: folders operations are NOT detected by Matcher in tokens!
            // Possible solutions:
            // 1. Use a separate poller for directories (i.e. another background task that polls directories every 4 seconds)
            // 2. Give up on polling and evaluate diff on sync procedure

            // Complexity: O(m + n) time, O(m + n) space, 
            // where m = old files count, n = new files count
            var tracker = state as Tracker;
            var contents = _provider.GetDirectoryContents(tracker!.Subpath);
            if (!contents.Exists)
            {
                // Tracked directory renamed
                // -> leave the processing to the parent tracker
                if (tracker.Subpath == "")
                    throw new NotImplementedException("Renaming the root directory is not supported.");

                var sep = tracker.Subpath.LastIndexOf(Path.DirectorySeparatorChar);
                var parent = tracker.Subpath[..Math.Max(sep, 0)];
                Task.Run(() => OnTokenFired(_trackers[parent]));
                return;
            }

            var directoriesOld = tracker.Directories;
            var directoriesRes = new List<IFileInfo>();
            var filesOld = tracker.Files;
            var filesNew = new Dictionary<(int, long), IFileInfo>();
            var filesRes = new List<IFileInfo>();
            foreach (var entry in contents)
            {
                if (entry.IsDirectory)
                {
                    if (!directoriesOld.Remove(entry.Name, out _))
                    {
                        // The directory with this name does not exist, meaning it is either an old directory renamed, or a brand new one.
                        // Here we just traverse it and store its current size and the number of files inside.
                        // Determining whether it was a rename or not is to be done later at some point ...
                        //
                        // ... or even skipped at all. In the worst case scenario, rename = delete + create,
                        // so during the sync procedure we might only lose some clock time on re-creating already existing folder.
                        SetupTracker(Path.Combine(tracker.Subpath, entry.Name));
                        OnCreated(tracker.Subpath, entry);
                    }
                    // Otherwise, the directory with this name already exists
                    // -> all changes to its contents are tracked by the inner trackers (which remain consistent)
                    // -> everything's ok

                    directoriesRes.Add(entry);
                }
                else
                {
                    if (filesOld.Remove(entry.Name, out var fileNotRenamed))
                    {
                        if (entry.LastModified.ToUnixTimeMilliseconds() != fileNotRenamed.LastModified.ToUnixTimeMilliseconds() ||
                            entry.Length != fileNotRenamed.Length)
                        {
                            // Changed only
                            OnChanged(tracker.Subpath, entry);
                        }
                        // Otherwise, neither renamed, nor changed

                        filesRes.Add(entry);
                    }
                    else
                    {
                        filesNew.Add((entry.LastModified.Millisecond, entry.Length), entry);
                    }
                }
            }

            foreach (var directory in directoriesOld.Values)
            {
                // The directory got deleted -> its tracker needs to be released
                ReleaseTracker(Path.Combine(tracker.Subpath, directory.Name));
                OnDeleted(tracker.Subpath, directory);
            }

            directoriesOld.Clear();

            // Track all processed directories
            foreach (var directory in directoriesRes)
                directoriesOld.Add(directory.Name, directory);

            foreach (var file in filesOld.Values)
            {
                if (filesNew.Remove((file.LastModified.Millisecond, file.Length), out var fileNotChanged))
                {
                    OnRenamed(tracker.Subpath, file, fileNotChanged);
                    filesRes.Add(fileNotChanged);
                }
                else
                {
                    OnDeleted(tracker.Subpath, file);
                }
            }

            filesOld.Clear();

            foreach (var file in filesNew.Values)
            {
                OnCreated(tracker.Subpath, file);
            }

            // Track created files
            foreach (var file in filesNew.Values)
                filesOld.Add(file.Name, file);

            // Track all the other processed files
            foreach (var file in filesRes)
                filesOld.Add(file.Name, file);

            tracker.RefreshToken();
        }

        private static void OnCreated(string path, IFileInfo file)
        {
            Console.WriteLine($"At {GetSubpathRepr(path)}: [Create] {file.Name}");
        }

        private static void OnRenamed(string path, IFileInfo fileOld, IFileInfo fileNew)
        {
            Console.WriteLine($"At {GetSubpathRepr(path)}: [Rename] {fileOld.Name} -> {fileNew.Name}");
        }

        private static void OnChanged(string path, IFileInfo file)
        {
            Console.WriteLine($"At {GetSubpathRepr(path)}: [Change] {file.Name}");
        }

        private static void OnDeleted(string path, IFileInfo file)
        {
            Console.WriteLine($"At {GetSubpathRepr(path)}: [Delete] {file.Name}");
        }

        private static string GetSubpathRepr(string subpath) => $".{Path.DirectorySeparatorChar}{subpath}";
    }
}

using DatagentMonitor.FileSystem;
using DatagentShared;
using Microsoft.Extensions.FileSystemGlobbing;
using System.Text;

namespace DatagentMonitor.Synchronization;

internal class SourceIndex
{
    private readonly string _root;
    public string Root => _root;

    private static readonly string _name = "index.txt";
    public static string Name => _name;

    public string Path => System.IO.Path.Combine(_root, SourceManager.FolderName, _name);

    private readonly CustomDirectoryInfo _rootImage;
    public CustomDirectoryInfo RootImage => _rootImage;

    public SourceIndex(string root)
    {
        _root = root;

        // TODO: computing a listing of a whole directory might take a long while, 
        // and some events might get missed during that operation; consider a faster solution if that's the case
        _rootImage = new CustomDirectoryInfo(new DirectoryInfo(_root), SourceFilter.ServiceMatcher);

        /* 1. If the index file is not present, it is created anew with the current directory contents.
         * 2. If the index file is present, it is overwritten, because we cannot be sure that 
         *    its contents reflect the current directory state.
         */
        Serialize(out _);
    }

    public void MergeChanges(IEnumerable<EntryChange> changes)
    {
        foreach (var change in changes)
        {
            if (change.Timestamp is null)
                throw new InvalidOperationException($"Cannot merge a change without a timestamp: {change}");

            if (SourceFilter.ServiceMatcher.Match(_root, change.OldPath).HasMatches)
                continue;

            switch (change.Action)
            {
                case EntryAction.Create:
                    _rootImage.Create(change.Timestamp.Value, change.OldPath, change.Type switch
                    {
                        EntryType.Directory => new CustomDirectoryInfo(change.Name, change.Timestamp!.Value),
                        EntryType.File => new CustomFileInfo(
                            change.Name,
                            change.ChangeProperties!.Value.LastWriteTime,
                            change.ChangeProperties!.Value.Length)
                    });
                    break;

                case EntryAction.Rename:
                    _rootImage.Rename(change.Timestamp.Value, change.OldPath, change.RenameProperties!.Value, out _);
                    break;

                case EntryAction.Change:
                    _rootImage.Change(change.Timestamp.Value, change.OldPath, change.ChangeProperties!.Value, out _);
                    break;

                case EntryAction.Delete:
                    _rootImage.Delete(change.Timestamp.Value, change.OldPath, out _);
                    break;
            }
        }
    }

    public void Serialize(out string result)
    {
        result = CustomDirectoryInfoSerializer.Serialize(_rootImage);
        using var writer = new StreamWriter(Path, append: false, encoding: Encoding.UTF8);
        writer.Write(result);
    }

    public void Deserialize(out CustomDirectoryInfo result)
    {
        using var reader = new StreamReader(Path, encoding: Encoding.UTF8);
        result = CustomDirectoryInfoSerializer.Deserialize(reader);
    }

    public void CopyTo(string path) => File.Copy(Path, path, overwrite: true);
}

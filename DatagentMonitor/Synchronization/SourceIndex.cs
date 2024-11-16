using DatagentMonitor.FileSystem;
using Microsoft.Extensions.FileSystemGlobbing;
using System.Text;

namespace DatagentMonitor.Synchronization;

// TODO: consider merging all SourceIndex functionality into SyncSourceManager
internal class SourceIndex
{
    private static readonly string _name = "index.txt";

    private readonly string _path;
    public string Path => _path;

    private readonly string _rootPath;
    private readonly CustomDirectoryInfo _root;
    public CustomDirectoryInfo Root => _root;

    private readonly Matcher? _matcher;

    public SourceIndex(string root, string path, Matcher matcher)
    {
        _rootPath = root;
        _path = System.IO.Path.Combine(root, path, _name);
        _matcher = matcher;

        // TODO: computing a listing of a whole directory might take a long while, 
        // and some events might get missed during that operation; consider a faster solution if that's the case
        _root = new CustomDirectoryInfo(new DirectoryInfo(root), matcher);
        if (!File.Exists(_path))
            Serialize(out _);
    }

    public SourceIndex(string path)
    {
        _path = path;
        Deserialize(out _root);
    }

    public void MergeChanges(IEnumerable<EntryChange> changes)
    {
        foreach (var change in changes)
        {
            if (change.Timestamp is null)
                throw new InvalidOperationException($"Cannot merge a change without a timestamp: {change}");

            if (_matcher is not null && _matcher.Match(_rootPath, change.OldPath).HasMatches)
                continue;

            switch (change.Action)
            {
                case EntryAction.Create:
                    _root.Create(change.Timestamp.Value, change.OldPath, change.Type switch
                    {
                        EntryType.Directory => new CustomDirectoryInfo(change.Name, change.Timestamp!.Value),
                        EntryType.File => new CustomFileInfo(
                            change.Name,
                            change.ChangeProperties!.Value.LastWriteTime,
                            change.ChangeProperties!.Value.Length)
                    });
                    break;

                case EntryAction.Rename:
                    _root.Rename(change.Timestamp.Value, change.OldPath, change.RenameProperties!.Value, out _);
                    break;

                case EntryAction.Change:
                    _root.Change(change.Timestamp.Value, change.OldPath, change.ChangeProperties!.Value, out _);
                    break;

                case EntryAction.Delete:
                    _root.Delete(change.Timestamp.Value, change.OldPath, out _);
                    break;
            }
        }
    }

    public void Serialize(out string result)
    {
        result = CustomDirectoryInfoSerializer.Serialize(_root);
        using var writer = new StreamWriter(_path, append: false, encoding: Encoding.UTF8);
        writer.Write(result);
    }

    public void Deserialize(out CustomDirectoryInfo result)
    {
        using var reader = new StreamReader(_path, encoding: Encoding.UTF8);
        result = CustomDirectoryInfoSerializer.Deserialize(reader);
    }

    public void CopyTo(string path) => File.Copy(Path, path, overwrite: true);
}

using DatagentMonitor.FileSystem;
using System.Text;

namespace DatagentMonitorTests;

// TODO: consider expanding the testing framework with the following pattern:
// Test_Something
// |-> [ { source_1, target_1 }, ..., { source_n, target_n } ]
//       |-> [ { a_1, b_1, ..., result_1 }, ..., { a_m, b_m, ..., result_m } ]
//
// In other words, every synchronization aspect is tested against a set of pairs { source_i, target_i } ("top-level cases"), 
// and every such pair (for the specific aspect) is tested against a set of properties ("low-level cases"), 
// like LastSyncTime, etc., along with the corresponding result.
public abstract class TestBase
{
    private static readonly Dictionary<string, Dictionary<string, string>> _configs = new();

    private readonly Random _rng;

    private string? _dataPath;
    protected string DataPath => _dataPath ??= GetDataPath(GetType());

    private string? _testName;
    protected string TestName => _testName ??= 
        string.Concat(GetType().Namespace!.SkipWhile(ch => ch is not '.')).Replace('.', '_').ToLower();

    private Dictionary<string, string>? _config;
    protected Dictionary<string, string> Config
    {
        get
        {
            lock (_configs)
            {
                if (_config is null && !_configs.TryGetValue(DataPath, out _config))
                    _configs.Add(DataPath, _config = ReadConfig(Path.Combine(DataPath, "config.txt")));
            }

            return _config;
        }
    }

    public TestBase()
    {
        _rng = new Random(12345);
    }

    protected static string GetDataPath(Type type) =>
        Path.Combine("Data", type.Namespace!.Split('.')[^2], type.Name);

    private static Dictionary<string, string> ReadConfig(string path)
    {
        var config = new Dictionary<string, string>();
        using var enumerator = File.ReadLines(path).GetEnumerator();
        while (enumerator.MoveNext())
        {
            var tag = enumerator.Current;
            if (tag is "")
                continue;

            // Validate the tag
            if (!tag.StartsWith("[") || !tag.EndsWith("]"))
                throw new ArgumentException("Invalid spec format.");

            // Read the tagged contents
            var builder = new StringBuilder();
            while (enumerator.MoveNext() && enumerator.Current is not "")
                builder.Append(enumerator.Current).Append('\n');

            config[tag[1..^1]] = builder.ToString();
        }

        return config;
    }

    protected void ToFileSystem(DirectoryInfo sourceRoot, CustomDirectoryInfo targetRoot)
    {
        foreach (var targetSubdir in targetRoot.Entries.Directories)
        {
            var sourceSubdir = sourceRoot.CreateSubdirectory(targetSubdir.Name);
            ToFileSystem(sourceSubdir, targetSubdir);
        }

        foreach (var targetFile in targetRoot.Entries.Files)
        {
            var sourceFile = new FileInfo(Path.Combine(sourceRoot.FullName, targetFile.Name));
            using (var writer = sourceFile.CreateText())
            {
                // Every char here takes 1 byte, as it is within the range [48, 123)
                for (int i = 0; i < targetFile.Length; i++)
                    writer.Write((char)_rng.Next(48, 123));
            }
            sourceFile.LastWriteTime = targetFile.LastWriteTime;
        }

        sourceRoot.LastWriteTime = targetRoot.LastWriteTime;
    }
}

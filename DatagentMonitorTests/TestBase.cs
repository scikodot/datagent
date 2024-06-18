using System.Text;

namespace DatagentMonitorTests;

public abstract class TestBase
{
    private static readonly Dictionary<string, Dictionary<string, string>> _configs = new();

    private string? _dataPath;
    protected string DataPath => _dataPath ??= GetDataPath(GetType());

    private string? _testName;
    protected string TestName => _testName ??= 
        string.Concat(GetType().FullName!.ToLower().Replace('.', '_').SkipWhile(ch => ch is not '_').Skip(1));

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

    protected string GetTempDirectoryName(string prefix) => $"_{prefix}_{TestName}";
}

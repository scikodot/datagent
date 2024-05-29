using System.Text;

namespace DatagentMonitorTests;

public abstract class TestBase
{
    private static readonly Dictionary<string, Dictionary<string, string>> _configs = new();

    private string? _dataPath;
    protected string DataPath => _dataPath ??= GetTestDataPath();

    private Dictionary<string, string>? _config;
    protected Dictionary<string, string> Config
    {
        get
        {
            if (_config is null)
            {
                if (!_configs.TryGetValue(DataPath, out _config))
                    _configs.Add(DataPath, _config = ReadConfig(Path.Combine(DataPath, "config.txt")));
            }

            return _config;
        }
    }

    private string GetTestDataPath() => GetTestDataPath(GetType());

    private static string GetTestDataPath(Type type) =>
        Path.Combine(type.Namespace!.Split('.')[^1], "Data", type.Name);

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
}

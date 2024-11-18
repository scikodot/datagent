using DatagentMonitor.FileSystem;
using DatagentShared;
using Microsoft.Extensions.FileSystemGlobbing;

namespace DatagentMonitor.Synchronization;

internal class SourceFilter
{
    private static readonly List<string> _serviceExcludePatterns = new()
    {
        // The service folder itself
        SourceManager.FolderName, 
        // All contents of the service folder
        $"{SourceManager.FolderName}/" 
    };

    private readonly string _root;
    public string Root => _root;

    private static readonly string _userExcludeName = "user.exclude";
    public static string UserExcludeName => _userExcludeName;

    public string UserExcludePath => Path.Combine(_root, SourceManager.FolderName, _userExcludeName);

    private static readonly Matcher _serviceMatcher;
    public static Matcher ServiceMatcher => _serviceMatcher;

    private readonly Matcher _userMatcher;
    public Matcher UserMatcher => _userMatcher;

    static SourceFilter()
    {
        _serviceMatcher = new Matcher();
        _serviceMatcher.AddIncludePatterns(_serviceExcludePatterns);
    }

    public SourceFilter(string root)
    {
        _root = root;
        _userMatcher = new Matcher();
        if (File.Exists(UserExcludePath))
        {
            _userMatcher.AddIncludePatterns(
                File.ReadLines(UserExcludePath)
                    .Where(l => !string.IsNullOrWhiteSpace(l) && !l.StartsWith('#')));
        }
        else
        {
            using var writer = File.CreateText(UserExcludePath);
            writer.WriteLine("# Add your exclude patterns below. Example:");
            writer.WriteLine("# *.txt");
        }
    }

    public bool ServiceExcludes(string path) => _serviceMatcher.Match(_root, path).HasMatches;

    public bool UserExcludes(string path, EntryType type) => _serviceMatcher.Match(_root, type switch
    {
        EntryType.Directory => $"{path}/",
        EntryType.File => path
    }).HasMatches;
}

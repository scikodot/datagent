using DatagentMonitor;
using DatagentMonitor.Collections;
using DatagentMonitor.FileSystem;

namespace DatagentMonitorTests.FileSystemTrieTests;

public class DateTimeProviderFixture : DateTimeProviderFixtureAbstract
{
    public override IDateTimeProvider DateTimeProvider => DateTimeProviderFactory.FromDefault();
}

public class Test_Add : IClassFixture<DateTimeProviderFixture>
{
    private readonly FileSystemTrie _trie = new();

    private static readonly List<EntryChange> _identityArgs = new()
    {
        new EntryChange(
            new DateTime(2024, 11, 14, 1, 0, 0),
            "folder1",
            EntryType.Directory, EntryAction.Rename,
            new RenameProperties("folder1"), null),

        new EntryChange(
            new DateTime(2024, 11, 14, 2, 0, 0),
            Path.Combine("folder1", "file1"),
            EntryType.File, EntryAction.Rename,
            new RenameProperties("file1"), null)
    };

    private static readonly List<EntryChange> _sparseCreate = new()
    {
        new EntryChange(
            new DateTime(2024, 11, 14, 1, 0, 0),
            "folder1",
            EntryType.Directory, EntryAction.Create,
            null, new ChangeProperties
            {
                LastWriteTime = new DateTime(2024, 11, 14, 1, 0, 0)
            }),

        new EntryChange(
            new DateTime(2024, 11, 14, 1, 0, 0),
            Path.Combine("folder1", "subfolder2"),
            EntryType.Directory, EntryAction.Create,
            null, new ChangeProperties
            {
                LastWriteTime = new DateTime(2024, 11, 14, 1, 0, 0)
            }),

        new EntryChange(
            new DateTime(2024, 11, 14, 1, 0, 0),
            Path.Combine("folder1", "subfolder1", "file1"),
            EntryType.File, EntryAction.Create,
            null, new ChangeProperties
            {
                LastWriteTime = new DateTime(2024, 11, 14, 1, 0, 0), 
                Length = 100
            }),

        new EntryChange(
            new DateTime(2024, 11, 14, 1, 0, 0),
            Path.Combine("folder1", "subfolder1"),
            EntryType.Directory, EntryAction.Create,
            null, new ChangeProperties
            {
                LastWriteTime = new DateTime(2024, 11, 14, 1, 0, 0)
            }),

        new EntryChange(
            new DateTime(2024, 11, 14, 1, 0, 0),
            Path.Combine("folder1", "subfolder2", "file2"),
            EntryType.File, EntryAction.Create,
            null, new ChangeProperties
            {
                LastWriteTime = new DateTime(2024, 11, 14, 2, 0, 0), 
                Length = 200
            })
    };

    private static readonly List<EntryChange> _sparseDelete = new()
    {
        new EntryChange(
            new DateTime(2024, 11, 14, 1, 0, 0),
            Path.Combine("folder1", "file1"),
            EntryType.File, EntryAction.Rename,
            new RenameProperties("file1-renamed"), null),

        new EntryChange(
            new DateTime(2024, 11, 14, 2, 0, 0),
            Path.Combine("folder1", "file2"),
            EntryType.File, EntryAction.Change,
            null, new ChangeProperties
            {
                LastWriteTime = new DateTime(2024, 11, 14, 2, 0, 0),
                Length = 250
            }),

        new EntryChange(
            new DateTime(2024, 11, 14, 3, 0, 0),
            Path.Combine("folder1", "file1"),
            EntryType.File, EntryAction.Delete,
            null, null),

        new EntryChange(
            new DateTime(2024, 11, 14, 3, 0, 0),
            "folder1",
            EntryType.Directory, EntryAction.Delete,
            null, null),

        new EntryChange(
            new DateTime(2024, 11, 14, 3, 0, 0),
            Path.Combine("folder1", "file2"),
            EntryType.File, EntryAction.Delete,
            null, null),
    };

    private static readonly List<(List<EntryChange> Changes, Type ExceptionType)> _failureArgs = new()
    {
        (_sparseCreate, typeof(ArgumentException)),
        (_sparseDelete, typeof(ArgumentException))
    };

    public static IEnumerable<object[]> IdentityArgs => _identityArgs.Select(c => new object[] { c });
    public static IEnumerable<object[]> FailureArgs => _failureArgs.Select(a => new object[] { a.Changes, a.ExceptionType });

    public Test_Add(DateTimeProviderFixture dateTimeProviderFixture)
    {
        _trie = new();
    }

    [Theory]
    [MemberData(nameof(IdentityArgs))]
    public void Test_Identity(EntryChange change)
    {
        _trie.Add(change);
        Assert.Empty(_trie);
    }

    [Theory]
    [MemberData(nameof(FailureArgs))]
    public void Test_Failure(List<EntryChange> changes, Type exceptionType)
    {
        Assert.Throws(exceptionType, () => _trie.AddRange(changes));
    }
}

namespace DatagentMonitorTests;

public class TestBaseCommon
{
    protected static string GetTestDataPath(Type type) =>
        Path.Combine(type.Namespace!.Split('.')[^1], "Data", type.Name);

    protected string GetTestDataPath() => GetTestDataPath(GetType());
}

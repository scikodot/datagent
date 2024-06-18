namespace DatagentMonitor;

public interface IDateTimeProvider
{
    public DateTime Now { get; }
}

public class DateTimeProviderFactory
{
    // TODO: add timezone handling
    private class DateTimeProvider : IDateTimeProvider
    {
        private readonly Func<DateTime> _now;
        public DateTime Now => _now();

        public DateTimeProvider(Func<DateTime> now)
        {
            _now = now;
        }
    }

    public static IDateTimeProvider FromDefault() => new DateTimeProvider(() => DateTime.Now);

    public static IDateTimeProvider FromDateTime(DateTime dateTime) => new DateTimeProvider(() => dateTime);
}

public static class DateTimeStaticProvider
{
    [ThreadStatic] private static IDateTimeProvider? _instance;
    public static IDateTimeProvider Instance
    {
        get
        {
            if (!Initialized)
                throw new InvalidOperationException($"{nameof(DateTimeStaticProvider)} is not initialized.");

            return _instance!;
        }
        private set
        {
            if (Initialized)
                throw new InvalidOperationException($"{nameof(DateTimeStaticProvider)} is already initialized.");

            _instance = value;
        }
    }

    public static bool Initialized => _instance is not null;

    public static DateTime Now => Instance.Now;

    public static void Initialize(IDateTimeProvider provider) => _instance = provider;

    public static void Reset() => _instance = null;
}



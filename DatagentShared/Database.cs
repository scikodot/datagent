using Microsoft.Data.Sqlite;

namespace DatagentShared
{
    public class Database
    {
        private bool _initialized;

        protected readonly string _connectionString;
        public string ConnectionString => _connectionString;

        public Database(string path)
        {
            _connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = path,
                Mode = SqliteOpenMode.ReadWriteCreate,
                Pooling = false
            }.ToString();
        }

        public void ExecuteReader(SqliteCommand command, Action<SqliteDataReader> action)
        {
            using var connection = new SqliteConnection(ConnectionString);
            connection.Open();
            
            command.Connection = connection;
            using var reader = command.ExecuteReader();
            action(reader);
        }

        public async Task ExecuteReaderAsync(SqliteCommand command, Action<SqliteDataReader> action)
        {
            using var connection = await ConnectAsync();
            
            command.Connection = connection;
            using var reader = await command.ExecuteReaderAsync();
            action(reader);
        }

        public IEnumerable<T> ExecuteForEach<T>(SqliteCommand command, Func<SqliteDataReader, T> action)
        {
            using var connection = new SqliteConnection(ConnectionString);
            connection.Open();

            command.Connection = connection;
            using var reader = command.ExecuteReader();
            while (reader.Read())
                yield return action(reader);
        }

        public async IAsyncEnumerable<T> ExecuteForEachAsync<T>(SqliteCommand command, Func<SqliteDataReader, T> action)
        {
            using var connection = await ConnectAsync();

            command.Connection = connection;
            using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                yield return action(reader);
        }

        public void ExecuteNonQuery(SqliteCommand command)
        {
            using var connection = new SqliteConnection(ConnectionString);
            connection.Open();

            command.Connection = connection;
            command.ExecuteNonQuery();
        }

        public async Task ExecuteNonQueryAsync(SqliteCommand command)
        {
            using var connection = await ConnectAsync();

            command.Connection = connection;
            await command.ExecuteNonQueryAsync();
        }

        private async Task<SqliteConnection> ConnectAsync()
        {
            var connection = new SqliteConnection(ConnectionString);
            await connection.OpenAsync();

            // Perform initialization routines only once (upon first connection)
            if (!_initialized)
            {
                await InitAsync(connection);
                _initialized = true;
            }

            return connection;
        }

        // Base method for initialization routines; 
        // override in derived classes to provide database-specific entities, like tables, etc.
        protected virtual Task InitAsync(SqliteConnection connection) => Task.CompletedTask;
    }
}

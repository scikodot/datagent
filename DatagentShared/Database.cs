using Microsoft.Data.Sqlite;

namespace DatagentShared
{
    public class Database
    {
        private readonly string _connectionString;
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

        public IEnumerable<T> ExecuteForEach<T>(SqliteCommand command, Func<SqliteDataReader, T> action)
        {
            using var connection = new SqliteConnection(ConnectionString);
            connection.Open();

            command.Connection = connection;
            using var reader = command.ExecuteReader();
            while (reader.Read())
                yield return action(reader);
        }

        public void ExecuteNonQuery(SqliteCommand command)
        {
            using var connection = new SqliteConnection(ConnectionString);
            connection.Open();

            command.Connection = connection;
            command.ExecuteNonQuery();
        }
    }
}

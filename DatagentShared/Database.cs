using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DatagentShared
{
    public class Database
    {
        private string _connectionString;
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

        public void ExecuteNonQuery(SqliteCommand command)
        {
            using var connection = new SqliteConnection(ConnectionString);
            connection.Open();

            command.Connection = connection;
            command.ExecuteNonQuery();
        }
    }
}

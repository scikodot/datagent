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
                Mode = SqliteOpenMode.ReadWriteCreate
            }.ToString();
        }

        public void ExecuteReader(SqliteCommand command, Action<SqliteDataReader> action)
        {
            try
            {
                using var connection = new SqliteConnection(ConnectionString);
                connection.Open();

                command.Connection = connection;
                using var reader = command.ExecuteReader();
                action(reader);
            }
            catch (SqliteException ex)
            {
                PrintException(ex);
            }
        }

        public void ExecuteNonQuery(SqliteCommand command)
        {
            try
            {
                using var connection = new SqliteConnection(ConnectionString);
                connection.Open();

                command.Connection = connection;
                command.ExecuteNonQuery();
            }
            catch (SqliteException ex)
            {
                PrintException(ex);
            }
        }

        private void PrintException(SqliteException ex)
        {
            Console.WriteLine(ex.ToString());
            Console.ReadKey();
        }
    }
}

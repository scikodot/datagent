using Datagent.Data;
using Datagent.ViewModels;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Datagent.Extensions;

public static class Extensions
{
    public static string ToSqlite(this ColumnType columnType) => columnType switch
    {
        ColumnType.Integer => "INTEGER",
        ColumnType.Real => "REAL",
        ColumnType.Text => "TEXT",
        _ => throw new NotSupportedException()
    };
}

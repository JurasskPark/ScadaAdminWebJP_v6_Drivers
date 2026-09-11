// Copyright (c) Rapid Software LLC. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Scada.Admin.Actions;
using Scada.Dbms;
using Scada.Log;
using Scada.MultiDb;
using Scada;
using System.Data;

namespace Scada.Comm.Drivers.DrvDbImport.View
{
    /// <summary>
    /// Provides web administrator actions for the DB import driver.
    /// <para>Предоставляет действия веб-администратора для драйвера импорта из БД.</para>
    /// </summary>
    public sealed class AdminActionProvider
    {
        /// <summary>
        /// Tests connection to a database.
        /// </summary>
        [AdminAction(AdminActionIds.TestConnection)]
        [AdminActionArgs(typeof(ConnectionArgs))]
        public Task<AdminActionResult> TestConnection(
            AdminActionContext context, CancellationToken cancellationToken)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                DbConnectionOptions connectionOptions = CreateConnectionOptions(context);
                DataSource dataSource = DataSourceFactory.GetDataSource(connectionOptions);

                try
                {
                    dataSource.Connect();
                    cancellationToken.ThrowIfCancellationRequested();
                    return Task.FromResult(AdminActionResult.Ok("Database connection established successfully."));
                }
                finally
                {
                    dataSource.Disconnect();
                }
            }
            catch (Exception ex)
            {
                return Task.FromResult(AdminActionResult.Error(ex.BuildErrorMessage("Database connection failed")));
            }
        }

        /// <summary>
        /// Executes the first active database query and returns result rows.
        /// </summary>
        [AdminAction(AdminActionIds.DBQuery)]
        [AdminActionArgs(typeof(DbQueryArgs))]
        public Task<AdminActionResult> DBQuery(
            AdminActionContext context, CancellationToken cancellationToken)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                QueryArgs args = context.GetArgs<QueryArgs>();
                QueryRow query = args.Queries
                    .FirstOrDefault(query => query.Active) ??
                    args.Queries.FirstOrDefault();

                if (query == null || string.IsNullOrWhiteSpace(query.Sql))
                {
                    return Task.FromResult(AdminActionResult.Error("SQL query is not specified."));
                }

                DbConnectionOptions connectionOptions = CreateConnectionOptions(context);
                DataSource dataSource = DataSourceFactory.GetDataSource(connectionOptions);

                try
                {
                    dataSource.Connect();
                    cancellationToken.ThrowIfCancellationRequested();

                    using IDbCommand command = dataSource.CreateCommand(query.Sql);
                    using IDataReader reader = command.ExecuteReader();

                    List<QueryColumnInfo> columns = [];

                    for (int i = 0; i < reader.FieldCount; i++)
                    {
                        columns.Add(new QueryColumnInfo
                        {
                            Name = reader.GetName(i),
                            DataType = reader.GetDataTypeName(i),
                            ClrType = reader.GetFieldType(i).Name
                        });
                    }

                    List<Dictionary<string, string>> rows = [];

                    while (reader.Read())
                    {
                        if (args.RowLimit > 0 && rows.Count >= args.RowLimit)
                        {
                            break;
                        }

                        Dictionary<string, string> row = [];

                        for (int i = 0; i < reader.FieldCount; i++)
                        {
                            object value = reader.GetValue(i);
                            row[reader.GetName(i)] = value == DBNull.Value ? "" : Convert.ToString(value) ?? "";
                        }

                        rows.Add(row);
                    }

                    return Task.FromResult(AdminActionResult.Table(new
                    {
                        columns = rows.Count > 0
                            ? columns.Select(column => column.Name).ToArray()
                            : new[] { "name", "dataType", "clrType" },
                        rows = rows.Count > 0 ? (object)rows : columns,
                        schema = columns,
                        rowLimit = args.RowLimit > 0 ? args.RowLimit : (int?)null
                    }));
                }
                finally
                {
                    dataSource.Disconnect();
                }
            }
            catch (Exception ex)
            {
                return Task.FromResult(AdminActionResult.Error(ex.BuildErrorMessage("Database query failed")));
            }
        }

        private static DbConnectionOptions CreateConnectionOptions(AdminActionContext context)
        {
            string dbms = context.GetString("dbms", "PostgreSQL");

            if (!Enum.TryParse(dbms, true, out KnownDBMS knownDBMS) ||
                knownDBMS == KnownDBMS.Undefined)
            {
                throw new ScadaException("DBMS is not specified or is not supported.");
            }

            return new DbConnectionOptions
            {
                KnownDBMS = knownDBMS,
                Server = context.GetString("server"),
                Database = context.GetString("database"),
                Username = context.GetString("username"),
                Password = context.GetString("password"),
                ConnectionString = context.GetString("connectionString")
            };
        }

        private sealed class QueryArgs
        {
            public List<QueryRow> Queries { get; set; } = [];
            public int RowLimit { get; set; }
        }

        private class ConnectionArgs
        {
            public string Dbms { get; set; } = "";
            public string Server { get; set; } = "";
            public string Database { get; set; } = "";
            public string Username { get; set; } = "";
            public string Password { get; set; } = "";
            public string ConnectionString { get; set; } = "";
        }

        private sealed class DbQueryArgs : ConnectionArgs
        {
            public List<QueryRow> Queries { get; set; } = [];
            public int RowLimit { get; set; }
        }

        private sealed class QueryRow
        {
            public bool Active { get; set; }
            public string Name { get; set; } = "";
            public string Sql { get; set; } = "";
        }

        private sealed class QueryColumnInfo
        {
            public string Name { get; init; } = "";
            public string DataType { get; init; } = "";
            public string ClrType { get; init; } = "";
        }
    }
}

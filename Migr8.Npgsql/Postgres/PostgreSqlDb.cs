﻿using Migr8.Internals;

namespace Migr8.Npgsql.Postgres
{
    class PostgreSqlDb : IDb
    {
        public IExclusiveDbConnection GetExclusiveDbConnection(string connectionString)
        {
            return new PostgresqlExclusiveDbConnection(connectionString);
        }
    }
}
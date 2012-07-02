﻿using System;
using System.Data;
using System.Data.SqlClient;
using System.Linq;

namespace Migr8
{
    public class DatabaseMigrator : IDisposable
    {
        readonly bool ownsTheDbConnection;
        readonly IProvideMigrations provideMigrations;
        readonly IDbConnection dbConnection;

        public DatabaseMigrator(IDbConnection dbConnection, IProvideMigrations provideMigrations)
            : this(dbConnection, false, provideMigrations)
        {
        }

        public DatabaseMigrator(string connectionString, IProvideMigrations provideMigrations)
            : this(new SqlConnection(connectionString), true, provideMigrations)
        {
        }

        DatabaseMigrator(IDbConnection dbConnection, bool ownsTheDbConnection, IProvideMigrations provideMigrations)
        {
            this.ownsTheDbConnection = ownsTheDbConnection;
            this.provideMigrations = provideMigrations;
            this.dbConnection = dbConnection;

            if (ownsTheDbConnection)
            {
                dbConnection.Open();
            }
        }

        public void Dispose()
        {
            if (ownsTheDbConnection)
            {
                Console.WriteLine("Disposing connection");
                dbConnection.Close();
                dbConnection.Dispose();
            }
            else
            {
                Console.WriteLine("Didn't dispose connection because it was provided from the outside");
            }
        }

        public void MigrateDatabase()
        {
            try
            {
                EnsureDatabaseHasVersionMetaData();
                var databaseVersionNumber = GetDatabaseVersionNumber();

                var migrationsToExecute = provideMigrations.GetAllMigrations().Where(m => m.TargetDatabaseVersion > databaseVersionNumber);

                foreach (var migration in migrationsToExecute.OrderBy(m => m.TargetDatabaseVersion))
                {
                    ExecuteMigration(migration);
                }
            }
            catch (Exception e)
            {
                throw new ApplicationException(string.Format("Something bad happened during migration"), e);
            }
        }

        void ExecuteMigration(IMigration migration)
        {
            try
            {
                using (var context = new DatabaseContext(dbConnection))
                {
                    context.NewTransaction();
                    foreach (var sqlStatement in migration.SqlStatements)
                    {
                        try
                        {
                            context.ExecuteNonQuery(sqlStatement);
                        }
                        catch (Exception e)
                        {
                            throw new ApplicationException(
                                string.Format(@"The following SQL could not be executed:

{0}

Exception:

{1}",
                                              sqlStatement, e), e);
                        }
                    }

                    var currentVersion = GetDatabaseVersionNumber(context);
                    var newVersion = currentVersion + 1;

                    context.ExecuteNonQuery(
                        string.Format("exec sys.sp_updateextendedproperty @name=N'{0}', @value=N'{1}'",
                                      ExtProp.DatabaseVersion, newVersion.ToString()));

                    context.Commit();
                }
            }
            catch (Exception e)
            {
                throw new ApplicationException(string.Format("The migration {0} (db version -> {1}) could not be executed", migration.Description, migration.TargetDatabaseVersion), e);
            }
        }


        int GetDatabaseVersionNumber()
        {
            using (var context = new DatabaseContext(dbConnection))
            {
                return GetDatabaseVersionNumber(context);
            }
        }

        int GetDatabaseVersionNumber(DatabaseContext context)
        {
            var versionProperty = context
                .ExecuteQuery(
                    string.Format("select * from sys.extended_properties where [class] = 0 and [name] = '{0}'",
                                  ExtProp.DatabaseVersion))
                .Single();
            var currentVersion = int.Parse(versionProperty["value"].ToString());
            return currentVersion;
        }

        void EnsureDatabaseHasVersionMetaData()
        {
            using (var context = new DatabaseContext(dbConnection))
            {
                context.NewTransaction();

                var sql = string.Format("select * from sys.extended_properties where [class] = 0 and [name] = '{0}'",
                                        ExtProp.DatabaseVersion);

                var properties = context.ExecuteQuery(sql);

                if (properties.Count == 0)
                {
                    context.ExecuteNonQuery(string.Format("exec sys.sp_addextendedproperty @name=N'{0}', @value=N'{1}'", ExtProp.DatabaseVersion, "0"));
                }

                context.Commit();
            }
        }
    }
}
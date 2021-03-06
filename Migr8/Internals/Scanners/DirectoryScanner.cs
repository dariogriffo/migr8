﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Migr8.Internals.Scanners
{
    class DirectoryScanner
    {
        readonly string _directory;

        public DirectoryScanner(string directory)
        {
            _directory = directory;
        }

        public IEnumerable<IExecutableSqlMigration> GetMigrations()
        {
            if (!Directory.Exists(_directory))
            {
                return Enumerable.Empty<IExecutableSqlMigration>();
            }

            return Directory.GetFiles(_directory, "*.sql", SearchOption.AllDirectories)
                .Where(MathchesMigrationIdPattern)
                .Select(migrationFile => new MigrationFromFile(migrationFile))
                .ToList();
        }

        static bool MathchesMigrationIdPattern(string filePath)
        {
            var migrationId = MigrationId.GetMigrationId(filePath, throwOnError: false);

            return migrationId != null;
        }

        class MigrationId
        {
            public static MigrationId GetMigrationId(string filePath, bool throwOnError = true)
            {
                var extension = Path.GetExtension(filePath);

                if (!string.Equals(extension, ".sql", StringComparison.OrdinalIgnoreCase))
                {
                    return null;
                }

                var fileName = Path.GetFileNameWithoutExtension(filePath);

                if (fileName == null)
                {
                    return null;
                }

                var tokens = fileName.Split('-');
                int sequenceNumber;

                if (!int.TryParse(tokens.First(), out sequenceNumber))
                {
                    return null;
                }

                return new MigrationId(sequenceNumber, string.Join("-", tokens.Skip(1)));
            }

            public int SequenceNumber { get; }
            public string BranchSpecification { get; }

            public MigrationId(int sequenceNumber, string branchSpecification)
            {
                SequenceNumber = sequenceNumber;
                BranchSpecification = branchSpecification;
            }

            public string GetPureId()
            {
                return $"{SequenceNumber}-{BranchSpecification}";
            }
        }

        class MigrationFromFile : IExecutableSqlMigration, ISqlMigration
        {
            public MigrationFromFile(string migrationFilePath)
            {
                MigrationFilePath = migrationFilePath;
                var migrationId = MigrationId.GetMigrationId(migrationFilePath);

                Id = migrationId.GetPureId();
                SequenceNumber = migrationId.SequenceNumber;
                BranchSpecification = migrationId.BranchSpecification;

                var lines = File.ReadAllLines(migrationFilePath);

                Description = ExtractDescription(lines);
                Sql = ExtractMigration(lines);
                SqlMigration = this;
            }

            static string ExtractDescription(string[] lines)
            {
                var commentLines = lines.TakeWhile(IsPartOfComments);

                return string.Join(Environment.NewLine,
                    commentLines
                        .Where(line => !string.IsNullOrWhiteSpace(line))
                        .Select(line => line.TrimStart(' ', '-')));
            }

            static string ExtractMigration(IEnumerable<string> lines)
            {
                var sqlLines = lines.SkipWhile(IsPartOfComments);

                return string.Join(Environment.NewLine, sqlLines);
            }

            static bool IsPartOfComments(string line)
            {
                return string.IsNullOrWhiteSpace(line)
                       || line.TrimStart().StartsWith("--");
            }

            public string Id { get; }
            public string Sql { get; }
            public string Description { get; }
            public int SequenceNumber { get; }
            public string BranchSpecification { get; }
            public ISqlMigration SqlMigration { get; }
            public string MigrationFilePath { get; }
        }
    }
}
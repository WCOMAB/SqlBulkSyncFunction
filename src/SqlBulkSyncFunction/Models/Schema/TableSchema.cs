using System;
using Microsoft.Data.SqlClient;
using SqlBulkSyncFunction.Helpers;

namespace SqlBulkSyncFunction.Models.Schema
{
    public record TableSchema
    {
        public string TableName { get; }
        public Column[] Columns { get; }
        public string SyncNewOrUpdatedTableName { get; }
        public string SyncDeletedTableName { get; }
        public string DropNewOrUpdatedTableStatement { get; }
        public string DropDeletedTableStatement { get; }
        public string MergeNewOrUpdateStatement { get; }
        public string DeleteStatement { get; }
        public string SourceNewOrUpdatedSelectStatement { get; }
        public string SourceDeletedSelectStatement { get; }
        public string CreateNewOrUpdatedSyncTableStatement { get; }
        public string CreateDeletedSyncTableStatement { get; }
        public TableVersion SourceVersion { get; }
        public TableVersion TargetVersion { get; }
        public int BatchSize { get; }
        private TableSchema(
            string tableName,
            Column[] columns,
            TableVersion sourceVersion,
            TableVersion targetVersion,
            int? batchSize
            )
        {
            var bufferName = tableName.Replace("[", "").Replace("]", "");

            TableName = tableName;
            SyncNewOrUpdatedTableName = $"sync.[{bufferName}_{Guid.NewGuid()}]";
            SyncDeletedTableName = $"sync.[{bufferName}_{Guid.NewGuid()}]";
            Columns = columns;
            SourceVersion = sourceVersion;
            TargetVersion = targetVersion;

            CreateNewOrUpdatedSyncTableStatement = this.GetCreateNewOrUpdatedSyncTableStatement();
            CreateDeletedSyncTableStatement = this.GetCreateDeletedSyncTableStatement();

            SourceNewOrUpdatedSelectStatement = this.GetNewOrUpdatedAtSourceSelectStatement();
            SourceDeletedSelectStatement = this.GetDeletedAtSourceSelectStatement();
            MergeNewOrUpdateStatement = this.GetNewOrUpdatedMergeStatement();
            DeleteStatement = this.GetDeleteStatement();
            DropNewOrUpdatedTableStatement = SyncNewOrUpdatedTableName.GetDropStatement();
            DropDeletedTableStatement = SyncDeletedTableName.GetDropStatement();
            BatchSize = batchSize ?? 1000;
        }


        public static TableSchema LoadSchema(
            SqlConnection sourceConn,
            SqlConnection targetConn,
            string tableName,
            int? batchSize,
            bool globalChangeTracking
            )
        {
            var columns = sourceConn.GetColumns(tableName);
            var targetVersion = targetConn.GetTargetVersion(tableName);
            return new TableSchema(
                tableName,
                columns,
                sourceConn.GetSourceVersion(tableName, globalChangeTracking, columns),
                targetVersion,
                batchSize
                );
        }
    }
}
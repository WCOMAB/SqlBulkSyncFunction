using System;
using Microsoft.Data.SqlClient;
using SqlBulkSyncFunction.Helpers;
using SqlBulkSyncFunction.Models.Job;

namespace SqlBulkSyncFunction.Models.Schema
{
    public record TableSchema
    {
        public string Scope { get; }
        public string SourceTableName { get; }
        public string TargetTableName { get; }
        public Column[] Columns { get; }
        public string SyncNewOrUpdatedTableName { get; }
        public string SyncDeletedTableName { get; }
        public string DropNewOrUpdatedTableStatement { get; }
        public string DropDeletedTableStatement { get; }
        public string MergeNewOrUpdateStatement { get; }
        public string DeleteStatement { get; }
        public string SourceNewOrUpdatedSelectStatement { get; }
        public string SourceDeletedSelectStatement { get; }
        public string SourceSelectAllStatement { get; }
        public string CreateNewOrUpdatedSyncTableStatement { get; }
        public string CreateDeletedSyncTableStatement { get; }
        public string TruncateTargetTableStatement { get; }
        public TableVersion SourceVersion { get; }
        public TableVersion TargetVersion { get; }
        public int BatchSize { get; }
        private TableSchema(
            SyncJobTable table,
            Column[] columns,
            TableVersion sourceVersion,
            TableVersion targetVersion,
            int? batchSize
            )
        {
            var bufferName = table.Target.Replace("[", "").Replace("]", "");

            Scope = string.Concat(
                table.Source,
                " to ",
                table.Target
            );

            SourceTableName = table.Source;
            TargetTableName = table.Target;
            SyncNewOrUpdatedTableName = $"sync.[{bufferName}_{Guid.NewGuid()}]";
            SyncDeletedTableName = $"sync.[{bufferName}_{Guid.NewGuid()}]";
            Columns = columns;
            SourceVersion = sourceVersion;
            TargetVersion = targetVersion;

            CreateNewOrUpdatedSyncTableStatement = this.GetCreateNewOrUpdatedSyncTableStatement();
            CreateDeletedSyncTableStatement = this.GetCreateDeletedSyncTableStatement();

            SourceNewOrUpdatedSelectStatement = this.GetNewOrUpdatedAtSourceSelectStatement();
            SourceSelectAllStatement = this.GetSourceSelectAllStatement();
            SourceDeletedSelectStatement = this.GetDeletedAtSourceSelectStatement();
            MergeNewOrUpdateStatement = this.GetNewOrUpdatedMergeStatement();
            DeleteStatement = this.GetDeleteStatement();
            DropNewOrUpdatedTableStatement = SyncNewOrUpdatedTableName.GetDropStatement();
            DropDeletedTableStatement = SyncDeletedTableName.GetDropStatement();
            TruncateTargetTableStatement = this.GetTruncateTargetTableStatement();
            BatchSize = batchSize ?? 1000;
        }


        public static TableSchema LoadSchema(
            SqlConnection sourceConn,
            SqlConnection targetConn,
            SyncJobTable syncTable,
            int? batchSize,
            bool globalChangeTracking
            )
        {
            var columns = sourceConn.GetColumns(syncTable.Source);
            var targetVersion = targetConn.GetTargetVersion(syncTable.Target);
            return new TableSchema(
                syncTable,
                columns,
                sourceConn.GetSourceVersion(syncTable.Source, globalChangeTracking, columns),
                targetVersion,
                batchSize
                );
        }
    }
}

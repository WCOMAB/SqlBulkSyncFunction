namespace SqlBulkSyncFunction.Models;

public enum SyncJobProgressState
{
    Created,
    Started,
    Done,
    Expired,
    Exception
}

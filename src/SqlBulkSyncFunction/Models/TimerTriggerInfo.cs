namespace SqlBulkSyncFunction.Models
{
    // ReSharper disable once UnusedMember.Global
    public record TimerTriggerInfo(TimerTriggerStatus ScheduleStatus, bool IsPastDue);
}
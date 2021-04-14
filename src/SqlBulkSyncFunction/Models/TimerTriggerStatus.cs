using System;

namespace SqlBulkSyncFunction.Models
{
    // ReSharper disable once UnusedMember.Global
    public record TimerTriggerStatus(DateTimeOffset Last, DateTimeOffset Next, DateTimeOffset LastUpdated);
}

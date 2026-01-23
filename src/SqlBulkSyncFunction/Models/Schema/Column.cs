namespace SqlBulkSyncFunction.Models.Schema
{
    public record Column
    {
        public string Name { get; set; }
        public string QuoteName { get; set; }
        public string Type { get; set; }
        public bool IsIdentity { get; set; }
        public bool IsPrimary { get; set; }
        public bool IsNullable { get; set; }
        public string Collation { get; set; }
    }
}

namespace OracleSqlPortal.Models
{
    public class QueryHistory
    {
        public string   Username    { get; set; } = "";
        public string   DisplayName { get; set; } = "";
        public string   Environment { get; set; } = "";
        public string   Sql         { get; set; } = "";
        public string   ClientIp    { get; set; } = "";
        public DateTime ExecutedAt  { get; set; } = DateTime.Now;
        public int      Rows        { get; set; }
        public long     DurationMs  { get; set; }
        public bool     IsError     { get; set; }
    }
}

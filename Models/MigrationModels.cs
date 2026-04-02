namespace OracleSqlPortal.Models
{
    public class MigrationStatementResult
    {
        public int     LineNumber   { get; set; }
        public string  Statement   { get; set; } = "";
        public string  Preview     { get; set; } = "";  // first 120 chars
        public bool    Success     { get; set; }
        public string? Error       { get; set; }
        public int     RowsAffected{ get; set; }
        public long    DurationMs  { get; set; }
    }

    public class MigrationSession
    {
        public string  SessionId   { get; set; } = Guid.NewGuid().ToString("N")[..12];
        public string  FileName    { get; set; } = "";
        public string  Environment { get; set; } = "";
        public string  Username    { get; set; } = "";
        public string  ClientIp    { get; set; } = "";
        public List<MigrationStatementResult> Results { get; set; } = new();
        public bool    Committed   { get; set; }
        public bool    RolledBack  { get; set; }
        public DateTime StartedAt  { get; set; } = DateTime.Now;
    }

    public class MigrationHistory
    {
        public string  Id          { get; set; } = "";
        public string  Username    { get; set; } = "";
        public string  DisplayName { get; set; } = "";
        public string  Environment { get; set; } = "";
        public string  FileName    { get; set; } = "";
        public string  ClientIp    { get; set; } = "";
        public int     TotalStmts  { get; set; }
        public int     OkStmts     { get; set; }
        public int     ErrStmts    { get; set; }
        public string  Outcome     { get; set; } = "";  // Committed | RolledBack
        public DateTime ExecutedAt { get; set; }
    }
}

namespace OracleSqlPortal.Models
{
    public class ErrorLog
    {
        public string? Username { get; set; }
        public string Method { get; set; } = "";
        public string Message { get; set; } = "";
        public string StackTrace { get; set; } = "";
        public string? Extra { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
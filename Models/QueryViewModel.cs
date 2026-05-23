using System.Data;

namespace OracleSqlPortal.Models
{
    // ─── SQL / Query ─────────────────────────────────────────────
    public class QueryViewModel
    {
        public string Sql { get; set; } = "";
        public DataTable? Result { get; set; }
        public string? Message { get; set; }
        public bool IsError { get; set; }
        public string SelectedEnv { get; set; } = "DEV";
        public long ElapsedMs { get; set; }
        public int RowCount { get; set; }
    }

    // ─── Sql Ops ──────────────────────────────────────────────────
    public static class SqlOps
    {
        public static readonly string[] All = new[]
        {
            "SELECT", "INSERT", "UPDATE", "DELETE", "TRUNCATE",
            "CREATE", "DROP", "ALTER", "EXECUTE", "VIEW_SP", "MIGRATION"
        };
    }

    // ─── App User ─────────────────────────────────────────────────
    public class AppUser
    {
        public string Username    { get; set; } = "";
        public string Password    { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public string Email       { get; set; } = "";
        public bool   IsApproved  { get; set; } = true;
        public bool   IsAdmin     { get; set; } = false;
        public bool   IsMigration { get; set; } = false;
        public Dictionary<string, List<string>> Permissions { get; set; } = new();
    }

    // ─── Signup / Forgot Password ─────────────────────────────────
    public class SignupViewModel
    {
        public string Username    { get; set; } = "";
        public string Password    { get; set; } = "";
        public string Confirm     { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public string Email       { get; set; } = "";
        public string? Error      { get; set; }
        public string? Success    { get; set; }
    }

    public class ForgotPasswordViewModel
    {
        public string Email    { get; set; } = "";
        public string? Error   { get; set; }
        public string? Success { get; set; }
    }

    public class ResetPasswordViewModel
    {
        public string Token    { get; set; } = "";
        public string Password { get; set; } = "";
        public string Confirm  { get; set; } = "";
        public string? Error   { get; set; }
        public string? Success { get; set; }
    }

    // ─── Access Request ───────────────────────────────────────────
    public class AccessRequest
    {
        public string Id          { get; set; } = Guid.NewGuid().ToString("N")[..8];
        public string Username    { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public string Env         { get; set; } = "";
        public string Operation   { get; set; } = "";
        public string Reason      { get; set; } = "";
        public string Status      { get; set; } = "Pending";
        public DateTime RequestedAt { get; set; } = DateTime.Now;
        public string? AdminNote  { get; set; }
    }

    // ─── Login History ────────────────────────────────────────────
    public class LoginHistory
    {
        public string Username  { get; set; } = "";
        public DateTime LoginAt { get; set; } = DateTime.Now;
        public string IpAddress { get; set; } = "";
        public bool   Success   { get; set; } = true;
    }

    // ─── Admin Activity ───────────────────────────────────────────
    public class AdminActivity
    {
        public long     Id          { get; set; }
        public string   AdminUser   { get; set; } = "";
        public string   Action      { get; set; } = "";
        public string   Target      { get; set; } = "";
        public string   Details     { get; set; } = "";
        public DateTime PerformedAt { get; set; } = DateTime.Now;
    }
    public class ErrorLog
    {
        public string? Username { get; set; }
        public string Method { get; set; } = "";
        public string Message { get; set; } = "";
        public string StackTrace { get; set; } = "";
        public string? Extra { get; set; }
        public DateTime CreatedAt { get; set; }
    }
    // ─── Admin view model ─────────────────────────────────────────
    public class AdminConfigViewModel
    {
        public Dictionary<string, EnvConfig> Environments   { get; set; } = new();
        public List<AppUser>       Users           { get; set; } = new();
        public List<AppUser>       PendingUsers    { get; set; } = new();
        public List<AccessRequest> AccessRequests  { get; set; } = new();
        public List<LoginHistory>  LoginHistory    { get; set; } = new();
        public List<QueryHistory>  QueryHistory    { get; set; } = new();
        public List<MigrationHistory> MigrationHistory { get; set; } = new();
        public List<AdminActivity> AdminActivity   { get; set; } = new();
       public List<ErrorLog> ErrorLog { get; set; } = new();
        public List<UserFeedback>  Feedbacks       { get; set; } = new();
        public List<PopupNotification> Popups      { get; set; } = new();
        public string  MigrationFolder  { get; set; } = "";
        public string  PortalDbConnStr  { get; set; } = "";
        public string  PortalDbDesc     { get; set; } = "";
        public string? SuccessMessage   { get; set; }
        public string? ErrorMessage     { get; set; }
        public string  ActiveTab        { get; set; } = "approvals";
        public string  PdbHost         { get; set; } = "";
        public string  PdbPort         { get; set; } = "1521";
        public string  PdbService      { get; set; } = "";
        public string  PdbUser         { get; set; } = "";
        public string  PdbPassword     { get; set; } = "";
        public string  PdbProtocol     { get; set; } = "TCP";
        public string  PdbSqlNetOraPath { get; set; } = "";
        public string  PdbWalletPath   { get; set; } = "";
        public string  PdbTrustStore   { get; set; } = "";
        public string  PdbKeyStore     { get; set; } = "";
        public string  PdbKeyStorePwd  { get; set; } = "";
        public string  PdbServerDN     { get; set; } = "";
    }

    // ─── Auth ─────────────────────────────────────────────────────
    public class LoginViewModel
    {
        public string Username { get; set; } = "";
        public string Password { get; set; } = "";
        public string? Error   { get; set; }
    }
}

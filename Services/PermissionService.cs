using OracleSqlPortal.Models;

namespace OracleSqlPortal.Services
{
    public class PermissionService
    {
        private readonly PortalDbService _db;
        public PermissionService(PortalDbService db) => _db = db;

        public AppUser? FindUser(string username, string password) => _db.FindUser(username, password);
        public AppUser? GetUser(string username) => _db.GetUser(username);
        public List<AppUser> GetAllUsers() => _db.GetAllUsers();
        public List<AppUser> GetPendingUsers() => _db.GetPendingUsers();
        public bool UsernameExists(string username) => _db.UsernameExists(username);
        public bool EmailExists(string email) => _db.EmailExists(email);

        public void AddPendingUser(AppUser user) { user.IsApproved = false; _db.AddUser(user); }
        public bool ApproveUser(string username) { _db.SetApproved(username, true); return true; }
        public bool RejectUser(string username) { _db.DeleteUser(username); return true; }
        public bool ResetPassword(string u, string p) { _db.ResetPassword(u, p); return true; }

        public bool CanExecute(string username, string env, string sql)
            => _db.UserHasPermission(username, env, DetectOperation(sql));

        public bool UserHasViewSpPermission(string username)
            => _db.UserHasViewSpPermission(username);

        public List<string> GetUserPermissions(string username, string env)
            => _db.GetUserEnvPermissions(username, env);

        public void SaveUserPermissions(string username, Dictionary<string, List<string>> permissions)
            => _db.SaveUserPermissions(username, permissions);

        public static string DetectOperation(string sql)
        {
            string s = sql.TrimStart().ToUpperInvariant();
            if (s.StartsWith("SELECT")) return "SELECT";
            if (s.StartsWith("INSERT")) return "INSERT";
            if (s.StartsWith("UPDATE")) return "UPDATE";
            if (s.StartsWith("DELETE")) return "DELETE";
            if (s.StartsWith("TRUNCATE")) return "TRUNCATE";
            if (s.StartsWith("CREATE OR REPLACE") || s.Contains("PROCEDURE") ||
                s.Contains("FUNCTION") || s.Contains("PACKAGE")) return "VIEW_SP";
            if (s.StartsWith("CREATE")) return "CREATE";
            if (s.StartsWith("DROP")) return "DROP";
            if (s.StartsWith("ALTER")) return "ALTER";
            if (s.StartsWith("EXEC") || s.StartsWith("CALL") || s.StartsWith("BEGIN")) return "EXECUTE";
            return "UNKNOWN";
        }
    }
}
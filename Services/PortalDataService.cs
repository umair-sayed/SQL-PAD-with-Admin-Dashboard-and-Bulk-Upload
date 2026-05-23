using OracleSqlPortal.Models;

namespace OracleSqlPortal.Services
{
    /// <summary>
    /// Facade over PortalDbService (Oracle DB).
    /// ALL data — users, permissions, access requests, reset tokens, audit logs —
    /// now lives in Oracle DB. AppSettingsDataService is no longer used here.
    /// </summary>
    public class PortalDataService
    {
        private readonly PortalDbService _db;

        public PortalDataService(PortalDbService db) => _db = db;

        // ── Login audit (Oracle DB) ──────────────────────────────
        public void RecordLogin(string username, string ip, bool success)
            => _db.RecordLogin(username, ip, success);
        public List<LoginHistory> GetLoginHistory(int n = 500)
            => _db.GetLoginHistory(n);

        // ── Query audit (Oracle DB) ──────────────────────────────
        public void RecordQuery(QueryHistory q) => _db.RecordQuery(q);
        public List<QueryHistory> GetQueryHistory(int n = 500)
            => _db.GetQueryHistory(n);

        // ── Access requests (Oracle DB) ──────────────────────────
        public void AddAccessRequest(AccessRequest r) => _db.AddAccessRequest(r);
        public List<AccessRequest> GetAccessRequests() => _db.GetAccessRequests();
        public bool UpdateAccessRequest(string id, string status, string? note)
            => _db.UpdateAccessRequest(id, status, note);

        // ── Reset tokens (Oracle DB) ─────────────────────────────
        public string CreateResetToken(string username) => _db.CreateResetToken(username);
        public string? ValidateResetToken(string token) => _db.ValidateResetToken(token);
        public void InvalidateResetToken(string username) => _db.InvalidateResetToken(username);
    }
}
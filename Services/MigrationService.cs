using Oracle.ManagedDataAccess.Client;
using OracleSqlPortal.Models;
using System.Collections.Concurrent;

namespace OracleSqlPortal.Services
{
    /// <summary>
    /// Executes migration SQL files statement-by-statement within an open Oracle transaction.
    /// Sessions are held in memory until commit/rollback.
    /// Migration history is persisted to the portal Oracle DB.
    /// </summary>
    public class MigrationService
    {
        private readonly OracleService   _oracle;
        private readonly PortalDbService _db;

        // In-memory sessions: sessionId → (connection, transaction, session-object)
        private static readonly ConcurrentDictionary<string, MigrationSessionState> _sessions = new();

        public MigrationService(OracleService oracle, PortalDbService db)
        { _oracle = oracle; _db = db; }

        // ── Start a new migration session ─────────────────────────
        public (MigrationSession session, string? error) StartSession(
            string env, string username, string displayName, string clientIp,
            string fileName, string sqlContent)
        {
            var session = new MigrationSession
            {
                Environment = env,
                Username    = username,
                FileName    = fileName,
                ClientIp    = clientIp,
                StartedAt   = DateTime.Now
            };

            try
            {
                // Parse statements
                var stmts = SqlMigrationParser.ParseStatements(sqlContent);

                // Open a dedicated connection + begin transaction
                var connStr = _oracle.BuildConnStr(env);
                var conn    = new OracleConnection(connStr);
                conn.Open();
                var tx = conn.BeginTransaction();

                // Execute each statement
                foreach (var (stmt, approxLine) in stmts)
                {
                    var res = new MigrationStatementResult
                    {
                        LineNumber = approxLine,
                        Statement  = stmt,
                        Preview    = stmt.Length > 120 ? stmt[..120] + "…" : stmt
                    };

                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    try
                    {
                        using var cmd = new OracleCommand(stmt, conn) { Transaction = tx, CommandTimeout = 120 };
                        res.RowsAffected = cmd.ExecuteNonQuery();
                        res.Success      = true;
                    }
                    catch (Exception ex)
                    {
                        res.Success = false;
                        res.Error   = ex.Message;
                    }
                    sw.Stop();
                    res.DurationMs = sw.ElapsedMilliseconds;
                    session.Results.Add(res);
                }

                // Store open session (don't commit yet)
                _sessions[session.SessionId] = new MigrationSessionState
                {
                    Connection  = conn,
                    Transaction = tx,
                    Session     = session,
                    DisplayName = displayName
                };

                return (session, null);
            }
            catch (Exception ex)
            {
                return (session, ex.Message);
            }
        }

        // ── Commit ────────────────────────────────────────────────
        public (bool ok, string? error) Commit(string sessionId)
        {
            if (!_sessions.TryRemove(sessionId, out var state))
                return (false, "Session not found or already finalised.");
            try
            {
                state.Transaction.Commit();
                state.Connection.Dispose();
                state.Session.Committed = true;

                _db.RecordMigration(state.Session, state.DisplayName, "Committed");
                return (true, null);
            }
            catch (Exception ex)
            {
                state.Connection.Dispose();
                return (false, ex.Message);
            }
        }

        // ── Rollback ──────────────────────────────────────────────
        public (bool ok, string? error) Rollback(string sessionId)
        {
            if (!_sessions.TryRemove(sessionId, out var state))
                return (false, "Session not found or already finalised.");
            try
            {
                state.Transaction.Rollback();
                state.Connection.Dispose();
                state.Session.RolledBack = true;

                _db.RecordMigration(state.Session, state.DisplayName, "RolledBack");
                return (true, null);
            }
            catch (Exception ex)
            {
                state.Connection.Dispose();
                return (false, ex.Message);
            }
        }

        // ── Get active session (for results display) ──────────────
        public MigrationSession? GetSession(string sessionId)
            => _sessions.TryGetValue(sessionId, out var s) ? s.Session : null;

        // ── History ───────────────────────────────────────────────
        public List<MigrationHistory> GetHistory(int count = 200)
            => _db.GetMigrationHistory(count);

        private class MigrationSessionState
        {
            public OracleConnection   Connection  { get; set; } = null!;
            public OracleTransaction  Transaction { get; set; } = null!;
            public MigrationSession   Session     { get; set; } = null!;
            public string             DisplayName { get; set; } = "";
        }
    }
}

using Microsoft.AspNetCore.Mvc;
using OracleSqlPortal.Models;
using OracleSqlPortal.Services;

namespace OracleSqlPortal.Controllers
{
    public class QueryController : Controller
    {
        private readonly OracleService       _oracle;
        private readonly PermissionService   _perms;
        private readonly QueryHistoryService _history;
        private readonly PortalDbService     _db;

        public QueryController(OracleService oracle, PermissionService perms,
            QueryHistoryService history, PortalDbService db)
        { _oracle = oracle; _perms = perms; _history = history; _db = db; }

        private string? CurrentUser  => HttpContext.Session.GetString("username");
        private string  DisplayName  => HttpContext.Session.GetString("displayname") ?? CurrentUser ?? "";
        private bool    IsAdmin      => HttpContext.Session.GetString("isadmin") == "1";
        private string  ClientIp     => HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        public IActionResult Index()
        {
            if (CurrentUser == null) return RedirectToAction("Login", "Auth");

            // Version check
            var version = _db.GetAppVersion();
            if (version != null && version.IsExpired)
            {
                ViewBag.VersionExpired = true;
                ViewBag.VersionNumber = version.VersionNumber;
                ViewBag.ExpiryDate = version.ExpiryDate.ToString("dd-MMM-yyyy");
            }

            var envConfigs = _oracle.GetAllEnvConfigs();
            ViewBag.UserPerms     = GetPermsMapForEnvs(envConfigs.Keys.ToList());
            ViewBag.IsAdminUser   = IsAdmin;
            ViewBag.AccessSuccess = TempData["AccessSuccess"];
            ViewBag.EnvConfigs    = envConfigs;
            ViewBag.AppVersion    = version;
            return View(new QueryViewModel { SelectedEnv = envConfigs.Keys.FirstOrDefault() ?? "" });
        }

        [HttpPost]
        public IActionResult Run(QueryViewModel model)
        {
            if (CurrentUser == null) return RedirectToAction("Login", "Auth");

            // Version check
            var version = _db.GetAppVersion();
            if (version != null && version.IsExpired)
            {
                model.IsError = true;
                model.Message = $"⛔ QueryPad version {version.VersionNumber} has expired on {version.ExpiryDate:dd-MMM-yyyy}. New build is to be updated in QueryPad, kindly contact admin.";
                var envConfigsExpired = _oracle.GetAllEnvConfigs();
                ViewBag.UserPerms   = GetPermsMapForEnvs(envConfigsExpired.Keys.ToList());
                ViewBag.IsAdminUser = IsAdmin;
                ViewBag.EnvConfigs  = envConfigsExpired;
                return View("Index", model);
            }

            var envConfigs = _oracle.GetAllEnvConfigs();
            ViewBag.UserPerms   = GetPermsMapForEnvs(envConfigs.Keys.ToList());
            ViewBag.IsAdminUser = IsAdmin;
            ViewBag.EnvConfigs  = envConfigs;
            ViewBag.AppVersion  = version;

            var entry = new QueryHistory
            {
                Username    = CurrentUser,
                DisplayName = DisplayName,
                Environment = model.SelectedEnv,
                Sql         = model.Sql ?? "",
                ClientIp    = ClientIp,
                ExecutedAt  = DateTime.Now
            };

            try
            {
                string rawSql = (model.Sql ?? "").Trim();
                if (string.IsNullOrWhiteSpace(rawSql))
                { model.IsError = true; model.Message = "Please enter SQL."; return View("Index", model); }

                // Split multi-statement DML (semicolon separated, preserve CREATE blocks)
                var statements = SplitSqlStatements(rawSql);

                if (statements.Count == 1)
                {
                    string sql = statements[0];
                    string op = PermissionService.DetectOperation(sql);

                    if (!_perms.CanExecute(CurrentUser!, model.SelectedEnv, sql))
                    {
                        model.IsError = true;
                        model.Message = $"⛔ Access Denied — {op} not permitted on {model.SelectedEnv}";
                        entry.IsError = true;
                        _history.Add(entry);
                        return View("Index", model);
                    }

                    if (op == "SELECT")
                    {
                        var (dt, ms)     = _oracle.ExecuteQueryAsync(sql, model.SelectedEnv);
                        model.Result     = dt;
                        model.ElapsedMs  = ms;
                        model.RowCount   = dt.Rows.Count;
                        model.Message    = $"{model.RowCount} rows returned in {ms} ms";
                        entry.Rows       = model.RowCount;
                        entry.DurationMs = ms;
                    }
                    else
                    {
                        var (rows, ms)   = _oracle.ExecuteNonQuery(sql, model.SelectedEnv);
                        model.ElapsedMs  = ms;
                        model.Message    = $"{rows} rows affected in {ms} ms";
                        entry.Rows       = rows;
                        entry.DurationMs = ms;
                    }
                }
                else
                {
                    // Multi-statement execution
                    int totalRows = 0; long totalMs = 0; int stmtCount = 0;
                    foreach (var stmt in statements)
                    {
                        if (string.IsNullOrWhiteSpace(stmt)) continue;
                        string op = PermissionService.DetectOperation(stmt);
                        if (!_perms.CanExecute(CurrentUser!, model.SelectedEnv, stmt))
                        {
                            model.IsError = true;
                            model.Message = $"⛔ Statement {stmtCount + 1}: {op} not permitted on {model.SelectedEnv}";
                            entry.IsError = true;
                            _history.Add(entry);
                            return View("Index", model);
                        }
                        var (rows, ms) = _oracle.ExecuteNonQuery(stmt, model.SelectedEnv);
                        totalRows += rows; totalMs += ms; stmtCount++;
                    }
                    model.ElapsedMs = totalMs;
                    model.Message = $"{stmtCount} statements executed, {totalRows} total rows affected in {totalMs} ms";
                    entry.Rows = totalRows;
                    entry.DurationMs = totalMs;
                }
            }
            catch (Exception ex)
            {
                model.IsError = true;
                model.Message = ex.Message;
                entry.IsError = true;
            }

            _history.Add(entry);
            return View("Index", model);
        }

        // ── Multi-statement splitter (respects CREATE blocks) ──────
        private static List<string> SplitSqlStatements(string rawSql)
        {
            var results = new List<string>();
            string upper = rawSql.TrimStart().ToUpperInvariant();

            // If it's a CREATE/DECLARE/BEGIN block, return as single statement
            if (upper.StartsWith("CREATE") || upper.StartsWith("DECLARE") || upper.StartsWith("BEGIN"))
            {
                results.Add(rawSql.Trim());
                return results;
            }

            // Split on semicolons for DML
            var parts = rawSql.Split(';');
            foreach (var part in parts)
            {
                var s = part.Trim();
                if (!string.IsNullOrWhiteSpace(s))
                    results.Add(s);
            }
            return results;
        }

        private Dictionary<string, List<string>> GetPermsMapForEnvs(List<string> envs)
        {
            var map = new Dictionary<string, List<string>>();
            foreach (var env in envs)
                map[env] = _perms.GetUserPermissions(CurrentUser!, env);
            return map;
        }
    }
}

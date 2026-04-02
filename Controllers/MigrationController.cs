using Microsoft.AspNetCore.Mvc;
using OracleSqlPortal.Models;
using OracleSqlPortal.Services;

namespace OracleSqlPortal.Controllers
{
    /// <summary>
    /// Migration controller.
    /// A user can access migration if:
    ///   (a) they are an admin (IsAdmin = true), OR
    ///   (b) they have been granted migration access (IsMigration = true)
    ///
    /// There is a SINGLE control in Admin > Users: "Grant/Revoke Migration Access"
    /// No separate "Grant Migration" button and "Migration Privilege" toggle needed.
    /// </summary>
    public class MigrationController : Controller
    {
        private readonly MigrationService _migration;
        private readonly PortalDbService _db;
        private readonly OracleService _oracle;
        private readonly IConfiguration _config;

        public MigrationController(MigrationService migration, PortalDbService db,
            OracleService oracle, IConfiguration config)
        { _migration = migration; _db = db; _oracle = oracle; _config = config; }

        private string? CurrentUser => HttpContext.Session.GetString("username");
        private string DisplayName => HttpContext.Session.GetString("displayname") ?? CurrentUser ?? "";
        private bool IsAdmin => HttpContext.Session.GetString("isadmin") == "1";
        private string ClientIp => HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        /// <summary>
        /// Unified migration access check.
        /// Admin always passes. Non-admin must have IsMigration = true.
        /// </summary>
        private bool CanMigrate()
        {
            if (CurrentUser == null) return false;
            if (IsAdmin) return true;
            return _db.IsMigrationUser(CurrentUser);
        }

        [HttpGet]
        public IActionResult Index()
        {
            if (CurrentUser == null) return RedirectToAction("Login", "Auth");
            if (!CanMigrate()) return View("Denied");

            ViewBag.Environments = _oracle.GetAllEnvConfigs().Keys.ToList();
            ViewBag.MigrationFolder = _config["MigrationFolder"] ?? "";
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> Execute(IFormFile sqlFile, string env)
        {
            if (CurrentUser == null) return Json(new { ok = false, error = "Not logged in." });
            if (!CanMigrate()) return Json(new { ok = false, error = "Access denied. You do not have migration privileges." });

            if (sqlFile == null || sqlFile.Length == 0)
                return Json(new { ok = false, error = "No file uploaded." });

            string fileName = Path.GetFileName(sqlFile.FileName);

            string migFolder = _config["MigrationFolder"] ?? "";
            if (!string.IsNullOrWhiteSpace(migFolder))
            {
                try
                {
                    if (!Directory.Exists(migFolder)) Directory.CreateDirectory(migFolder);
                    string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                    string savePath = Path.Combine(migFolder, $"{stamp}_{CurrentUser}_{fileName}");
                    using var fs = new FileStream(savePath, FileMode.Create);
                    await sqlFile.CopyToAsync(fs);
                }
                catch { /* non-fatal */ }
            }

            string sqlContent;
            using (var sr = new StreamReader(sqlFile.OpenReadStream()))
                sqlContent = await sr.ReadToEndAsync();

            var (session, connError) = _migration.StartSession(
                env, CurrentUser, DisplayName, ClientIp, fileName, sqlContent);

            if (connError != null)
                return Json(new { ok = false, error = connError });

            return Json(new
            {
                ok = true,
                sessionId = session.SessionId,
                results = session.Results.Select(r => new
                {
                    lineNumber = r.LineNumber,
                    preview = r.Preview,
                    success = r.Success,
                    error = r.Error,
                    rowsAffected = r.RowsAffected,
                    durationMs = r.DurationMs
                })
            });
        }

        [HttpPost]
        public IActionResult Commit(string sessionId)
        {
            if (CurrentUser == null) return Json(new { ok = false, error = "Not logged in." });
            if (!CanMigrate()) return Json(new { ok = false, error = "Access denied." });
            var (ok, err) = _migration.Commit(sessionId);
            return Json(new { ok, error = err });
        }

        [HttpPost]
        public IActionResult Rollback(string sessionId)
        {
            if (CurrentUser == null) return Json(new { ok = false, error = "Not logged in." });
            if (!CanMigrate()) return Json(new { ok = false, error = "Access denied." });
            var (ok, err) = _migration.Rollback(sessionId);
            return Json(new { ok, error = err });
        }

        [HttpGet]
        public IActionResult History()
        {
            if (!IsAdmin) return Json(new { ok = false });
            return Json(_migration.GetHistory(300).Select(h => new
            {
                h.Id,
                h.Username,
                h.DisplayName,
                h.Environment,
                h.FileName,
                h.ClientIp,
                h.TotalStmts,
                h.OkStmts,
                h.ErrStmts,
                h.Outcome,
                executedAt = h.ExecutedAt.ToString("yyyy-MM-dd HH:mm:ss")
            }));
        }
    }
}
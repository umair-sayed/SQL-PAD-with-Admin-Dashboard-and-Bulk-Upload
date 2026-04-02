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

        public QueryController(OracleService oracle, PermissionService perms, QueryHistoryService history)
        { _oracle = oracle; _perms = perms; _history = history; }

        private string? CurrentUser  => HttpContext.Session.GetString("username");
        private string  DisplayName  => HttpContext.Session.GetString("displayname") ?? CurrentUser ?? "";
        private bool    IsAdmin      => HttpContext.Session.GetString("isadmin") == "1";
        private string  ClientIp     => HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        public IActionResult Index()
        {
            if (CurrentUser == null) return RedirectToAction("Login", "Auth");
            var envConfigs = _oracle.GetAllEnvConfigs();
            ViewBag.UserPerms     = GetPermsMapForEnvs(envConfigs.Keys.ToList());
            ViewBag.IsAdminUser   = IsAdmin;
            ViewBag.AccessSuccess = TempData["AccessSuccess"];
            ViewBag.EnvConfigs    = envConfigs;
            return View(new QueryViewModel { SelectedEnv = envConfigs.Keys.FirstOrDefault() ?? "" });
        }

        [HttpPost]
        public IActionResult Run(QueryViewModel model)
        {
            if (CurrentUser == null) return RedirectToAction("Login", "Auth");
            var envConfigs = _oracle.GetAllEnvConfigs();
            ViewBag.UserPerms   = GetPermsMapForEnvs(envConfigs.Keys.ToList());
            ViewBag.IsAdminUser = IsAdmin;
            ViewBag.EnvConfigs  = envConfigs;

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
                //string sql = (model.Sql ?? "").Trim();
                string sql = (model.Sql ?? "").Trim().TrimEnd(';');
                if (string.IsNullOrWhiteSpace(sql))
                { model.IsError = true; model.Message = "Please enter SQL."; return View("Index", model); }

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
                    var (dt, ms)     = _oracle.ExecuteQuery(sql, model.SelectedEnv);
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
            catch (Exception ex)
            {
                model.IsError = true;
                model.Message = ex.Message;
                entry.IsError = true;
            }

            _history.Add(entry);
            return View("Index", model);
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

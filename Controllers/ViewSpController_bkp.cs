using Microsoft.AspNetCore.Mvc;
using OracleSqlPortal.Services;

namespace OracleSqlPortal.Controllers
{
    public class ViewSpController : Controller
    {
        private readonly OracleService _oracle;
        private readonly PermissionService _perms;
        private readonly PortalDbService _db;

        public ViewSpController(OracleService oracle, PermissionService perms, PortalDbService db)
        { _oracle = oracle; _perms = perms; _db = db; }

        private string? CurrentUser => HttpContext.Session.GetString("username");

        private bool CanView()
        {
            if (CurrentUser == null) return false;
            return _perms.UserHasViewSpPermission(CurrentUser);
        }

        public IActionResult Index()
        {
            ViewBag.EnvConfigs = _oracle.GetAllEnvConfigs();
            if (CurrentUser == null) return RedirectToAction("Login", "Auth");
            if (!CanView())
            {
                ViewBag.Error = "Table/SP Definition access not granted for this User. Kindly request access";
                return View();
            }
            return View();
        }

        // ── AJAX: search objects with LIKE filter ─────────────────
        [HttpPost]
        public IActionResult SearchObjects(string pattern, string objectType, string env)
        {
            ViewBag.EnvConfigs = _oracle.GetAllEnvConfigs();
            if (CurrentUser == null) return Json(new { ok = false, error = "Not logged in" });
            if (!CanView()) return Json(new { ok = false, error = "Access denied" });
            if (string.IsNullOrWhiteSpace(pattern))
                return Json(new { ok = false, error = "Please enter an object name." });
            try
            {
                var objects = _db.GetObjectsLike(pattern, objectType, env);
                return Json(new { ok = true, objects });
            }
            catch (Exception ex) { return Json(new { ok = false, error = ex.Message }); }
        }

        [HttpPost]
        public IActionResult Lookup(string objectName, string env, string? objectType = null)
        {
            ViewBag.EnvConfigs = _oracle.GetAllEnvConfigs();
            if (CurrentUser == null) return RedirectToAction("Login", "Auth");
            if (!CanView())
            {
                ViewBag.Error = "Table/SP Definition access not granted for this User. Kindly request access";
                return View("Index");
            }
            if (string.IsNullOrWhiteSpace(objectName))
            {
                ViewBag.Error = "Please enter an object name.";
                return View("Index");
            }
            try
            {
                var result = _oracle.GetObjectDefinition(objectName.Trim().ToUpper(), env, objectType);
                ViewBag.ObjectName = objectName.Trim().ToUpper();
                ViewBag.Env = env;
                ViewBag.ObjectType = result.ObjectType;
                ViewBag.SqlText = result.SqlText;
                ViewBag.Found = result.Found;
                if (!result.Found)
                    ViewBag.Error = $"Object '{objectName.Trim().ToUpper()}' not found in {env}.";
            }
            catch (Exception ex)
            {
                ViewBag.Error = ex.Message;
                ViewBag.ObjectName = objectName;
                ViewBag.Env = env;
            }
            return View("Index");
        }

        // ── SP Execute with params ─────────────────────────────────
        [HttpPost]
        public IActionResult GetSpParams(string spName, string env)
        {
            if (CurrentUser == null) return Json(new { ok = false });
            var parms = _db.GetSpParameters(spName, env);
            return Json(new { ok = true, parameters = parms.Select(p => new { p.name, p.type, p.inOut }) });
        }

        [HttpPost]
        public IActionResult ExecuteSp(string spName, string env,
            [FromBody] List<SpParamInput> parameters)
        {
            if (CurrentUser == null) return Json(new { ok = false, error = "Not logged in" });
            if (!_perms.UserHasViewSpPermission(CurrentUser))
                return Json(new { ok = false, error = "Access denied" });
            var pList = parameters?.Select(p => (p.Name, p.Type, p.Direction, p.Value)).ToList()
                        ?? new List<(string, string, string, string)>();
            var (output, error) = _db.ExecuteSpWithParams(spName, env, pList);
            if (!string.IsNullOrEmpty(error))
                return Json(new { ok = false, error });
            return Json(new { ok = true, output });
        }
    }

    public class SpParamInput
    {
        public string Name { get; set; } = "";
        public string Type { get; set; } = "VARCHAR2";
        public string Direction { get; set; } = "IN";
        public string Value { get; set; } = "";
    }
}

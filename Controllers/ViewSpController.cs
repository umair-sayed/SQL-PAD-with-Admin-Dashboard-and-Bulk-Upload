using Microsoft.AspNetCore.Mvc;
using OracleSqlPortal.Services;

namespace OracleSqlPortal.Controllers
{
    public class ViewSpController : Controller
    {
        private readonly OracleService _oracle;
        private readonly PermissionService _perms;

        public ViewSpController(OracleService oracle, PermissionService perms)
        { _oracle = oracle; _perms = perms; }

        private string? CurrentUser => HttpContext.Session.GetString("username");

        // Guard: user must have VIEW_SP on at least one environment
        private bool CanView()
        {
            // ViewBag.Environments = _oracle.GetAllEnvConfigs().Keys.ToList();
            if (CurrentUser == null) return false;
            return _perms.UserHasViewSpPermission(CurrentUser);
        }

        public IActionResult Index()
        {
            //ViewBag.EnvConfigs = _oracle.GetAllEnvConfigs();
            //if (CurrentUser == null) return RedirectToAction("Login", "Auth");
            //if (!CanView())          return RedirectToAction("Index", "Query");
            //return View();
            //public IActionResult Index()
            //{
            ViewBag.EnvConfigs = _oracle.GetAllEnvConfigs();

            if (CurrentUser == null)
                return RedirectToAction("Login", "Auth");

            if (!CanView())
            {
                ViewBag.Error = "Table/SP Definition access not granted for this User. Kindly request accesss";
                return View(); // stay on same page
            }

            return View();
        }
        //}

        [HttpPost]
        public IActionResult Lookup(string objectName, string env)
        {
            ViewBag.EnvConfigs = _oracle.GetAllEnvConfigs();

            if (CurrentUser == null)
                return RedirectToAction("Login", "Auth");

            if (!CanView())
            {
                ViewBag.Error = "Table/SP Definition access not granted for this User. Kindly request accesss";
                return View("Index");
            }

            if (string.IsNullOrWhiteSpace(objectName))
            {
                ViewBag.Error = "Please enter an object name.";
                return View("Index");
            }

            try
            {
                var result = _oracle.GetObjectDefinition(objectName.Trim().ToUpper(), env);

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
    }
}

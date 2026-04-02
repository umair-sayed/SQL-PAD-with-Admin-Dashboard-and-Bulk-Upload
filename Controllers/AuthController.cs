using Microsoft.AspNetCore.Mvc;
using OracleSqlPortal.Models;
using OracleSqlPortal.Services;

namespace OracleSqlPortal.Controllers
{
    public class AuthController : Controller
    {
        private readonly PortalDbService _db;
        private readonly PortalDataService _portal;
        private readonly OracleService _oracle;

        public AuthController(PortalDbService db, PortalDataService portal, OracleService oracle)
        { _db = db; _portal = portal; _oracle = oracle; }

        private string? CurrentUser => HttpContext.Session.GetString("username");
        private bool CurrentUserIsAdmin() => CurrentUser != null && (_db.GetUser(CurrentUser)?.IsAdmin == true);

        [HttpGet]
        public IActionResult Login(string? returnUrl = null)
        {
            if (CurrentUser != null) return RedirectToAction("Index", "Query");
            ViewBag.ReturnUrl = returnUrl;
            return View(new LoginViewModel());
        }

        [HttpPost]
        public IActionResult Login(LoginViewModel model, string? returnUrl = null)
        {
            string ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

            var existing = _db.GetUser(model.Username);
            if (existing != null && !existing.IsApproved)
            {
                _portal.RecordLogin(model.Username, ip, false);
                model.Error = "Your account is pending admin approval.";
                return View(model);
            }

            var user = _db.FindUser(model.Username, model.Password);
            if (user == null)
            {
                _portal.RecordLogin(model.Username, ip, false);
                model.Error = "Invalid username or password.";
                return View(model);
            }

            _portal.RecordLogin(model.Username, ip, true);
            HttpContext.Session.SetString("username", user.Username);
            HttpContext.Session.SetString("displayname", user.DisplayName);
            HttpContext.Session.SetString("isadmin", user.IsAdmin ? "1" : "0");
            if (user.IsAdmin) HttpContext.Session.SetString("admin_auth", "1");
            HttpContext.Session.SetString("hasviewsp",
                _db.UserHasViewSpPermission(user.Username) ? "1" : "0");
            HttpContext.Session.SetString("ismigration",
                (user.IsAdmin || _db.IsMigrationUser(user.Username)) ? "1" : "0");

            if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
                return Redirect(returnUrl);
            return RedirectToAction("Index", "Query");
        }

        public IActionResult Logout() { HttpContext.Session.Clear(); return RedirectToAction("Login"); }

        [HttpGet] public IActionResult Signup() => View(new SignupViewModel());

        [HttpPost]
        public IActionResult Signup(SignupViewModel model)
        {
            if (model.Password != model.Confirm) { model.Error = "Passwords do not match."; return View(model); }
            if (_db.UsernameExists(model.Username)) { model.Error = "Username already taken."; return View(model); }
            if (!string.IsNullOrWhiteSpace(model.Email) && _db.EmailExists(model.Email))
            { model.Error = "Email already registered."; return View(model); }

            _db.AddUser(new AppUser
            {
                Username = model.Username.Trim(),
                Password = model.Password,
                DisplayName = model.DisplayName.Trim(),
                Email = model.Email?.Trim() ?? "",
                IsApproved = false,
                Permissions = new()
            });
            model.Success = "Account request submitted! Awaiting admin approval.";
            model.Username = model.Password = model.Confirm = model.DisplayName = model.Email = "";
            return View(model);
        }

        [HttpGet] public IActionResult ForgotPassword() => View(new ForgotPasswordViewModel());

        [HttpPost]
        public IActionResult ForgotPassword(ForgotPasswordViewModel model)
        {
            var user = _db.GetAllUsers().FirstOrDefault(u =>
                u.Email.Equals(model.Email, StringComparison.OrdinalIgnoreCase) ||
                u.Username.Equals(model.Email, StringComparison.OrdinalIgnoreCase));
            string token = user != null ? _portal.CreateResetToken(user.Username) : Guid.NewGuid().ToString("N");
            model.Success = user != null ? $"Reset token: {token}" : "If that account exists, a link was sent.";
            return View(model);
        }

        [HttpGet] public IActionResult ResetPassword(string token) => View(new ResetPasswordViewModel { Token = token });

        [HttpPost]
        public IActionResult ResetPassword(ResetPasswordViewModel model)
        {
            if (model.Password != model.Confirm) { model.Error = "Passwords do not match."; return View(model); }
            string? username = _portal.ValidateResetToken(model.Token);
            if (username == null) { model.Error = "Invalid or expired token."; return View(model); }
            _db.ResetPassword(username, model.Password);
            _portal.InvalidateResetToken(username);
            model.Success = "Password reset. You can now log in.";
            return View(model);
        }

        [HttpGet]
        public IActionResult RequestAccess()
        {
            if (CurrentUser == null) return RedirectToAction("Login");
            if (CurrentUserIsAdmin()) return RedirectToAction("Index", "Admin");
            ViewBag.Environments = _oracle.GetAllEnvConfigs().Keys.ToList();
            ViewBag.AccessError = TempData["AccessError"];
            ViewBag.AccessSuccess = TempData["AccessSuccess"];
            return View(new AccessRequest());
        }

        [HttpPost]
        public IActionResult RequestAccess(AccessRequest model)
        {
            if (CurrentUser == null) return RedirectToAction("Login");
            if (CurrentUserIsAdmin()) return RedirectToAction("Index", "Admin");

            var user = _db.GetUser(CurrentUser);
            model.Username = CurrentUser;
            model.DisplayName = user?.DisplayName ?? CurrentUser;
            model.RequestedAt = DateTime.Now;
            model.Status = "Pending";
            model.Id = Guid.NewGuid().ToString("N")[..8];
            model.Env = model.Env?.Trim() ?? "";
            model.Operation = model.Operation?.Trim().ToUpper() ?? "";

            // Guard: already has this permission
            var userPerms = _db.GetUserPermissions(CurrentUser);
            if (userPerms.TryGetValue(model.Env, out var perms) &&
                perms.Contains(model.Operation, StringComparer.OrdinalIgnoreCase))
            {
                TempData["AccessError"] = $"You already have {model.Operation} permission on {model.Env}.";
                return RedirectToAction("RequestAccess");
            }

            // Guard: duplicate pending request
            bool alreadyPending = _db.GetAccessRequests()
                .Any(r => r.Username == CurrentUser
                       && r.Status == "Pending"
                       && r.Env == model.Env
                       && r.Operation == model.Operation);
            if (alreadyPending)
            {
                TempData["AccessError"] = $"You already have a pending request for {model.Operation} on {model.Env}.";
                return RedirectToAction("RequestAccess");
            }

            // Insert into Oracle DB via PortalDataService -> PortalDbService
            try
            {
                _portal.AddAccessRequest(model);
                TempData["AccessSuccess"] = $"Request for {model.Operation} on {model.Env} submitted successfully.";
            }
            catch (Exception ex)
            {
                TempData["AccessError"] = $"Failed to submit request: {ex.Message}";
            }
            return RedirectToAction("RequestAccess");
        }
    }
}
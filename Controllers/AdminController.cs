using Microsoft.AspNetCore.Mvc;
using OracleSqlPortal.Models;
using OracleSqlPortal.Services;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace OracleSqlPortal.Controllers
{
    public class AdminController : Controller
    {
        private readonly OracleService _oracle;
        private readonly PermissionService _perms;
        private readonly PortalDbService _db;
        private readonly PortalDataService _portal;
        private readonly IConfiguration _config;
        private readonly IWebHostEnvironment _env;

        public AdminController(OracleService oracle, PermissionService perms,
            PortalDbService db, PortalDataService portal,
            IConfiguration config, IWebHostEnvironment env)
        { _oracle = oracle; _perms = perms; _db = db; _portal = portal; _config = config; _env = env; }

        private string AdminUser => HttpContext.Session.GetString("username") ?? "admin";

        private bool IsAdmin()
        {
            if (HttpContext.Session.GetString("admin_auth") == "1") return true;
            var u = HttpContext.Session.GetString("username");
            return u != null && (_db.GetUser(u)?.IsAdmin == true);
        }

        private void LogActivity(string action, string target, string details)
            => _db.RecordAdminActivity(AdminUser, action, target, details);

        [HttpGet] public IActionResult Login() { if (IsAdmin()) return RedirectToAction("Index"); return View(); }
        [HttpPost]
        public IActionResult Login(string password)
        {
            if (password == (_config["AdminPassword"] ?? "Admin@1234"))
            { HttpContext.Session.SetString("admin_auth", "1"); return RedirectToAction("Index"); }
            ViewBag.Error = "Invalid admin password."; return View();
        }
        public IActionResult AdminLogout()
        {
            var u = HttpContext.Session.GetString("username");
            HttpContext.Session.Clear();
            if (u != null && (_db.GetUser(u)?.IsAdmin == true)) return RedirectToAction("Login", "Auth");
            return RedirectToAction("Login");
        }

        public IActionResult Index(string tab = "approvals")
        {
            if (!IsAdmin()) return RedirectToAction("Login");
            ViewBag.MigrationFolderConfig = _config["MigrationFolder"] ?? "";
            return View(BuildModel(tab));
        }

        // ── Save single connection ─────────────────────────────────
        [HttpPost]
        public IActionResult SaveSingleConnection(string envKey,
            string label, string protocol, string host, string port,
            string serviceName, string userId, string password,
            string sqlNetOraPath, string walletPath, string trustStore,
            string keyStore, string keyStorePwd, string serverDN)
        {
            if (!IsAdmin()) return RedirectToAction("Login");
            if (string.IsNullOrWhiteSpace(envKey))
            { TempData["Error"] = "Environment key is required."; return RedirectToAction("Index", new { tab = "connections" }); }
            try
            {
                UpdateSettings(json =>
                {
                    var environments = json["Environments"]?.AsObject() ?? new JsonObject();
                    if (json["Environments"] == null) json["Environments"] = environments;
                    environments[envKey] = EnvToJson(new EnvConfig
                    {
                        Label = label,
                        Protocol = protocol,
                        Host = host,
                        Port = port,
                        ServiceName = serviceName,
                        UserId = userId,
                        Password = password,
                        SqlNetOraPath = sqlNetOraPath,
                        WalletPath = walletPath,
                        TrustStore = trustStore,
                        KeyStore = keyStore,
                        KeyStorePwd = keyStorePwd,
                        ServerDN = serverDN
                    });
                });

                // Ensure all admin users get full access on updated connection
                _db.EnsureAdminPermissionsForEnv(envKey);

                LogActivity("Edit Connection", envKey, $"Updated connection '{envKey}' (host: {host}, service: {serviceName})");
                TempData["Success"] = $"'{envKey}' saved.";
            }
            catch (Exception ex) { TempData["Error"] = ex.Message; }
            return RedirectToAction("Index", new { tab = "connections" });
        }

        // ── Add connection ─────────────────────────────────────────
        [HttpPost]
        public IActionResult AddConnection(string envKey, string label, string protocol,
            string host, string port, string serviceName, string userId, string password,
            string sqlNetOraPath, string walletPath, string trustStore,
            string keyStore, string keyStorePwd, string serverDN)
        {
            if (!IsAdmin()) return RedirectToAction("Login");
            if (string.IsNullOrWhiteSpace(envKey))
            { TempData["Error"] = "Environment key is required."; return RedirectToAction("Index", new { tab = "connections" }); }
            try
            {
                string key = envKey.ToUpper().Trim();
                UpdateSettings(json =>
                {
                    var environments = json["Environments"]?.AsObject() ?? new JsonObject();
                    if (json["Environments"] == null) json["Environments"] = environments;
                    environments[key] = EnvToJson(new EnvConfig
                    {
                        Label = label,
                        Protocol = protocol,
                        Host = host,
                        Port = port,
                        ServiceName = serviceName,
                        UserId = userId,
                        Password = password,
                        SqlNetOraPath = sqlNetOraPath,
                        WalletPath = walletPath,
                        TrustStore = trustStore,
                        KeyStore = keyStore,
                        KeyStorePwd = keyStorePwd,
                        ServerDN = serverDN
                    });
                });

                // Auto-grant all admin users full access on the new connection
                _db.EnsureAdminPermissionsForEnv(key);

                LogActivity("Add Connection", key,
                    $"Added new connection '{key}' (host: {host}, service: {serviceName})");
                TempData["Success"] = $"Environment '{key}' added.";
            }
            catch (Exception ex) { TempData["Error"] = ex.Message; }
            return RedirectToAction("Index", new { tab = "connections" });
        }

        // ── Delete connection ──────────────────────────────────────
        [HttpPost]
        public IActionResult DeleteConnection(string envKey)
        {
            if (!IsAdmin()) return RedirectToAction("Login");
            try
            {
                UpdateSettings(json => { json["Environments"]?.AsObject().Remove(envKey); });
                LogActivity("Delete Connection", envKey, $"Removed connection '{envKey}'");
                TempData["Success"] = $"Environment '{envKey}' removed.";
            }
            catch (Exception ex) { TempData["Error"] = ex.Message; }
            return RedirectToAction("Index", new { tab = "connections" });
        }

        // ── Test Oracle connection ─────────────────────────────────
        [HttpPost]
        public IActionResult TestConnection(string env)
        {
            if (!IsAdmin()) return Json(new { ok = false, error = "Unauthorized" });
            var (ok, error, ms) = _oracle.TestConnection(env);
            return Json(new { ok, error, ms });
        }

        // ── Test Portal DB ─────────────────────────────────────────
        [HttpPost]
        public IActionResult TestPortalDb()
        {
            if (!IsAdmin()) return Json(new { ok = false, error = "Unauthorized" });
            var sw = System.Diagnostics.Stopwatch.StartNew();
            bool ok = _db.TestPortalDb(out string error);
            sw.Stop();
            return Json(new { ok, error, ms = sw.ElapsedMilliseconds });
        }

        // ── Save Portal DB config ──────────────────────────────────
        [HttpPost]
        public IActionResult SavePortalDb(AdminConfigViewModel model)
        {
            if (!IsAdmin()) return RedirectToAction("Login");
            try
            {
                UpdateSettings(json =>
                {
                    if (!string.IsNullOrWhiteSpace(model.PortalDbConnStr))
                    {
                        json["PortalDb"] = new JsonObject
                        {
                            ["ConnectionString"] = model.PortalDbConnStr,
                            ["Comment"] = "Raw connection string takes priority over individual fields."
                        };
                    }
                    else
                    {
                        json["PortalDb"] = new JsonObject
                        {
                            ["ConnectionString"] = "",
                            ["Host"] = model.PdbHost,
                            ["Port"] = model.PdbPort,
                            ["ServiceName"] = model.PdbService,
                            ["UserId"] = model.PdbUser,
                            ["Password"] = model.PdbPassword,
                            ["Protocol"] = model.PdbProtocol,
                            ["SqlNetOraPath"] = model.PdbSqlNetOraPath,
                            ["WalletPath"] = model.PdbWalletPath,
                            ["TrustStore"] = model.PdbTrustStore,
                            ["KeyStore"] = model.PdbKeyStore,
                            ["KeyStorePwd"] = model.PdbKeyStorePwd,
                            ["ServerDN"] = model.PdbServerDN
                        };
                    }
                });
                LogActivity("Update Portal DB", "PortalDb",
                    $"Updated Portal DB config (host: {model.PdbHost}, service: {model.PdbService})");
                TempData["Success"] = "Portal DB config saved. Restart the application to connect to the new database.";
            }
            catch (Exception ex) { TempData["Error"] = ex.Message; }
            return RedirectToAction("Index", new { tab = "portaldb" });
        }

        // ── Add User ───────────────────────────────────────────────
        [HttpPost]
        public IActionResult AddUser(string username, string password, string displayName,
            string email, bool isAdmin = false)
        {
            if (!IsAdmin()) return RedirectToAction("Login");
            if (_db.UsernameExists(username))
            { TempData["Error"] = $"'{username}' already exists."; return RedirectToAction("Index", new { tab = "users" }); }

            _db.AddUser(new AppUser
            {
                Username = username,
                Password = password,
                DisplayName = displayName,
                Email = email ?? "",
                IsApproved = true,
                IsAdmin = isAdmin
            });
            LogActivity("Add User", username, $"Created user '{username}' ({displayName}), Admin={isAdmin}");
            TempData["Success"] = $"User '{username}' added.";
            return RedirectToAction("Index", new { tab = "users" });
        }

        [HttpPost]
        public IActionResult DeleteUser(string username)
        {
            if (!IsAdmin()) return RedirectToAction("Login");
            _db.DeleteUser(username);
            LogActivity("Delete User", username, $"Deleted user '{username}'");
            TempData["Success"] = $"User '{username}' deleted.";
            return RedirectToAction("Index", new { tab = "users" });
        }

        // ── Set Migration Access (replaces both old buttons) ───────
        /// <summary>
        /// Single toggle: grant or revoke migration access for a user.
        /// If the user has the IsMigration flag, they can perform migrations.
        /// No separate "Grant Migration" + "Migration Privilege" buttons needed.
        /// </summary>
        [HttpPost]
        public IActionResult SetMigrationAccess(string username, bool hasMigration)
        {
            if (!IsAdmin()) return RedirectToAction("Login");
            _db.SetMigrationRole(username, hasMigration);

            // Also sync the MIGRATION permission in USER_PERMISSIONS for all envs they have access to
            if (hasMigration)
            {
                var perms = _db.GetUserPermissions(username);
                foreach (var env in perms.Keys)
                    _db.GrantPermission(username, env, "MIGRATION");
            }

            LogActivity("Set Migration Access", username,
                $"{(hasMigration ? "Granted" : "Revoked")} Migration access for '{username}'");
            TempData["Success"] = $"Migration access {(hasMigration ? "granted to" : "revoked from")} '{username}'.";
            return RedirectToAction("Index", new { tab = "users" });
        }

        [HttpPost]
        public IActionResult SaveUsers(AdminConfigViewModel model)
        {
            if (!IsAdmin()) return RedirectToAction("Login");
            foreach (var u in model.Users)
            {
                var ex = _db.GetUser(u.Username); if (ex == null) continue;
                ex.Password = u.Password;
                ex.DisplayName = u.DisplayName;
                ex.Email = u.Email ?? "";
                _db.UpdateUser(ex);
                _db.SaveUserPermissions(u.Username, u.Permissions);
            }
            LogActivity("Bulk Save Users", "Multiple", $"Saved permissions for {model.Users.Count} users");
            TempData["Success"] = "Permissions saved.";
            return RedirectToAction("Index", new { tab = "users" });
        }

        // ── AJAX: save a single user's permissions ─────────────────
        [HttpPost]
        public IActionResult SaveUserAjax(string username, string? password,
            string? displayName, string? email)
        {
            if (!IsAdmin()) return Json(new { ok = false, error = "Unauthorized" });
            var ex = _db.GetUser(username);
            if (ex == null) return Json(new { ok = false, error = "User not found" });

            if (!string.IsNullOrWhiteSpace(password)) ex.Password = password;
            if (!string.IsNullOrWhiteSpace(displayName)) ex.DisplayName = displayName;
            if (email != null) ex.Email = email;
            _db.UpdateUser(ex);

            var map = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var key in Request.Form.Keys)
            {
                if (!key.StartsWith("perms[", StringComparison.OrdinalIgnoreCase)) continue;
                string env = key[6..^1];
                if (!map.ContainsKey(env)) map[env] = new();
                foreach (var v in Request.Form[key])
                    if (!string.IsNullOrWhiteSpace(v)) map[env].Add(v!);
            }
            _db.SaveUserPermissions(username, map);

            var permSummary = string.Join(", ", map.Select(kv => $"{kv.Key}:[{string.Join(",", kv.Value)}]"));
            LogActivity("Edit User Permissions", username,
                $"Updated permissions for '{username}': {permSummary}");

            return Json(new { ok = true });
        }

        [HttpPost]
        public IActionResult ApproveUser(string username)
        {
            if (!IsAdmin()) return RedirectToAction("Login");
            _db.SetApproved(username, true);
            LogActivity("Approve User", username, $"Approved signup request for '{username}'");
            TempData["Success"] = $"'{username}' approved.";
            return RedirectToAction("Index", new { tab = "approvals" });
        }

        [HttpPost]
        public IActionResult RejectUserRequest(string username)
        {
            if (!IsAdmin()) return RedirectToAction("Login");
            _db.DeleteUser(username);
            LogActivity("Reject User", username, $"Rejected and deleted signup request for '{username}'");
            TempData["Success"] = $"'{username}' rejected.";
            return RedirectToAction("Index", new { tab = "approvals" });
        }

        [HttpPost]
        public IActionResult ApproveAccess(string id, string? note)
        {
            if (!IsAdmin()) return RedirectToAction("Login");
            var req = _db.GetAccessRequests().FirstOrDefault(r => r.Id == id);
            if (req == null) { TempData["Error"] = "Not found."; return RedirectToAction("Index", new { tab = "access" }); }
            _db.UpdateAccessRequest(id, "Approved", note);
            _db.GrantPermission(req.Username, req.Env, req.Operation);

            // If the approved operation is MIGRATION, also set the migration role flag
            // so the user gets access to the Migration module (IsMigration check in session/nav)
            if (req.Operation.Equals("MIGRATION", StringComparison.OrdinalIgnoreCase))
                _db.SetMigrationRole(req.Username, true);

            LogActivity("Approve Access", req.Username,
                $"Granted {req.Operation} on {req.Env} to '{req.DisplayName}' (request #{id})");
            TempData["Success"] = $"✅ Granted {req.Operation} on {req.Env} to {req.DisplayName}.";
            return RedirectToAction("Index", new { tab = "access" });
        }

        [HttpPost]
        public IActionResult RejectAccess(string id, string? note)
        {
            if (!IsAdmin()) return RedirectToAction("Login");
            var req = _db.GetAccessRequests().FirstOrDefault(r => r.Id == id);
            _db.UpdateAccessRequest(id, "Rejected", note ?? "Rejected.");
            if (req != null)
                LogActivity("Reject Access", req.Username,
                    $"Rejected {req.Operation} on {req.Env} for '{req.DisplayName}' (request #{id})");
            TempData["Success"] = "Rejected.";
            return RedirectToAction("Index", new { tab = "access" });
        }

        // ── Helpers ───────────────────────────────────────────────
        private AdminConfigViewModel BuildModel(string tab)
        {
            var sec = _config.GetSection("PortalDb");
            var users = _db.GetApprovedUsers();
            return new AdminConfigViewModel
            {
                Environments = _oracle.GetAllEnvConfigs(),
                Users = users,
                PendingUsers = _db.GetPendingUsers(),
                AccessRequests = _db.GetAccessRequests(),
                LoginHistory = _db.GetLoginHistory(500),
                QueryHistory = _db.GetQueryHistory(500),
                MigrationHistory = _db.GetMigrationHistory(300),
                AdminActivity = _db.GetAdminActivity(500),
                ErrorLog=_db.GetErrorLogs(500),
                MigrationFolder = _config["MigrationFolder"] ?? "",
                PortalDbConnStr = sec["ConnectionString"] ?? "",
                PortalDbDesc = _db.GetDbDescription(),
                PdbHost = sec["Host"] ?? "",
                PdbPort = sec["Port"] ?? "1521",
                PdbService = sec["ServiceName"] ?? "",
                PdbUser = sec["UserId"] ?? "",
                PdbPassword = sec["Password"] ?? "",
                PdbProtocol = sec["Protocol"] ?? "TCP",
                PdbSqlNetOraPath = sec["SqlNetOraPath"] ?? "",
                PdbWalletPath = sec["WalletPath"] ?? "",
                PdbTrustStore = sec["TrustStore"] ?? "",
                PdbKeyStore = sec["KeyStore"] ?? "",
                PdbKeyStorePwd = sec["KeyStorePwd"] ?? "",
                PdbServerDN = sec["ServerDN"] ?? "",
                SuccessMessage = TempData["Success"]?.ToString(),
                ErrorMessage = TempData["Error"]?.ToString(),
                ActiveTab = tab
            };
        }

        private static JsonObject EnvToJson(EnvConfig c) => new()
        {
            ["Label"] = c.Label,
            ["Protocol"] = c.Protocol,
            ["Host"] = c.Host,
            ["Port"] = c.Port,
            ["ServiceName"] = c.ServiceName,
            ["UserId"] = c.UserId,
            ["Password"] = c.Password,
            ["SqlNetOraPath"] = c.SqlNetOraPath,
            ["WalletPath"] = c.WalletPath,
            ["TrustStore"] = c.TrustStore,
            ["KeyStore"] = c.KeyStore,
            ["KeyStorePwd"] = c.KeyStorePwd,
            ["ServerDN"] = c.ServerDN
        };
        private void LogError(Exception ex, string method, string? extra = null)
        {
            _db.LogErrorToDb(new ErrorLog
            {
                Username = AdminUser,
                Method = method,
                Message = ex.Message,
                StackTrace = ex.ToString(),
                Extra = extra
            });
        }
        private void UpdateSettings(Action<JsonObject> mutate)
        {
            string path = Path.Combine(_env.ContentRootPath, "appsettings.json");
            var json = JsonNode.Parse(System.IO.File.ReadAllText(path))!.AsObject();
            mutate(json);
            System.IO.File.WriteAllText(path,
                json.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            (_config as IConfigurationRoot)?.Reload();
        }
    }
}
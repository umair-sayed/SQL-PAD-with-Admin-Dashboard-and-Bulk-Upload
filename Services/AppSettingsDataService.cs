using OracleSqlPortal.Models;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace OracleSqlPortal.Services
{
    /// <summary>
    /// Stores users, permissions, connections, access requests, and reset tokens
    /// entirely in appsettings.json. No database required for these entities.
    /// Only query history, login history, migration logs, and admin activity
    /// remain in the Oracle Portal DB.
    /// </summary>
    public class AppSettingsDataService
    {
        private readonly string _settingsPath;
        private readonly IConfiguration _config;
        private readonly IConfigurationRoot? _configRoot;
        private static readonly object _fileLock = new();

        public AppSettingsDataService(IConfiguration config, IWebHostEnvironment env)
        {
            _config     = config;
            _configRoot = config as IConfigurationRoot;
            _settingsPath = Path.Combine(env.ContentRootPath, "appsettings.json");
        }

        // ══════════════════════════════════════════════════════════
        // FILE I/O
        // ══════════════════════════════════════════════════════════
        private JsonObject ReadJson()
        {
            lock (_fileLock)
            {
                var text = File.ReadAllText(_settingsPath);
                return JsonNode.Parse(text)!.AsObject();
            }
        }

        private void WriteJson(Action<JsonObject> mutate)
        {
            lock (_fileLock)
            {
                var json = ReadJson();
                mutate(json);
                File.WriteAllText(_settingsPath,
                    json.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
                _configRoot?.Reload();
            }
        }

        // ══════════════════════════════════════════════════════════
        // USERS
        // ══════════════════════════════════════════════════════════
        public List<AppUser> GetAllUsers()
        {
            var json = ReadJson();
            var usersNode = json["Users"]?.AsArray();
            if (usersNode == null) return new();
            var list = new List<AppUser>();
            foreach (var node in usersNode)
            {
                if (node == null) continue;
                var u = DeserializeUser(node.AsObject());
                u.Permissions = GetUserPermissions(u.Username);
                list.Add(u);
            }
            return list.OrderByDescending(u => u.IsAdmin).ThenBy(u => u.DisplayName).ToList();
        }

        public AppUser? GetUser(string username)
            => GetAllUsers().FirstOrDefault(u =>
                u.Username.Equals(username, StringComparison.OrdinalIgnoreCase));

        public AppUser? FindUser(string username, string password)
        {
            var u = GetUser(username);
            if (u == null || u.Password != password || !u.IsApproved) return null;
            return u;
        }

        public bool UsernameExists(string username)
            => GetAllUsers().Any(u => u.Username.Equals(username, StringComparison.OrdinalIgnoreCase));

        public bool EmailExists(string email)
        {
            if (string.IsNullOrWhiteSpace(email)) return false;
            return GetAllUsers().Any(u => u.Email.Equals(email, StringComparison.OrdinalIgnoreCase));
        }

        public List<AppUser> GetApprovedUsers() => GetAllUsers().Where(u =>  u.IsApproved).ToList();
        public List<AppUser> GetPendingUsers()  => GetAllUsers().Where(u => !u.IsApproved).ToList();

        public void AddUser(AppUser user)
        {
            WriteJson(json =>
            {
                var arr = EnsureArray(json, "Users");
                arr.Add(SerializeUser(user));
            });
            SaveUserPermissions(user.Username, user.Permissions);
        }

        public void UpdateUser(AppUser user)
        {
            WriteJson(json =>
            {
                var arr = EnsureArray(json, "Users");
                for (int i = 0; i < arr.Count; i++)
                {
                    var node = arr[i]?.AsObject();
                    if (node == null) continue;
                    if ((node["Username"]?.GetValue<string>() ?? "")
                            .Equals(user.Username, StringComparison.OrdinalIgnoreCase))
                    {
                        arr[i] = SerializeUser(user);
                        return;
                    }
                }
            });
        }

        public void DeleteUser(string username)
        {
            WriteJson(json =>
            {
                var arr = EnsureArray(json, "Users");
                for (int i = arr.Count - 1; i >= 0; i--)
                {
                    var uname = arr[i]?.AsObject()?["Username"]?.GetValue<string>() ?? "";
                    if (uname.Equals(username, StringComparison.OrdinalIgnoreCase))
                        arr.RemoveAt(i);
                }
                // Remove permissions too
                var perms = EnsureObject(json, "UserPermissions");
                perms.Remove(username.ToUpperInvariant());
            });
        }

        public void SetApproved(string username, bool approved)
        {
            WriteJson(json =>
            {
                var arr = EnsureArray(json, "Users");
                foreach (var node in arr)
                {
                    var obj = node?.AsObject();
                    if (obj == null) continue;
                    if ((obj["Username"]?.GetValue<string>() ?? "")
                            .Equals(username, StringComparison.OrdinalIgnoreCase))
                    {
                        obj["IsApproved"] = approved;
                        return;
                    }
                }
            });
        }

        public void ResetPassword(string username, string newPassword)
        {
            WriteJson(json =>
            {
                var arr = EnsureArray(json, "Users");
                foreach (var node in arr)
                {
                    var obj = node?.AsObject();
                    if (obj == null) continue;
                    if ((obj["Username"]?.GetValue<string>() ?? "")
                            .Equals(username, StringComparison.OrdinalIgnoreCase))
                    {
                        obj["Password"] = newPassword;
                        return;
                    }
                }
            });
        }

        public void SetMigrationRole(string username, bool value)
        {
            WriteJson(json =>
            {
                var arr = EnsureArray(json, "Users");
                foreach (var node in arr)
                {
                    var obj = node?.AsObject();
                    if (obj == null) continue;
                    if ((obj["Username"]?.GetValue<string>() ?? "")
                            .Equals(username, StringComparison.OrdinalIgnoreCase))
                    {
                        obj["IsMigration"] = value;
                        return;
                    }
                }
            });
        }

        public bool IsMigrationUser(string username)
            => GetUser(username)?.IsMigration == true;

        // ══════════════════════════════════════════════════════════
        // PERMISSIONS  (stored as UserPermissions: { "USERNAME": { "ENV": ["SELECT",...] } })
        // ══════════════════════════════════════════════════════════
        public Dictionary<string, List<string>> GetUserPermissions(string username)
        {
            var json  = ReadJson();
            var perms = json["UserPermissions"]?.AsObject();
            if (perms == null) return new();
            var userKey = username.ToUpperInvariant();
            var userPerms = perms[userKey]?.AsObject();
            if (userPerms == null) return new();
            var map = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var (env, ops) in userPerms)
            {
                var opList = ops?.AsArray()?.Select(o => o?.GetValue<string>() ?? "")
                              .Where(o => !string.IsNullOrEmpty(o)).ToList() ?? new();
                map[env] = opList;
            }
            return map;
        }

        public List<string> GetUserEnvPermissions(string username, string env)
        {
            var perms = GetUserPermissions(username);
            return perms.TryGetValue(env, out var ops) ? ops : new();
        }

        public void SaveUserPermissions(string username, Dictionary<string, List<string>> permissions)
        {
            WriteJson(json =>
            {
                var permsRoot = EnsureObject(json, "UserPermissions");
                var userKey   = username.ToUpperInvariant();
                var userNode  = new JsonObject();
                foreach (var (env, ops) in permissions)
                {
                    var arr = new JsonArray();
                    foreach (var op in ops) arr.Add(JsonValue.Create(op));
                    userNode[env] = arr;
                }
                permsRoot[userKey] = userNode;
            });
        }

        public void GrantPermission(string username, string env, string operation)
        {
            var perms = GetUserPermissions(username);
            if (!perms.ContainsKey(env)) perms[env] = new();
            if (!perms[env].Contains(operation, StringComparer.OrdinalIgnoreCase))
                perms[env].Add(operation);
            SaveUserPermissions(username, perms);
        }

        public bool UserHasPermission(string username, string env, string op)
        {
            var ops = GetUserEnvPermissions(username, env);
            return ops.Any(o => o.Equals(op, StringComparison.OrdinalIgnoreCase));
        }

        public bool UserHasViewSpPermission(string username)
        {
            var perms = GetUserPermissions(username);
            return perms.Values.Any(ops =>
                ops.Any(o => o.Equals("VIEW_SP", StringComparison.OrdinalIgnoreCase)));
        }

        // ══════════════════════════════════════════════════════════
        // ACCESS REQUESTS
        // ══════════════════════════════════════════════════════════
        public List<AccessRequest> GetAccessRequests()
        {
            var json = ReadJson();
            var arr  = json["AccessRequests"]?.AsArray();
            if (arr == null) return new();
            var list = arr.Select(n => DeserializeAccessRequest(n!.AsObject()))
                         .OrderBy(r => r.Status == "Pending" ? 0 : 1)
                         .ThenByDescending(r => r.RequestedAt)
                         .ToList();
            return list;
        }

        public void AddAccessRequest(AccessRequest req)
        {
            WriteJson(json =>
            {
                var arr = EnsureArray(json, "AccessRequests");
                arr.Add(SerializeAccessRequest(req));
            });
        }

        public bool UpdateAccessRequest(string id, string status, string? note)
        {
            bool found = false;
            WriteJson(json =>
            {
                var arr = json["AccessRequests"]?.AsArray();
                if (arr == null) return;
                foreach (var node in arr)
                {
                    var obj = node?.AsObject();
                    if (obj == null) continue;
                    if ((obj["Id"]?.GetValue<string>() ?? "") == id)
                    {
                        obj["Status"]    = status;
                        obj["AdminNote"] = note ?? "";
                        found = true;
                        return;
                    }
                }
            });
            return found;
        }

        // ══════════════════════════════════════════════════════════
        // RESET TOKENS
        // ══════════════════════════════════════════════════════════
        public string CreateResetToken(string username)
        {
            string token = Guid.NewGuid().ToString("N");
            WriteJson(json =>
            {
                var tokens = EnsureObject(json, "ResetTokens");
                tokens[username.ToLowerInvariant()] = JsonValue.Create(
                    $"{token}|{DateTime.UtcNow.AddHours(1):O}");
            });
            return token;
        }

        public string? ValidateResetToken(string token)
        {
            var json   = ReadJson();
            var tokens = json["ResetTokens"]?.AsObject();
            if (tokens == null) return null;
            foreach (var (username, node) in tokens)
            {
                var val = node?.GetValue<string>() ?? "";
                var parts = val.Split('|');
                if (parts.Length == 2 && parts[0] == token
                    && DateTime.Parse(parts[1]) > DateTime.UtcNow)
                    return username;
            }
            return null;
        }

        public void InvalidateResetToken(string username)
        {
            WriteJson(json =>
            {
                json["ResetTokens"]?.AsObject()?.Remove(username.ToLowerInvariant());
            });
        }

        // ══════════════════════════════════════════════════════════
        // SERIALIZATION HELPERS
        // ══════════════════════════════════════════════════════════
        private static JsonObject SerializeUser(AppUser u) => new()
        {
            ["Username"]    = u.Username,
            ["Password"]    = u.Password,
            ["DisplayName"] = u.DisplayName,
            ["Email"]       = u.Email,
            ["IsApproved"]  = u.IsApproved,
            ["IsAdmin"]     = u.IsAdmin,
            ["IsMigration"] = u.IsMigration
        };

        private static AppUser DeserializeUser(JsonObject o) => new()
        {
            Username    = o["Username"]?.GetValue<string>()    ?? "",
            Password    = o["Password"]?.GetValue<string>()    ?? "",
            DisplayName = o["DisplayName"]?.GetValue<string>() ?? "",
            Email       = o["Email"]?.GetValue<string>()       ?? "",
            IsApproved  = o["IsApproved"]?.GetValue<bool>()    ?? false,
            IsAdmin     = o["IsAdmin"]?.GetValue<bool>()       ?? false,
            IsMigration = o["IsMigration"]?.GetValue<bool>()   ?? false
        };

        private static JsonObject SerializeAccessRequest(AccessRequest r) => new()
        {
            ["Id"]          = r.Id,
            ["Username"]    = r.Username,
            ["DisplayName"] = r.DisplayName,
            ["Env"]         = r.Env,
            ["Operation"]   = r.Operation,
            ["Reason"]      = r.Reason,
            ["Status"]      = r.Status,
            ["AdminNote"]   = r.AdminNote ?? "",
            ["RequestedAt"] = r.RequestedAt.ToString("O")
        };

        private static AccessRequest DeserializeAccessRequest(JsonObject o) => new()
        {
            Id          = o["Id"]?.GetValue<string>()          ?? "",
            Username    = o["Username"]?.GetValue<string>()    ?? "",
            DisplayName = o["DisplayName"]?.GetValue<string>() ?? "",
            Env         = o["Env"]?.GetValue<string>()         ?? "",
            Operation   = o["Operation"]?.GetValue<string>()   ?? "",
            Reason      = o["Reason"]?.GetValue<string>()      ?? "",
            Status      = o["Status"]?.GetValue<string>()      ?? "Pending",
            AdminNote   = o["AdminNote"]?.GetValue<string>()   ?? "",
            RequestedAt = DateTime.TryParse(o["RequestedAt"]?.GetValue<string>(), out var dt) ? dt : DateTime.Now
        };

        private static JsonObject EnsureObject(JsonObject json, string key)
        {
            if (json[key] == null) json[key] = new JsonObject();
            return json[key]!.AsObject();
        }

        private static JsonArray EnsureArray(JsonObject json, string key)
        {
            if (json[key] == null) json[key] = new JsonArray();
            return json[key]!.AsArray();
        }
    }
}

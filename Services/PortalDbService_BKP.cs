using Microsoft.AspNetCore.Http;
using Oracle.ManagedDataAccess.Client;
using OracleSqlPortal.Models;

namespace OracleSqlPortal.Services
{
    /// <summary>
    /// Oracle-backed store for ALL portal data:
    ///   - PORTAL_USERS           (users, moved from appsettings.json)
    ///   - USER_PERMISSIONS       (per-user per-env grants; admin gets all automatically)
    ///   - ACCESS_REQUESTS        (access request workflow)
    ///   - RESET_TOKENS           (password reset)
    ///   - QUERY_HISTORY          (audit)
    ///   - LOGIN_HISTORY          (audit)
    ///   - MIGRATION_HISTORY      (audit)
    ///   - ADMIN_ACTIVITY         (audit)
    ///
    /// appsettings.json now only stores: PortalDb connection, Environments (connections).
    /// </summary>
    public class PortalDbService
    {
        private readonly string _connStr;
        private readonly List<string> _envKeys;
        private readonly IHttpContextAccessor _httpContext;
        private readonly ILogger<PortalDbService> _logger;
        private readonly IConfiguration _config;
        public PortalDbService(IConfiguration config, ILogger<PortalDbService> logger, IHttpContextAccessor httpContext)
        {
            _logger = logger;
            _httpContext = httpContext;
            _config = config;
            _connStr = BuildConnectionString(config);
            _envKeys = config.GetSection("Environments")
                    .GetChildren()
                    .Select(e => e.Key)
                    .ToList();

            InitSchema();
        }

        public List<ErrorLog> GetErrorLogs(int count = 200)
        {
            try
            {
                using var conn = Open();
                using var cmd = new OracleCommand(
                    @"SELECT USERNAME, METHOD, MESSAGE, STACK_TRACE, EXTRA, CREATED_AT
              FROM (SELECT * FROM ERROR_LOG ORDER BY ID DESC)
              WHERE ROWNUM <= :n", conn);

                cmd.Parameters.Add("n", OracleDbType.Int32).Value = count;

                var list = new List<ErrorLog>();
                using var r = cmd.ExecuteReader();

                while (r.Read())
                {
                    list.Add(new ErrorLog
                    {
                        Username = r.IsDBNull(0) ? "" : r.GetString(0),
                        Method = r.GetString(1),
                        Message = r.GetString(2),
                        StackTrace = r.GetString(3),
                        Extra = r.IsDBNull(4) ? "" : r.GetString(4),
                        CreatedAt = r.GetDateTime(5)
                    });
                }

                return list;
            }
            catch (Exception ex)
            {
                LogError(ex, nameof(GetErrorLogs));
                return new List<ErrorLog>(); // ✅ REQUIRED
            }
        }
        // ── Connection string ──────────────────────────────────────
        private static string BuildConnectionString(IConfiguration config)
        {
            var sec = config.GetSection("PortalDb");
            string? raw = sec["ConnectionString"];
            if (!string.IsNullOrWhiteSpace(raw)) return raw;

            string host = sec["Host"] ?? "localhost";
            string port = sec["Port"] ?? "1521";
            string service = sec["ServiceName"] ?? "ORCL";
            string user = sec["UserId"] ?? "";
            string pass = sec["Password"] ?? "";
            string proto = sec["Protocol"]?.ToUpperInvariant() == "TCPS" ? "TCPS" : "TCP";
            int poolMin = int.TryParse(sec["PoolMin"], out var pm) ? pm : 1;
            int poolMax = int.TryParse(sec["PoolMax"], out var px) ? px : 10;

            string ds = $"(DESCRIPTION=(ADDRESS=(PROTOCOL={proto})(HOST={host})(PORT={port}))" +
                        $"(CONNECT_DATA=(SERVICE_NAME={service})))";
            string cs = $"User Id={user};Password={pass};Data Source={ds};" +
                        $"Pooling=true;Min Pool Size={poolMin};Max Pool Size={poolMax};";

            if (proto == "TCPS")
            {
                string? sqlnetDir = sec["SqlNetOraPath"];
                string? wallet = sec["WalletPath"];
                string? ca = sec["TrustStore"];
                string? cert = sec["KeyStore"];
                string? key = sec["KeyStorePwd"];
                string? dn = sec["ServerDN"];
                if (!string.IsNullOrEmpty(sqlnetDir)) cs += $"TNS_ADMIN={sqlnetDir};";
                if (!string.IsNullOrEmpty(wallet)) cs += $"Wallet Location=\"(SOURCE=(METHOD=file)(METHOD_DATA=(DIRECTORY={wallet})))\";";
                if (string.IsNullOrEmpty(sqlnetDir) && !string.IsNullOrEmpty(wallet)) cs += $"TNS_ADMIN={wallet};";
                if (!string.IsNullOrEmpty(ca)) cs += $"SSL CA Cert={ca};";
                if (!string.IsNullOrEmpty(cert)) cs += $"SSL Certificate={cert};";
                if (!string.IsNullOrEmpty(key)) cs += $"SSL Key={key};";
                if (!string.IsNullOrEmpty(dn)) cs += $"SSL Server Cert DN=\"{dn}\";";
            }
            return cs;
        }

        private OracleConnection Open()
        {
            var conn = new OracleConnection(_connStr);
            conn.Open();
            return conn;
        }

        // ── Schema init (idempotent) ───────────────────────────────
        private void InitSchema()
        {
            try
            {
                using var conn = Open();
                var ddl = new[]
                {
                    // PORTAL_USERS
                    @"DECLARE c NUMBER;
                      BEGIN
                        SELECT COUNT(*) INTO c FROM user_tables WHERE table_name='PORTAL_USERS';
                        IF c=0 THEN
                          EXECUTE IMMEDIATE '
                            CREATE TABLE PORTAL_USERS (
                              USERNAME      VARCHAR2(100)  NOT NULL,
                              PASSWORD      VARCHAR2(200)  NOT NULL,
                              DISPLAY_NAME  VARCHAR2(200)  NOT NULL,
                              EMAIL         VARCHAR2(300)  DEFAULT '''' NOT NULL,
                              IS_APPROVED   NUMBER(1)      DEFAULT 0 NOT NULL,
                              IS_ADMIN      NUMBER(1)      DEFAULT 0 NOT NULL,
                              IS_MIGRATION  NUMBER(1)      DEFAULT 0 NOT NULL,
                              CREATED_AT    TIMESTAMP      DEFAULT SYSTIMESTAMP NOT NULL,
                              CONSTRAINT PK_PORTAL_USERS PRIMARY KEY (USERNAME)
                            )';
                          EXECUTE IMMEDIATE 'CREATE INDEX IDX_PU_EMAIL ON PORTAL_USERS(EMAIL)';
                        END IF;
                      END;",

                    // USER_PERMISSIONS
                    @"DECLARE c NUMBER;
                      BEGIN
                        SELECT COUNT(*) INTO c FROM user_tables WHERE table_name='USER_PERMISSIONS';
                        IF c=0 THEN
                          EXECUTE IMMEDIATE '
                            CREATE TABLE USER_PERMISSIONS (
                              ID         NUMBER GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
                              USERNAME   VARCHAR2(100) NOT NULL,
                              ENV_KEY    VARCHAR2(100) NOT NULL,
                              OPERATION  VARCHAR2(50)  NOT NULL,
                              GRANTED_AT TIMESTAMP     DEFAULT SYSTIMESTAMP NOT NULL,
                              CONSTRAINT FK_UP_USER FOREIGN KEY (USERNAME)
                                REFERENCES PORTAL_USERS(USERNAME) ON DELETE CASCADE,
                              CONSTRAINT UQ_UP UNIQUE (USERNAME, ENV_KEY, OPERATION)
                            )';
                          EXECUTE IMMEDIATE 'CREATE INDEX IDX_UP_USER ON USER_PERMISSIONS(USERNAME)';
                          EXECUTE IMMEDIATE 'CREATE INDEX IDX_UP_ENV  ON USER_PERMISSIONS(ENV_KEY)';
                        END IF;
                      END;",

                    // ACCESS_REQUESTS
                    @"DECLARE c NUMBER;
                      BEGIN
                        SELECT COUNT(*) INTO c FROM user_tables WHERE table_name='ACCESS_REQUESTS';
                        IF c=0 THEN
                          EXECUTE IMMEDIATE '
                            CREATE TABLE ACCESS_REQUESTS (
                              ID           VARCHAR2(20)   PRIMARY KEY,
                              USERNAME     VARCHAR2(100)  NOT NULL,
                              DISPLAY_NAME VARCHAR2(200)  NOT NULL,
                              ENV_KEY      VARCHAR2(100)  DEFAULT '''' NOT NULL,
                              OPERATION    VARCHAR2(50)   NOT NULL,
                              REASON       VARCHAR2(1000) DEFAULT '''' NOT NULL,
                              STATUS       VARCHAR2(20)   DEFAULT ''Pending'' NOT NULL,
                              ADMIN_NOTE   VARCHAR2(1000) DEFAULT '''' NOT NULL,
                              REQUESTED_AT TIMESTAMP      DEFAULT SYSTIMESTAMP NOT NULL
                            )';
                          EXECUTE IMMEDIATE 'CREATE INDEX IDX_AR_USER   ON ACCESS_REQUESTS(USERNAME)';
                          EXECUTE IMMEDIATE 'CREATE INDEX IDX_AR_STATUS ON ACCESS_REQUESTS(STATUS)';
                          EXECUTE IMMEDIATE 'CREATE INDEX IDX_AR_AT     ON ACCESS_REQUESTS(REQUESTED_AT DESC)';
                        END IF;
                      END;",

                    // RESET_TOKENS
                    @"DECLARE c NUMBER;
                      BEGIN
                        SELECT COUNT(*) INTO c FROM user_tables WHERE table_name='RESET_TOKENS';
                        IF c=0 THEN
                          EXECUTE IMMEDIATE '
                            CREATE TABLE RESET_TOKENS (
                              USERNAME   VARCHAR2(100) PRIMARY KEY,
                              TOKEN      VARCHAR2(100) NOT NULL,
                              EXPIRES_AT TIMESTAMP     NOT NULL,
                              CREATED_AT TIMESTAMP     DEFAULT SYSTIMESTAMP NOT NULL
                            )';
                        END IF;
                      END;",

                    // LOGIN_HISTORY
                    @"DECLARE c NUMBER;
                      BEGIN
                        SELECT COUNT(*) INTO c FROM user_tables WHERE table_name='LOGIN_HISTORY';
                        IF c=0 THEN
                          EXECUTE IMMEDIATE '
                            CREATE TABLE LOGIN_HISTORY (
                              ID         NUMBER GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
                              USERNAME   VARCHAR2(100) NOT NULL,
                              IP_ADDRESS VARCHAR2(50)  DEFAULT '''' NOT NULL,
                              SUCCESS    NUMBER(1)     DEFAULT 1    NOT NULL,
                              LOGIN_AT   TIMESTAMP     DEFAULT SYSTIMESTAMP NOT NULL
                            )';
                          EXECUTE IMMEDIATE 'CREATE INDEX IDX_LH_USER ON LOGIN_HISTORY(USERNAME)';
                          EXECUTE IMMEDIATE 'CREATE INDEX IDX_LH_AT   ON LOGIN_HISTORY(LOGIN_AT DESC)';
                        END IF;
                      END;",

                    // QUERY_HISTORY
                    @"DECLARE c NUMBER;
                      BEGIN
                        SELECT COUNT(*) INTO c FROM user_tables WHERE table_name='QUERY_HISTORY';
                        IF c=0 THEN
                          EXECUTE IMMEDIATE '
                            CREATE TABLE QUERY_HISTORY (
                              ID           NUMBER GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
                              USERNAME     VARCHAR2(100) NOT NULL,
                              DISPLAY_NAME VARCHAR2(200) DEFAULT '''' NOT NULL,
                              ENVIRONMENT  VARCHAR2(20)  DEFAULT '''' NOT NULL,
                              SQL_TEXT     CLOB          NOT NULL,
                              CLIENT_IP    VARCHAR2(50)  DEFAULT '''' NOT NULL,
                              ROWS_AFFECTED NUMBER       DEFAULT 0    NOT NULL,
                              DURATION_MS  NUMBER        DEFAULT 0    NOT NULL,
                              IS_ERROR     NUMBER(1)     DEFAULT 0    NOT NULL,
                              EXECUTED_AT  TIMESTAMP     DEFAULT SYSTIMESTAMP NOT NULL
                            )';
                          EXECUTE IMMEDIATE 'CREATE INDEX IDX_QH_USER ON QUERY_HISTORY(USERNAME)';
                          EXECUTE IMMEDIATE 'CREATE INDEX IDX_QH_AT   ON QUERY_HISTORY(EXECUTED_AT DESC)';
                        END IF;
                      END;",

                    // MIGRATION_HISTORY
                    @"DECLARE c NUMBER;
                      BEGIN
                        SELECT COUNT(*) INTO c FROM user_tables WHERE table_name='MIGRATION_HISTORY';
                        IF c=0 THEN
                          EXECUTE IMMEDIATE '
                            CREATE TABLE MIGRATION_HISTORY (
                              ID           VARCHAR2(20)  PRIMARY KEY,
                              USERNAME     VARCHAR2(100) NOT NULL,
                              DISPLAY_NAME VARCHAR2(200) DEFAULT '''' NOT NULL,
                              ENVIRONMENT  VARCHAR2(50)  NOT NULL,
                              FILE_NAME    VARCHAR2(500) NOT NULL,
                              CLIENT_IP    VARCHAR2(50)  DEFAULT '''' NOT NULL,
                              TOTAL_STMTS  NUMBER        DEFAULT 0 NOT NULL,
                              OK_STMTS     NUMBER        DEFAULT 0 NOT NULL,
                              ERR_STMTS    NUMBER        DEFAULT 0 NOT NULL,
                              OUTCOME      VARCHAR2(20)  NOT NULL,
                              EXECUTED_AT  TIMESTAMP     DEFAULT SYSTIMESTAMP NOT NULL
                            )';
                          EXECUTE IMMEDIATE 'CREATE INDEX IDX_MH_USER ON MIGRATION_HISTORY(USERNAME)';
                          EXECUTE IMMEDIATE 'CREATE INDEX IDX_MH_AT   ON MIGRATION_HISTORY(EXECUTED_AT DESC)';
                        END IF;
                      END;",

                    // ADMIN_ACTIVITY
                    @"DECLARE c NUMBER;
                      BEGIN
                        SELECT COUNT(*) INTO c FROM user_tables WHERE table_name='ADMIN_ACTIVITY';
                        IF c=0 THEN
                          EXECUTE IMMEDIATE '
                            CREATE TABLE ADMIN_ACTIVITY (
                              ID           NUMBER GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
                              ADMIN_USER   VARCHAR2(100)  NOT NULL,
                              ACTION       VARCHAR2(100)  NOT NULL,
                              TARGET       VARCHAR2(200)  DEFAULT '''' NOT NULL,
                              DETAILS      VARCHAR2(2000) DEFAULT '''' NOT NULL,
                              PERFORMED_AT TIMESTAMP DEFAULT SYSTIMESTAMP NOT NULL
                            )';
                          EXECUTE IMMEDIATE 'CREATE INDEX IDX_AA_ADMIN ON ADMIN_ACTIVITY(ADMIN_USER)';
                          EXECUTE IMMEDIATE 'CREATE INDEX IDX_AA_AT    ON ADMIN_ACTIVITY(PERFORMED_AT DESC)';
                        END IF;
                      END;",

                    // USER_FEEDBACK
                    @"DECLARE c NUMBER;
                      BEGIN
                        SELECT COUNT(*) INTO c FROM user_tables WHERE table_name='USER_FEEDBACK';
                        IF c=0 THEN
                          EXECUTE IMMEDIATE '
                            CREATE TABLE USER_FEEDBACK (
                              ID           NUMBER GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
                              USERNAME     VARCHAR2(100)  NOT NULL,
                              DISPLAY_NAME VARCHAR2(200)  DEFAULT '''' NOT NULL,
                              SUBJECT      VARCHAR2(300)  NOT NULL,
                              MESSAGE      CLOB           NOT NULL,
                              STATUS       VARCHAR2(20)   DEFAULT ''Open'' NOT NULL,
                              ADMIN_REPLY  CLOB,
                              CREATED_AT   TIMESTAMP      DEFAULT SYSTIMESTAMP NOT NULL,
                              REPLIED_AT   TIMESTAMP
                            )';
                          EXECUTE IMMEDIATE 'CREATE INDEX IDX_FB_USER ON USER_FEEDBACK(USERNAME)';
                          EXECUTE IMMEDIATE 'CREATE INDEX IDX_FB_STATUS ON USER_FEEDBACK(STATUS)';
                        END IF;
                      END;",

                    // POPUP_NOTIFICATIONS
                    @"DECLARE c NUMBER;
                      BEGIN
                        SELECT COUNT(*) INTO c FROM user_tables WHERE table_name='POPUP_NOTIFICATIONS';
                        IF c=0 THEN
                          EXECUTE IMMEDIATE '
                            CREATE TABLE POPUP_NOTIFICATIONS (
                              ID          NUMBER GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
                              TITLE       VARCHAR2(300)  NOT NULL,
                              BODY        CLOB           NOT NULL,
                              IS_ACTIVE   NUMBER(1)      DEFAULT 1 NOT NULL,
                              CREATED_BY  VARCHAR2(100)  NOT NULL,
                              CREATED_AT  TIMESTAMP      DEFAULT SYSTIMESTAMP NOT NULL,
                              EXPIRES_AT  TIMESTAMP
                            )';
                        END IF;
                      END;",

                    // POPUP_ACKNOWLEDGEMENTS
                    @"DECLARE c NUMBER;
                      BEGIN
                        SELECT COUNT(*) INTO c FROM user_tables WHERE table_name='POPUP_ACKNOWLEDGEMENTS';
                        IF c=0 THEN
                          EXECUTE IMMEDIATE '
                            CREATE TABLE POPUP_ACKNOWLEDGEMENTS (
                              ID         NUMBER GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
                              USERNAME   VARCHAR2(100) NOT NULL,
                              POPUP_ID   NUMBER        NOT NULL,
                              ACK_AT     TIMESTAMP     DEFAULT SYSTIMESTAMP NOT NULL,
                              CONSTRAINT UQ_PA UNIQUE(USERNAME, POPUP_ID)
                            )';
                          EXECUTE IMMEDIATE 'CREATE INDEX IDX_PA_USER ON POPUP_ACKNOWLEDGEMENTS(USERNAME)';
                        END IF;
                      END;",

                    // APP_VERSION
                    @"DECLARE c NUMBER;
                      BEGIN
                        SELECT COUNT(*) INTO c FROM user_tables WHERE table_name='APP_VERSION';
                        IF c=0 THEN
                          EXECUTE IMMEDIATE '
                            CREATE TABLE APP_VERSION (
                              ID             NUMBER GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
                              VERSION_NUMBER VARCHAR2(20)  NOT NULL,
                              VERSION_DATE   DATE          DEFAULT SYSDATE NOT NULL,
                              EXPIRY_DATE    DATE          NOT NULL
                            )';
                          EXECUTE IMMEDIATE '
                            INSERT INTO APP_VERSION(VERSION_NUMBER, VERSION_DATE, EXPIRY_DATE)
                            VALUES(''1.0.0'', SYSDATE, ADD_MONTHS(SYSDATE, 12))';
                        END IF;
                      END;"
                };

                foreach (var sql in ddl)
                {
                    using var cmd = new OracleCommand(sql, conn) { CommandTimeout = 30 };
                    try { cmd.ExecuteNonQuery(); } catch (Exception ex) { LogError(ex, nameof(InitSchema)); } { }
                }

                // Seed default admin if not present
                SeedDefaultAdmin(conn);
            }
            catch (Exception ex) { LogError(ex, nameof(InitSchema)); } { /* Portal DB not configured yet — silently skip */ }
        }

        private  void SeedDefaultAdmin(OracleConnection conn)
        {
            try
            {
                using var check = new OracleCommand(
                    "SELECT COUNT(*) FROM PORTAL_USERS WHERE USERNAME='admin'", conn);
                var cnt = Convert.ToInt32(check.ExecuteScalar());
                if (cnt > 0) return;

                using var ins = new OracleCommand(
                    @"INSERT INTO PORTAL_USERS(USERNAME,PASSWORD,DISPLAY_NAME,EMAIL,IS_APPROVED,IS_ADMIN,IS_MIGRATION)
                      VALUES('admin','Admin@1234','Administrator','admin@company.com',1,1,1)", conn);
                ins.ExecuteNonQuery();
            }
            catch (Exception ex) { LogError(ex, nameof(SeedDefaultAdmin)); }
        }

        // ════════════════════════════════════════════════════════════
        // USERS
        // ════════════════════════════════════════════════════════════

        public List<AppUser> GetAllUsers()
        {
            try
            {
                using var conn = Open();
                using var cmd = new OracleCommand(
                    @"SELECT USERNAME,PASSWORD,DISPLAY_NAME,EMAIL,IS_APPROVED,IS_ADMIN,IS_MIGRATION
              FROM PORTAL_USERS ORDER BY IS_ADMIN DESC, DISPLAY_NAME", conn);

                var list = new List<AppUser>();
                using var r = cmd.ExecuteReader();

                while (r.Read())
                {
                    var u = new AppUser
                    {
                        Username = r.GetString(0),
                        Password = r.GetString(1),
                        DisplayName = r.GetString(2),
                        //Email = r.GetString(3),
                        Email = r.IsDBNull(3) ? "" : r.GetString(3),
                        IsApproved = r.GetDecimal(4) == 1,
                        IsAdmin = r.GetDecimal(5) == 1,
                        IsMigration = r.GetDecimal(6) == 1
                    };
                    u.Permissions = GetUserPermissions(u.Username);
                    list.Add(u);
                }

                return list;
            }
            catch (Exception ex) { LogError(ex, nameof(GetAllUsers));
            {
                LogError(ex, nameof(GetAllUsers));
                return new List<AppUser>(); // ✅ FIX
                }
            }
        }

        public AppUser? GetUser(string username)
        {
            try
            {
                using var conn = Open();
                using var cmd = new OracleCommand(
                    @"SELECT USERNAME,PASSWORD,DISPLAY_NAME,EMAIL,IS_APPROVED,IS_ADMIN,IS_MIGRATION
                      FROM PORTAL_USERS WHERE USERNAME=:u", conn);
                cmd.Parameters.Add("u", OracleDbType.Varchar2).Value = username;
                using var r = cmd.ExecuteReader();
                if (!r.Read()) return null;
                var user = new AppUser
                {
                    Username = r.GetString(0),
                    Password = r.GetString(1),
                    DisplayName = r.GetString(2),
                    Email = r.IsDBNull(3) ? "" : r.GetString(3),
                    IsApproved = r.GetDecimal(4) == 1,
                    IsAdmin = r.GetDecimal(5) == 1,
                    IsMigration = r.GetDecimal(6) == 1
                };
                user.Permissions = GetUserPermissions(user.Username);
                return user;
            }
            catch (Exception ex) { LogError(ex, nameof(GetUser)); } { return null; }
        }

        public AppUser? FindUser(string username, string password)
        {
            var u = GetUser(username);
            if (u == null || u.Password != password || !u.IsApproved) return null;
            return u;
        }

        public bool UsernameExists(string username)
        {
            try
            {
                using var conn = Open();
                using var cmd = new OracleCommand(
                    "SELECT COUNT(*) FROM PORTAL_USERS WHERE USERNAME=:u", conn);
                cmd.Parameters.Add("u", OracleDbType.Varchar2).Value = username;
                return Convert.ToInt32(cmd.ExecuteScalar()) > 0;
            }
            catch (Exception ex) { LogError(ex, nameof(UsernameExists)); } { return false; }
        }

        public bool EmailExists(string email)
        {
            if (string.IsNullOrWhiteSpace(email)) return false;
            try
            {
                using var conn = Open();
                using var cmd = new OracleCommand(
                    "SELECT COUNT(*) FROM PORTAL_USERS WHERE LOWER(EMAIL)=LOWER(:e)", conn);
                cmd.Parameters.Add("e", OracleDbType.Varchar2).Value = email;
                return Convert.ToInt32(cmd.ExecuteScalar()) > 0;
            }
            catch (Exception ex) { LogError(ex, nameof(EmailExists)); } { return false; }
        }

        public List<AppUser> GetApprovedUsers() => GetAllUsers().Where(u => u.IsApproved).ToList();
        public List<AppUser> GetPendingUsers() => GetAllUsers().Where(u => !u.IsApproved).ToList();

        public void AddUser(AppUser user)
        {
            try
            {
                using var conn = Open();
                using var cmd = new OracleCommand(
                    @"INSERT INTO PORTAL_USERS(USERNAME,PASSWORD,DISPLAY_NAME,EMAIL,IS_APPROVED,IS_ADMIN,IS_MIGRATION)
                      VALUES(:u,:p,:d,:e,:a,:ad,:m)", conn);
                cmd.Parameters.Add("u", OracleDbType.Varchar2).Value = user.Username;
                cmd.Parameters.Add("p", OracleDbType.Varchar2).Value = user.Password;
                cmd.Parameters.Add("d", OracleDbType.Varchar2).Value = user.DisplayName;
                cmd.Parameters.Add("e", OracleDbType.Varchar2).Value = user.Email ?? "";
                cmd.Parameters.Add("a", OracleDbType.Int16).Value = user.IsApproved ? 1 : 0;
                cmd.Parameters.Add("ad", OracleDbType.Int16).Value = user.IsAdmin ? 1 : 0;
                cmd.Parameters.Add("m", OracleDbType.Int16).Value = user.IsMigration ? 1 : 0;
                cmd.ExecuteNonQuery();

                // If admin, auto-grant all permissions on all existing connections
                if (user.IsAdmin)
                    GrantAdminAllPermissions(user.Username, conn);
                else if (user.Permissions?.Count > 0)
                    SaveUserPermissions(user.Username, user.Permissions);
            }
            catch (Exception ex) { LogError(ex, nameof(AddUser)); } { }
        }

        public void UpdateUser(AppUser user)
        {
            try
            {
                using var conn = Open();
                using var cmd = new OracleCommand(
                    @"UPDATE PORTAL_USERS SET PASSWORD=:p,DISPLAY_NAME=:d,EMAIL=:e,
                        IS_APPROVED=:a,IS_ADMIN=:ad,IS_MIGRATION=:m
                      WHERE USERNAME=:u", conn);
                cmd.Parameters.Add("p", OracleDbType.Varchar2).Value = user.Password;
                cmd.Parameters.Add("d", OracleDbType.Varchar2).Value = user.DisplayName;
                cmd.Parameters.Add("e", OracleDbType.Varchar2).Value = user.Email ?? "";
                cmd.Parameters.Add("a", OracleDbType.Int16).Value = user.IsApproved ? 1 : 0;
                cmd.Parameters.Add("ad", OracleDbType.Int16).Value = user.IsAdmin ? 1 : 0;
                cmd.Parameters.Add("m", OracleDbType.Int16).Value = user.IsMigration ? 1 : 0;
                cmd.Parameters.Add("u", OracleDbType.Varchar2).Value = user.Username;
                cmd.ExecuteNonQuery();
            }
            catch (Exception ex) { LogError(ex, nameof(UpdateUser)); } { }
        }

        public void DeleteUser(string username)
        {
            try
            {
                using var conn = Open();
                // Cascade deletes USER_PERMISSIONS via FK
                using var cmd = new OracleCommand(
                    "DELETE FROM PORTAL_USERS WHERE USERNAME=:u", conn);
                cmd.Parameters.Add("u", OracleDbType.Varchar2).Value = username;
                cmd.ExecuteNonQuery();
                // Access requests remain (no FK) for audit trail
            }
            catch (Exception ex) { LogError(ex, nameof(DeleteUser)); } { }
        }

        public void SetApproved(string username, bool approved)
        {
            try
            {
                using var conn = Open();
                using var cmd = new OracleCommand(
                    "UPDATE PORTAL_USERS SET IS_APPROVED=:a WHERE USERNAME=:u", conn);
                cmd.Parameters.Add("a", OracleDbType.Int16).Value = approved ? 1 : 0;
                cmd.Parameters.Add("u", OracleDbType.Varchar2).Value = username;
                cmd.ExecuteNonQuery();
            }
            catch (Exception ex) { LogError(ex, nameof(SetApproved)); } { }
        }

        public void ResetPassword(string username, string newPassword)
        {
            try
            {
                using var conn = Open();
                using var cmd = new OracleCommand(
                    "UPDATE PORTAL_USERS SET PASSWORD=:p WHERE USERNAME=:u", conn);
                cmd.Parameters.Add("p", OracleDbType.Varchar2).Value = newPassword;
                cmd.Parameters.Add("u", OracleDbType.Varchar2).Value = username;
                cmd.ExecuteNonQuery();
            }
            catch (Exception ex) { LogError(ex, nameof(ResetPassword)); } { }
        }

        public void SetMigrationRole(string username, bool value)
        {
            try
            {
                using var conn = Open();
                using var cmd = new OracleCommand(
                    "UPDATE PORTAL_USERS SET IS_MIGRATION=:m WHERE USERNAME=:u", conn);
                cmd.Parameters.Add("m", OracleDbType.Int16).Value = value ? 1 : 0;
                cmd.Parameters.Add("u", OracleDbType.Varchar2).Value = username;
                cmd.ExecuteNonQuery();
            }
            catch (Exception ex) { LogError(ex, nameof(SetMigrationRole)); } { }
        }

        public bool IsMigrationUser(string username)
            => GetUser(username)?.IsMigration == true;

        // ════════════════════════════════════════════════════════════
        // PERMISSIONS
        // Admin users ALWAYS get all ops on all envs automatically.
        // ════════════════════════════════════════════════════════════

        private static readonly string[] AllOps = {
            "SELECT","INSERT","UPDATE","DELETE",
            "CREATE","DROP","ALTER","EXECUTE","VIEW_SP","MIGRATION"
        };

        /// <summary>
        /// Called when a new connection is added or an admin user created.
        /// Grants all ops on envKey to every admin user.
        /// </summary>
        public void EnsureAdminPermissionsForEnv(string envKey)
        {
            try
            {
                using var conn = Open();
                using var adminQuery = new OracleCommand(
                    "SELECT USERNAME FROM PORTAL_USERS WHERE IS_ADMIN=1", conn);
                var admins = new List<string>();
                using (var r = adminQuery.ExecuteReader())
                    while (r.Read()) admins.Add(r.GetString(0));

                foreach (var admin in admins)
                    foreach (var op in AllOps)
                        InsertPermissionIfMissing(conn, admin, envKey, op);
            }
            catch (Exception ex) { LogError(ex, nameof(EnsureAdminPermissionsForEnv)); } { }
        }

        private void GrantAdminAllPermissions(string username, OracleConnection conn)
        {
            // Grant on all envs currently in USER_PERMISSIONS or passed statically
            // We'll also grant on the known envs from the config via a separate call
            // For simplicity: insert for common envs – the runtime sync will fill gaps
            foreach (var op in AllOps)
            {
                // Use INSERT with MERGE to avoid duplicates
                try
                {
                    using var cmd = new OracleCommand(
                        @"MERGE INTO USER_PERMISSIONS UP
                          USING DUAL ON (UP.USERNAME=:u AND UP.ENV_KEY=:e AND UP.OPERATION=:o)
                          WHEN NOT MATCHED THEN INSERT(USERNAME,ENV_KEY,OPERATION) VALUES(:u,:e,:o)",
                        conn);
                    foreach (var envKey in _envKeys)
                    {
                        cmd.Parameters.Clear();
                        cmd.Parameters.Add("u", OracleDbType.Varchar2).Value = username;
                        cmd.Parameters.Add("e", OracleDbType.Varchar2).Value = envKey;
                        cmd.Parameters.Add("o", OracleDbType.Varchar2).Value = op;

                        try { cmd.ExecuteNonQuery(); }
                        catch (Exception ex) { LogError(ex, nameof(GrantAdminAllPermissions)); }
                    }
                }
                catch (Exception ex) { LogError(ex, nameof(GrantAdminAllPermissions)); } { }
            }
        }

        private  void InsertPermissionIfMissing(OracleConnection conn,
            string username, string envKey, string op)
        {
            try
            {
                using var cmd = new OracleCommand(
                    @"MERGE INTO USER_PERMISSIONS UP
                      USING DUAL ON (UP.USERNAME=:u AND UP.ENV_KEY=:e AND UP.OPERATION=:o)
                      WHEN NOT MATCHED THEN INSERT(USERNAME,ENV_KEY,OPERATION) VALUES(:u,:e,:o)",
                    conn);
                cmd.Parameters.Add("u", OracleDbType.Varchar2).Value = username;
                cmd.Parameters.Add("e", OracleDbType.Varchar2).Value = envKey;
                cmd.Parameters.Add("o", OracleDbType.Varchar2).Value = op;
                cmd.ExecuteNonQuery();
            }
            catch (Exception ex) { LogError(ex, nameof(InsertPermissionIfMissing)); } { }
        }

        public Dictionary<string, List<string>> GetUserPermissions(string username)
        {
            var map = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            try
            {
                using var conn = Open();
                // Check if admin — if so, return synthetic full permissions
                using var adminCheck = new OracleCommand(
                    "SELECT IS_ADMIN FROM PORTAL_USERS WHERE USERNAME=:u", conn);
                adminCheck.Parameters.Add("u", OracleDbType.Varchar2).Value = username;
                var isAdmin = false;
                var val = adminCheck.ExecuteScalar();
                if (val != null) isAdmin = Convert.ToDecimal(val) == 1;

                using var cmd = new OracleCommand(
                    "SELECT ENV_KEY, OPERATION FROM USER_PERMISSIONS WHERE USERNAME=:u ORDER BY ENV_KEY",
                    conn);
                cmd.Parameters.Add("u", OracleDbType.Varchar2).Value = username;
                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    string env = r.GetString(0);
                    string op = r.GetString(1);
                    if (!map.ContainsKey(env)) map[env] = new();
                    map[env].Add(op);
                }
            }
            catch (Exception ex) { LogError(ex, nameof(GetUserPermissions)); } { }
            return map;
        }

        public List<string> GetUserEnvPermissions(string username, string env)
        {
            try
            {
                var user = GetUser(username);
                if (user?.IsAdmin == true) return new List<string>(AllOps);

                using var conn = Open();
                using var cmd = new OracleCommand(
                    "SELECT OPERATION FROM USER_PERMISSIONS WHERE USERNAME=:u AND ENV_KEY=:e",
                    conn);

                cmd.Parameters.Add("u", OracleDbType.Varchar2).Value = username;
                cmd.Parameters.Add("e", OracleDbType.Varchar2).Value = env;

                var list = new List<string>();
                using var r = cmd.ExecuteReader();

                while (r.Read())
                    list.Add(r.GetString(0));

                return list;
            }
            catch (Exception ex)
            {
                LogError(ex, nameof(GetUserEnvPermissions));
                {
                    LogError(ex, nameof(GetUserEnvPermissions));
                    return new List<string>(); // ✅ FIX
                }
            }
        }
        public void SaveUserPermissions(string username,
            Dictionary<string, List<string>> permissions)
        {
            try
            {
                using var conn = Open();
                // Delete existing
                using var del = new OracleCommand(
                    "DELETE FROM USER_PERMISSIONS WHERE USERNAME=:u", conn);
                del.Parameters.Add("u", OracleDbType.Varchar2).Value = username;
                del.ExecuteNonQuery();

                // Re-insert
                foreach (var (env, ops) in permissions)
                    foreach (var op in ops)
                        InsertPermissionIfMissing(conn, username, env, op);
            }
            catch (Exception ex) { LogError(ex, nameof(SaveUserPermissions)); } { }
        }

        public void GrantPermission(string username, string env, string operation)
        {
            try
            {
                using var conn = Open();
                InsertPermissionIfMissing(conn, username, env, operation);
            }
            catch (Exception ex) { LogError(ex, nameof(GrantPermission)); } { }
        }

        public bool UserHasPermission(string username, string env, string op)
        {
            // Admins always have all permissions
            var user = GetUser(username);
            if (user?.IsAdmin == true) return true;

            var ops = GetUserEnvPermissions(username, env);
            return ops.Any(o => o.Equals(op, StringComparison.OrdinalIgnoreCase));
        }

        public bool UserHasViewSpPermission(string username)
        {
            var user = GetUser(username);
            if (user?.IsAdmin == true) return true;
            var perms = GetUserPermissions(username);
            return perms.Values.Any(ops =>
                ops.Any(o => o.Equals("VIEW_SP", StringComparison.OrdinalIgnoreCase)));
        }

        // ════════════════════════════════════════════════════════════
        // ACCESS REQUESTS
        // ════════════════════════════════════════════════════════════

        public List<AccessRequest> GetAccessRequests()
        {
            try
            {
                using var conn = Open();
                using var cmd = new OracleCommand(
                    @"SELECT ID,USERNAME,DISPLAY_NAME,ENV_KEY,OPERATION,REASON,STATUS,ADMIN_NOTE,REQUESTED_AT
              FROM ACCESS_REQUESTS
              ORDER BY CASE STATUS WHEN 'Pending' THEN 0 ELSE 1 END, REQUESTED_AT DESC",
                    conn);

                var list = new List<AccessRequest>();
                using var r = cmd.ExecuteReader();

                while (r.Read())
                {
                    list.Add(new AccessRequest
                    {
                        Id = r.GetString(0),
                        Username = r.GetString(1),
                        DisplayName = r.GetString(2),
                        Env = r.IsDBNull(3) ? "" : r.GetString(3),
                        Operation = r.GetString(4),
                        Reason = r.IsDBNull(5) ? "" : r.GetString(5),
                        Status = r.GetString(6),
                        AdminNote = r.IsDBNull(7) ? "" : r.GetString(7),
                        RequestedAt = r.GetDateTime(8)
                    });
                }

                return list;
            }
            catch (Exception ex) { LogError(ex, nameof(GetAccessRequests)); 
            {
                LogError(ex, nameof(GetAccessRequests));
                return new List<AccessRequest>(); // ✅ FIX
            }
            }
        }

        public void AddAccessRequest(AccessRequest req)
        {
            using var conn = Open();
            using var cmd = new OracleCommand(
                @"INSERT INTO ACCESS_REQUESTS(ID,USERNAME,DISPLAY_NAME,ENV_KEY,OPERATION,REASON,STATUS,ADMIN_NOTE,REQUESTED_AT)
                  VALUES(:id,:u,:d,:e,:o,:r,:s,:n,:at)", conn);
            cmd.Parameters.Add("id", OracleDbType.Varchar2).Value = req.Id;
            cmd.Parameters.Add("u", OracleDbType.Varchar2).Value = req.Username;
            cmd.Parameters.Add("d", OracleDbType.Varchar2).Value = req.DisplayName;
            cmd.Parameters.Add("e", OracleDbType.Varchar2).Value = req.Env ?? "";
            cmd.Parameters.Add("o", OracleDbType.Varchar2).Value = req.Operation;
            cmd.Parameters.Add("r", OracleDbType.Varchar2).Value = req.Reason ?? "";
            cmd.Parameters.Add("s", OracleDbType.Varchar2).Value = req.Status;
            cmd.Parameters.Add("n", OracleDbType.Varchar2).Value = req.AdminNote ?? "";
            cmd.Parameters.Add("at", OracleDbType.TimeStamp).Value = req.RequestedAt;
            cmd.ExecuteNonQuery();
        }

        public bool UpdateAccessRequest(string id, string status, string? note)
        {
            try
            {
                using var conn = Open();
                using var cmd = new OracleCommand(
                    "UPDATE ACCESS_REQUESTS SET STATUS=:s,ADMIN_NOTE=:n WHERE ID=:id", conn);
                cmd.Parameters.Add("s", OracleDbType.Varchar2).Value = status;
                cmd.Parameters.Add("n", OracleDbType.Varchar2).Value = note ?? "";
                cmd.Parameters.Add("id", OracleDbType.Varchar2).Value = id;
                return cmd.ExecuteNonQuery() > 0;
            }
            catch (Exception ex) { LogError(ex, nameof(UpdateAccessRequest)); } { return false; }
        }

        // ════════════════════════════════════════════════════════════
        // RESET TOKENS
        // ════════════════════════════════════════════════════════════

        public string CreateResetToken(string username)
        {
            string token = Guid.NewGuid().ToString("N");
            try
            {
                using var conn = Open();
                using var cmd = new OracleCommand(
                    @"MERGE INTO RESET_TOKENS RT USING DUAL
                      ON (RT.USERNAME=:u)
                      WHEN MATCHED THEN UPDATE SET TOKEN=:t,EXPIRES_AT=:ex,CREATED_AT=SYSTIMESTAMP
                      WHEN NOT MATCHED THEN INSERT(USERNAME,TOKEN,EXPIRES_AT)
                        VALUES(:u,:t,:ex)", conn);
                cmd.Parameters.Add("u", OracleDbType.Varchar2).Value = username.ToLowerInvariant();
                cmd.Parameters.Add("t", OracleDbType.Varchar2).Value = token;
                cmd.Parameters.Add("ex", OracleDbType.TimeStamp).Value = DateTime.UtcNow.AddHours(1);
                cmd.ExecuteNonQuery();
            }
            catch (Exception ex) { LogError(ex, nameof(CreateResetToken)); } { }
            return token;
        }

        public string? ValidateResetToken(string token)
        {
            try
            {
                using var conn = Open();
                using var cmd = new OracleCommand(
                    "SELECT USERNAME FROM RESET_TOKENS WHERE TOKEN=:t AND EXPIRES_AT>SYSTIMESTAMP",
                    conn);
                cmd.Parameters.Add("t", OracleDbType.Varchar2).Value = token;
                var result = cmd.ExecuteScalar();
                return result?.ToString();
            }
            catch (Exception ex) { LogError(ex, nameof(ValidateResetToken)); } { return null; }
        }

        public void InvalidateResetToken(string username)
        {
            try
            {
                using var conn = Open();
                using var cmd = new OracleCommand(
                    "DELETE FROM RESET_TOKENS WHERE USERNAME=:u", conn);
                cmd.Parameters.Add("u", OracleDbType.Varchar2).Value = username.ToLowerInvariant();
                cmd.ExecuteNonQuery();
            }
            catch (Exception ex) { LogError(ex, nameof(InvalidateResetToken)); } { }
        }

        // ════════════════════════════════════════════════════════════
        // LOGIN HISTORY
        // ════════════════════════════════════════════════════════════

        public void RecordLogin(string username, string ip, bool success)
        {
            try
            {
                using var conn = Open();
                using var cmd = new OracleCommand(
                    "INSERT INTO LOGIN_HISTORY(USERNAME,IP_ADDRESS,SUCCESS) VALUES(:u,:i,:s)", conn);
                cmd.Parameters.Add("u", OracleDbType.Varchar2).Value = username;
                cmd.Parameters.Add("i", OracleDbType.Varchar2).Value = ip;
                cmd.Parameters.Add("s", OracleDbType.Int16).Value = success ? 1 : 0;
                cmd.ExecuteNonQuery();
            }
            catch (Exception ex) { LogError(ex, nameof(RecordLogin)); } { }
        }

        public List<LoginHistory> GetLoginHistory(int count = 500)
        {
            try
            {
                using var conn = Open();
                using var cmd = new OracleCommand(
                    @"SELECT USERNAME,IP_ADDRESS,SUCCESS,LOGIN_AT
              FROM (SELECT * FROM LOGIN_HISTORY ORDER BY ID DESC)
              WHERE ROWNUM<=:n", conn);

                cmd.Parameters.Add("n", OracleDbType.Int32).Value = count;

                var list = new List<LoginHistory>();
                using var r = cmd.ExecuteReader();

                while (r.Read())
                {
                    list.Add(new LoginHistory
                    {
                        Username = r.GetString(0),
                        IpAddress = r.GetString(1),
                        Success = r.GetDecimal(2) == 1,
                        LoginAt = r.GetDateTime(3)
                    });
                }

                return list;
            }
            catch (Exception ex) { LogError(ex, nameof(GetLoginHistory)); 
            {
                LogError(ex, nameof(GetLoginHistory));
                return new List<LoginHistory>(); // ✅ FIX
            }
            }
        }
        // ════════════════════════════════════════════════════════════
        // QUERY HISTORY
        // ════════════════════════════════════════════════════════════

        public void RecordQuery(QueryHistory q)
        {
            try
            {
                using var conn = Open();
                using var cmd = new OracleCommand(
                    @"INSERT INTO QUERY_HISTORY
                        (USERNAME,DISPLAY_NAME,ENVIRONMENT,SQL_TEXT,CLIENT_IP,ROWS_AFFECTED,DURATION_MS,IS_ERROR)
                      VALUES(:u,:d,:e,:s,:ip,:r,:ms,:err)", conn);
                cmd.Parameters.Add("u", OracleDbType.Varchar2).Value = q.Username;
                cmd.Parameters.Add("d", OracleDbType.Varchar2).Value = q.DisplayName ?? "";
                cmd.Parameters.Add("e", OracleDbType.Varchar2).Value = q.Environment ?? "";
                cmd.Parameters.Add("s", OracleDbType.Clob).Value = q.Sql ?? "";
                cmd.Parameters.Add("ip", OracleDbType.Varchar2).Value = q.ClientIp ?? "";
                cmd.Parameters.Add("r", OracleDbType.Int32).Value = q.Rows;
                cmd.Parameters.Add("ms", OracleDbType.Int64).Value = q.DurationMs;
                cmd.Parameters.Add("err", OracleDbType.Int16).Value = q.IsError ? 1 : 0;
                cmd.ExecuteNonQuery();
            }
            catch (Exception ex) { LogError(ex, nameof(RecordQuery)); } { }
        }

        public List<QueryHistory> GetQueryHistory(int count = 500)
        {
            try
            {
                using var conn = Open();
                using var cmd = new OracleCommand(
                    @"SELECT USERNAME,DISPLAY_NAME,ENVIRONMENT,SQL_TEXT,CLIENT_IP,
                     ROWS_AFFECTED,DURATION_MS,IS_ERROR,EXECUTED_AT
              FROM (SELECT * FROM QUERY_HISTORY ORDER BY ID DESC)
              WHERE ROWNUM<=:n", conn);

                cmd.Parameters.Add("n", OracleDbType.Int32).Value = count;

                var list = new List<QueryHistory>();
                using var r = cmd.ExecuteReader();

                while (r.Read())
                {
                    list.Add(new QueryHistory
                    {
                        Username = r.GetString(0),
                        DisplayName = r.GetString(1),
                        Environment = r.GetString(2),
                        Sql = r.GetString(3),
                        ClientIp = r.GetString(4),
                        Rows = (int)r.GetDecimal(5),
                        DurationMs = (long)r.GetDecimal(6),
                        IsError = r.GetDecimal(7) == 1,
                        ExecutedAt = r.GetDateTime(8)
                    });
                }

                return list;
            }
            catch (Exception ex) { LogError(ex, nameof(GetQueryHistory)); 
            {
                LogError(ex, nameof(GetQueryHistory));
                return new List<QueryHistory>(); // ✅ FIX
            }
        }
        }
        // ════════════════════════════════════════════════════════════
        // MIGRATION HISTORY
        // ════════════════════════════════════════════════════════════

        public void RecordMigration(MigrationSession s, string displayName, string outcome)
        {
            try
            {
                using var conn = Open();
                using var cmd = new OracleCommand(
                    @"INSERT INTO MIGRATION_HISTORY
                        (ID,USERNAME,DISPLAY_NAME,ENVIRONMENT,FILE_NAME,CLIENT_IP,TOTAL_STMTS,OK_STMTS,ERR_STMTS,OUTCOME)
                      VALUES(:id,:u,:d,:e,:f,:ip,:t,:ok,:err,:out)", conn);
                cmd.Parameters.Add("id", OracleDbType.Varchar2).Value = s.SessionId;
                cmd.Parameters.Add("u", OracleDbType.Varchar2).Value = s.Username;
                cmd.Parameters.Add("d", OracleDbType.Varchar2).Value = displayName;
                cmd.Parameters.Add("e", OracleDbType.Varchar2).Value = s.Environment;
                cmd.Parameters.Add("f", OracleDbType.Varchar2).Value = s.FileName;
                cmd.Parameters.Add("ip", OracleDbType.Varchar2).Value = s.ClientIp;
                cmd.Parameters.Add("t", OracleDbType.Int32).Value = s.Results.Count;
                cmd.Parameters.Add("ok", OracleDbType.Int32).Value = s.Results.Count(r => r.Success);
                cmd.Parameters.Add("err", OracleDbType.Int32).Value = s.Results.Count(r => !r.Success);
                cmd.Parameters.Add("out", OracleDbType.Varchar2).Value = outcome;
                cmd.ExecuteNonQuery();
            }
            catch (Exception ex) { LogError(ex, nameof(RecordMigration)); } { }
        }

        public List<MigrationHistory> GetMigrationHistory(int count = 200)
        {
            try
            {
                using var conn = Open();
                using var cmd = new OracleCommand(
                    @"SELECT ID,USERNAME,DISPLAY_NAME,ENVIRONMENT,FILE_NAME,CLIENT_IP,
                     TOTAL_STMTS,OK_STMTS,ERR_STMTS,OUTCOME,EXECUTED_AT
              FROM (SELECT * FROM MIGRATION_HISTORY ORDER BY EXECUTED_AT DESC)
              WHERE ROWNUM<=:n", conn);

                cmd.Parameters.Add("n", OracleDbType.Int32).Value = count;

                var list = new List<MigrationHistory>();
                using var r = cmd.ExecuteReader();

                while (r.Read())
                {
                    list.Add(new MigrationHistory
                    {
                        Id = r.GetString(0),
                        Username = r.GetString(1),
                        DisplayName = r.GetString(2),
                        Environment = r.GetString(3),
                        FileName = r.GetString(4),
                        ClientIp = r.GetString(5),
                        TotalStmts = (int)r.GetDecimal(6),
                        OkStmts = (int)r.GetDecimal(7),
                        ErrStmts = (int)r.GetDecimal(8),
                        Outcome = r.GetString(9),
                        ExecutedAt = r.GetDateTime(10)
                    });
                }

                return list;
            }
            catch (Exception ex) { LogError(ex, nameof(GetMigrationHistory)); 
            {
                LogError(ex, nameof(GetMigrationHistory));
                return new List<MigrationHistory>(); // ✅ FIX
            }
            }
        }
        // ════════════════════════════════════════════════════════════
        // ADMIN ACTIVITY
        // ════════════════════════════════════════════════════════════

        public void RecordAdminActivity(string adminUser, string action, string target, string details)
        {
            try
            {
                using var conn = Open();
                using var cmd = new OracleCommand(
                    "INSERT INTO ADMIN_ACTIVITY(ADMIN_USER,ACTION,TARGET,DETAILS) VALUES(:a,:act,:t,:d)", conn);
                cmd.Parameters.Add("a", OracleDbType.Varchar2).Value = adminUser;
                cmd.Parameters.Add("act", OracleDbType.Varchar2).Value = action;
                cmd.Parameters.Add("t", OracleDbType.Varchar2).Value = target;
                cmd.Parameters.Add("d", OracleDbType.Varchar2).Value = details;
                cmd.ExecuteNonQuery();
            }
            catch (Exception ex) { LogError(ex, nameof(RecordAdminActivity)); } { }
        }
     
        public List<AdminActivity> GetAdminActivity(int count = 500)
        {
            try
            {
                using var conn = Open();
                using var cmd = new OracleCommand(
                    @"SELECT ADMIN_USER,ACTION,TARGET,DETAILS,PERFORMED_AT
              FROM (SELECT * FROM ADMIN_ACTIVITY ORDER BY ID DESC)
              WHERE ROWNUM<=:n", conn);

                cmd.Parameters.Add("n", OracleDbType.Int32).Value = count;

                var list = new List<AdminActivity>();
                using var r = cmd.ExecuteReader();

                while (r.Read())
                {
                    list.Add(new AdminActivity
                    {
                        AdminUser = r.GetString(0),
                        Action = r.GetString(1),
                        Target = r.GetString(2),
                        Details = r.GetString(3),
                        PerformedAt = r.GetDateTime(4)
                    });
                }

                return list;
            }
            catch (Exception ex) { LogError(ex, nameof(GetAdminActivity)); 
            {
                LogError(ex, nameof(GetAdminActivity));
                return new List<AdminActivity>(); // ✅ FIX
            }
            }
        }

        // ════════════════════════════════════════════════════════════
        // PORTAL DB HEALTH
        // ════════════════════════════════════════════════════════════

        public bool TestPortalDb(out string error)
        {
            try
            {
                using var conn = Open();
                using var cmd = new OracleCommand("SELECT 1 FROM DUAL", conn);
                cmd.ExecuteScalar();
                error = ""; return true;
            }
            catch  (Exception ex) { error = ex.Message; return false; }
        }
        private void LogError(Exception ex, string method, string? extra = null, string? username = null)
        {
            // 1. Log to file/console
            _logger.LogError(ex, "Error in {Method} | {Message}", method, ex.Message);

            // 2. Log to DB
            try
            {
                LogErrorToDb(new ErrorLog
                {
                   // Username = username,
                   Username= _httpContext.HttpContext?.Session.GetString("username") ?? "admin",
                    Method = method,
                    Message = ex.Message,
                    StackTrace = ex.ToString(),
                    Extra = extra
                });
            }
            catch 
            {
                // prevent crash if logging fails
            }
        }
        public void LogErrorToDb(ErrorLog log)
        {
            try
            {
                using var conn = Open();
                using var cmd = new OracleCommand(
                    @"INSERT INTO ERROR_LOG (USERNAME, METHOD, MESSAGE, STACK_TRACE, EXTRA)
              VALUES (:u, :m, :msg, :st, :ex)", conn);

                cmd.Parameters.Add("u", OracleDbType.Varchar2).Value = log.Username ?? "";
                cmd.Parameters.Add("m", OracleDbType.Varchar2).Value = log.Method;
                cmd.Parameters.Add("msg", OracleDbType.Varchar2).Value = log.Message;
                cmd.Parameters.Add("st", OracleDbType.Clob).Value = log.StackTrace;
                cmd.Parameters.Add("ex", OracleDbType.Varchar2).Value = log.Extra ?? "";

                cmd.ExecuteNonQuery();
            }
            catch (Exception ex) { LogError(ex, nameof(LogErrorToDb)); }
            {
                // ⚠️ NEVER throw here → avoid infinite loop if DB fails
            }
        }
        public string GetDbDescription()
        {
            return System.Text.RegularExpressions.Regex.Replace(
                _connStr, @"(?i)(Password\s*=\s*)([^;]+)", "$1****");
        }

        // ════════════════════════════════════════════════════════════
        // FEEDBACK
        // ════════════════════════════════════════════════════════════

        public void AddFeedback(UserFeedback fb)
        {
            try
            {
                using var conn = Open();
                using var cmd = new OracleCommand(
                    @"INSERT INTO USER_FEEDBACK(USERNAME,DISPLAY_NAME,SUBJECT,MESSAGE,STATUS)
                      VALUES(:u,:d,:s,:m,'Open')", conn);
                cmd.Parameters.Add("u", OracleDbType.Varchar2).Value = fb.Username;
                cmd.Parameters.Add("d", OracleDbType.Varchar2).Value = fb.DisplayName;
                cmd.Parameters.Add("s", OracleDbType.Varchar2).Value = fb.Subject;
                cmd.Parameters.Add("m", OracleDbType.Clob).Value = fb.Message;
                cmd.ExecuteNonQuery();
            }
            catch (Exception ex) { LogError(ex, nameof(AddFeedback)); }
        }

        public List<UserFeedback> GetAllFeedback()
        {
            var list = new List<UserFeedback>();
            try
            {
                using var conn = Open();
                using var cmd = new OracleCommand(
                    @"SELECT ID,USERNAME,DISPLAY_NAME,SUBJECT,MESSAGE,STATUS,ADMIN_REPLY,CREATED_AT,REPLIED_AT
                      FROM USER_FEEDBACK ORDER BY ID DESC", conn);
                using var r = cmd.ExecuteReader();
                while (r.Read())
                    list.Add(new UserFeedback
                    {
                        Id = (long)r.GetDecimal(0),
                        Username = r.GetString(1),
                        DisplayName = r.GetString(2),
                        Subject = r.GetString(3),
                        Message = r.GetString(4),
                        Status = r.GetString(5),
                        AdminReply = r.IsDBNull(6) ? null : r.GetString(6),
                        CreatedAt = r.GetDateTime(7),
                        RepliedAt = r.IsDBNull(8) ? null : r.GetDateTime(8)
                    });
            }
            catch (Exception ex) { LogError(ex, nameof(GetAllFeedback)); }
            return list;
        }

        public void ReplyFeedback(long id, string reply, string status)
        {
            try
            {
                using var conn = Open();
                using var cmd = new OracleCommand(
                    "UPDATE USER_FEEDBACK SET ADMIN_REPLY=:r,STATUS=:s,REPLIED_AT=SYSTIMESTAMP WHERE ID=:id", conn);
                cmd.Parameters.Add("r", OracleDbType.Varchar2).Value = reply;
                cmd.Parameters.Add("s", OracleDbType.Varchar2).Value = status;
                cmd.Parameters.Add("id", OracleDbType.Int64).Value = id;
                cmd.ExecuteNonQuery();
            }
            catch (Exception ex) { LogError(ex, nameof(ReplyFeedback)); }
        }

        public List<UserFeedback> GetUserFeedback(string username)
        {
            var list = new List<UserFeedback>();
            try
            {
                using var conn = Open();
                using var cmd = new OracleCommand(
                    @"SELECT ID,USERNAME,DISPLAY_NAME,SUBJECT,MESSAGE,STATUS,ADMIN_REPLY,CREATED_AT,REPLIED_AT
                      FROM USER_FEEDBACK WHERE USERNAME=:u ORDER BY ID DESC", conn);
                cmd.Parameters.Add("u", OracleDbType.Varchar2).Value = username;
                using var r = cmd.ExecuteReader();
                while (r.Read())
                    list.Add(new UserFeedback
                    {
                        Id = (long)r.GetDecimal(0),
                        Username = r.GetString(1),
                        DisplayName = r.GetString(2),
                        Subject = r.GetString(3),
                        Message = r.GetString(4),
                        Status = r.GetString(5),
                        AdminReply = r.IsDBNull(6) ? null : r.GetString(6),
                        CreatedAt = r.GetDateTime(7),
                        RepliedAt = r.IsDBNull(8) ? null : r.GetDateTime(8)
                    });
            }
            catch (Exception ex) { LogError(ex, nameof(GetUserFeedback)); }
            return list;
        }

        // ════════════════════════════════════════════════════════════
        // POPUP NOTIFICATIONS
        // ════════════════════════════════════════════════════════════

        public void AddPopupNotification(PopupNotification n)
        {
            try
            {
                using var conn = Open();
                using var cmd = new OracleCommand(
                    @"INSERT INTO POPUP_NOTIFICATIONS(TITLE,BODY,IS_ACTIVE,CREATED_BY,EXPIRES_AT)
                      VALUES(:t,:b,1,:c,:ex)", conn);
                cmd.Parameters.Add("t", OracleDbType.Varchar2).Value = n.Title;
                cmd.Parameters.Add("b", OracleDbType.Clob).Value = n.Body;
                cmd.Parameters.Add("c", OracleDbType.Varchar2).Value = n.CreatedBy;
                cmd.Parameters.Add("ex", OracleDbType.TimeStamp).Value = (object?)n.ExpiresAt ?? DBNull.Value;
                cmd.ExecuteNonQuery();
            }
            catch (Exception ex) { LogError(ex, nameof(AddPopupNotification)); }
        }

        public List<PopupNotification> GetActivePopups()
        {
            var list = new List<PopupNotification>();
            try
            {
                using var conn = Open();
                using var cmd = new OracleCommand(
                    @"SELECT ID,TITLE,BODY,IS_ACTIVE,CREATED_BY,CREATED_AT,EXPIRES_AT
                      FROM POPUP_NOTIFICATIONS
                      WHERE IS_ACTIVE=1 AND (EXPIRES_AT IS NULL OR EXPIRES_AT > SYSTIMESTAMP)
                      ORDER BY ID DESC", conn);
                using var r = cmd.ExecuteReader();
                while (r.Read())
                    list.Add(new PopupNotification
                    {
                        Id = (long)r.GetDecimal(0),
                        Title = r.GetString(1),
                        Body = r.GetString(2),
                        IsActive = r.GetDecimal(3) == 1,
                        CreatedBy = r.GetString(4),
                        CreatedAt = r.GetDateTime(5),
                        ExpiresAt = r.IsDBNull(6) ? null : r.GetDateTime(6)
                    });
            }
            catch (Exception ex) { LogError(ex, nameof(GetActivePopups)); }
            return list;
        }

        public List<PopupNotification> GetAllPopups()
        {
            var list = new List<PopupNotification>();
            try
            {
                using var conn = Open();
                using var cmd = new OracleCommand(
                    @"SELECT ID,TITLE,BODY,IS_ACTIVE,CREATED_BY,CREATED_AT,EXPIRES_AT
                      FROM POPUP_NOTIFICATIONS ORDER BY ID DESC", conn);
                using var r = cmd.ExecuteReader();
                while (r.Read())
                    list.Add(new PopupNotification
                    {
                        Id = (long)r.GetDecimal(0),
                        Title = r.GetString(1),
                        Body = r.GetString(2),
                        IsActive = r.GetDecimal(3) == 1,
                        CreatedBy = r.GetString(4),
                        CreatedAt = r.GetDateTime(5),
                        ExpiresAt = r.IsDBNull(6) ? null : r.GetDateTime(6)
                    });
            }
            catch (Exception ex) { LogError(ex, nameof(GetAllPopups)); }
            return list;
        }

        public void SetPopupActive(long id, bool active)
        {
            try
            {
                using var conn = Open();
                using var cmd = new OracleCommand("UPDATE POPUP_NOTIFICATIONS SET IS_ACTIVE=:a WHERE ID=:id", conn);
                cmd.Parameters.Add("a", OracleDbType.Int16).Value = active ? 1 : 0;
                cmd.Parameters.Add("id", OracleDbType.Int64).Value = id;
                cmd.ExecuteNonQuery();
            }
            catch (Exception ex) { LogError(ex, nameof(SetPopupActive)); }
        }

        public bool HasUserAcknowledgedPopup(string username, long popupId)
        {
            try
            {
                using var conn = Open();
                using var cmd = new OracleCommand(
                    "SELECT COUNT(*) FROM POPUP_ACKNOWLEDGEMENTS WHERE USERNAME=:u AND POPUP_ID=:p", conn);
                cmd.Parameters.Add("u", OracleDbType.Varchar2).Value = username;
                cmd.Parameters.Add("p", OracleDbType.Int64).Value = popupId;
                return Convert.ToInt32(cmd.ExecuteScalar()) > 0;
            }
            catch (Exception ex) { LogError(ex, nameof(HasUserAcknowledgedPopup)); return false; }
        }

        public void AcknowledgePopup(string username, long popupId)
        {
            try
            {
                using var conn = Open();
                using var cmd = new OracleCommand(
                    @"MERGE INTO POPUP_ACKNOWLEDGEMENTS PA USING DUAL
                      ON (PA.USERNAME=:u AND PA.POPUP_ID=:p)
                      WHEN NOT MATCHED THEN INSERT(USERNAME,POPUP_ID) VALUES(:u,:p)", conn);
                cmd.Parameters.Add("u", OracleDbType.Varchar2).Value = username;
                cmd.Parameters.Add("p", OracleDbType.Int64).Value = popupId;
                cmd.ExecuteNonQuery();
            }
            catch (Exception ex) { LogError(ex, nameof(AcknowledgePopup)); }
        }

        public List<PopupNotification> GetUnseenPopups(string username)
        {
            var list = new List<PopupNotification>();
            try
            {
                using var conn = Open();
                using var cmd = new OracleCommand(
                    @"SELECT ID,TITLE,BODY,IS_ACTIVE,CREATED_BY,CREATED_AT,EXPIRES_AT
                      FROM POPUP_NOTIFICATIONS
                      WHERE IS_ACTIVE=1
                        AND (EXPIRES_AT IS NULL OR EXPIRES_AT > SYSTIMESTAMP)
                        AND ID NOT IN (SELECT POPUP_ID FROM POPUP_ACKNOWLEDGEMENTS WHERE USERNAME=:u)
                      ORDER BY ID DESC", conn);
                cmd.Parameters.Add("u", OracleDbType.Varchar2).Value = username;
                using var r = cmd.ExecuteReader();
                while (r.Read())
                    list.Add(new PopupNotification
                    {
                        Id = (long)r.GetDecimal(0),
                        Title = r.GetString(1),
                        Body = r.GetString(2),
                        IsActive = r.GetDecimal(3) == 1,
                        CreatedBy = r.GetString(4),
                        CreatedAt = r.GetDateTime(5),
                        ExpiresAt = r.IsDBNull(6) ? null : r.GetDateTime(6)
                    });
            }
            catch (Exception ex) { LogError(ex, nameof(GetUnseenPopups)); }
            return list;
        }

        public List<PopupNotification> GetAcknowledgedPopups(string username)
        {
            var list = new List<PopupNotification>();
            try
            {
                using var conn = Open();
                using var cmd = new OracleCommand(
                    @"SELECT P.ID,P.TITLE,P.BODY,P.IS_ACTIVE,P.CREATED_BY,P.CREATED_AT,P.EXPIRES_AT
                      FROM POPUP_NOTIFICATIONS P
                      JOIN POPUP_ACKNOWLEDGEMENTS PA ON PA.POPUP_ID=P.ID AND PA.USERNAME=:u
                      ORDER BY P.ID DESC", conn);
                cmd.Parameters.Add("u", OracleDbType.Varchar2).Value = username;
                using var r = cmd.ExecuteReader();
                while (r.Read())
                    list.Add(new PopupNotification
                    {
                        Id = (long)r.GetDecimal(0),
                        Title = r.GetString(1),
                        Body = r.GetString(2),
                        IsActive = r.GetDecimal(3) == 1,
                        CreatedBy = r.GetString(4),
                        CreatedAt = r.GetDateTime(5),
                        ExpiresAt = r.IsDBNull(6) ? null : r.GetDateTime(6)
                    });
            }
            catch (Exception ex) { LogError(ex, nameof(GetAcknowledgedPopups)); }
            return list;
        }

        // ════════════════════════════════════════════════════════════
        // APP VERSION
        // ════════════════════════════════════════════════════════════

        public AppVersion? GetAppVersion()
        {
            try
            {
                using var conn = Open();
                using var cmd = new OracleCommand(
                    @"SELECT VERSION_NUMBER,VERSION_DATE,EXPIRY_DATE FROM APP_VERSION
                      WHERE ROWNUM=1 ORDER BY ID DESC", conn);
                using var r = cmd.ExecuteReader();
                if (!r.Read()) return null;
                return new AppVersion
                {
                    VersionNumber = r.GetString(0),
                    VersionDate = r.GetDateTime(1),
                    ExpiryDate = r.GetDateTime(2)
                };
            }
            catch (Exception ex) { LogError(ex, nameof(GetAppVersion)); return null; }
        }

        // ════════════════════════════════════════════════════════════
        // SP EXECUTE WITH PARAMS
        // ════════════════════════════════════════════════════════════

        public (string output, string error) ExecuteSpWithParams(
            string spName, string env,
            List<(string name, string type, string direction, string value)> parameters)
        {
            try
            {
                using var conn = new Oracle.ManagedDataAccess.Client.OracleConnection(
                    new OracleService(_config).BuildConnStr(env));
                conn.Open();
                using var cmd = new Oracle.ManagedDataAccess.Client.OracleCommand(spName, conn);
                cmd.CommandType = System.Data.CommandType.StoredProcedure;
                cmd.CommandTimeout = 120;

                foreach (var (name, type, dir, val) in parameters)
                {
                    var p = cmd.Parameters.Add(name, Oracle.ManagedDataAccess.Client.OracleDbType.Varchar2);
                    p.Direction = dir.ToUpper() == "OUT" ? System.Data.ParameterDirection.Output
                                : dir.ToUpper() == "INOUT" ? System.Data.ParameterDirection.InputOutput
                                : System.Data.ParameterDirection.Input;
                    p.Size = 4000;
                    if (dir.ToUpper() != "OUT" && !string.IsNullOrEmpty(val))
                        p.Value = val;
                }

                cmd.ExecuteNonQuery();

                var sb = new System.Text.StringBuilder();
                foreach (Oracle.ManagedDataAccess.Client.OracleParameter p in cmd.Parameters)
                {
                    if (p.Direction == System.Data.ParameterDirection.Output ||
                        p.Direction == System.Data.ParameterDirection.InputOutput)
                        sb.AppendLine($"{p.ParameterName} = {p.Value}");
                }
                return (sb.ToString(), "");
            }
            catch (Exception ex) { return ("", ex.Message); }
        }

        public List<(string name, string type, string inOut)> GetSpParameters(string spName, string env)
        {
            var list = new List<(string, string, string)>();
            try
            {
                using var conn = new Oracle.ManagedDataAccess.Client.OracleConnection(
                    new OracleService(_config).BuildConnStr(env));
                conn.Open();
                using var cmd = new Oracle.ManagedDataAccess.Client.OracleCommand(
                    @"SELECT ARGUMENT_NAME, DATA_TYPE, IN_OUT
                      FROM USER_ARGUMENTS
                      WHERE OBJECT_NAME = :n AND ARGUMENT_NAME IS NOT NULL
                      ORDER BY POSITION", conn);
                cmd.Parameters.Add("n", Oracle.ManagedDataAccess.Client.OracleDbType.Varchar2).Value = spName.ToUpper();
                using var r = cmd.ExecuteReader();
                while (r.Read())
                    list.Add((r.GetString(0), r.IsDBNull(1) ? "VARCHAR2" : r.GetString(1),
                               r.IsDBNull(2) ? "IN" : r.GetString(2)));
            }
            catch (Exception ex) { LogError(ex, nameof(GetSpParameters)); }
            return list;
        }

        public List<string> GetObjectsLike(string pattern, string objectType, string env)
        {
            var list = new List<string>();
            try
            {
                using var conn = new Oracle.ManagedDataAccess.Client.OracleConnection(
                    new OracleService(_config).BuildConnStr(env));
                conn.Open();
                string typeFilter = objectType.ToUpper() switch {
                    "SP" or "PROCEDURE" => "AND OBJECT_TYPE='PROCEDURE'",
                    "FUNCTION"          => "AND OBJECT_TYPE='FUNCTION'",
                    "VIEW"              => "AND OBJECT_TYPE='VIEW'",
                    "TABLE"             => "AND OBJECT_TYPE='TABLE'",
                    _                   => "AND OBJECT_TYPE IN ('PROCEDURE','FUNCTION','VIEW','TABLE','PACKAGE')"
                };
                using var cmd = new Oracle.ManagedDataAccess.Client.OracleCommand(
                    $"SELECT OBJECT_NAME FROM USER_OBJECTS WHERE OBJECT_NAME LIKE :p {typeFilter} ORDER BY OBJECT_NAME",
                    conn);
                cmd.Parameters.Add("p", Oracle.ManagedDataAccess.Client.OracleDbType.Varchar2).Value =
                    "%" + pattern.ToUpper() + "%";
                using var r = cmd.ExecuteReader();
                while (r.Read()) list.Add(r.GetString(0));
            }
            catch (Exception ex) { LogError(ex, nameof(GetObjectsLike)); }
            return list;
        }
    }
}
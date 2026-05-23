using Oracle.ManagedDataAccess.Client;
using OracleSqlPortal.Models;
using System.Data;
using System.Data.Common;

namespace OracleSqlPortal.Services
{
    public class OracleService
    {
        private readonly IConfiguration _config;
        public OracleService(IConfiguration config) => _config = config;

        // ── Build connection string from structured env config ─────
        public string BuildConnStr(string env)
        {
            var s = _config.GetSection($"Environments:{env}");
            string proto   = s["Protocol"]?.ToUpperInvariant() == "TCPS" ? "TCPS" : "TCP";
            string host    = s["Host"]        ?? "localhost";
            string port    = s["Port"]        ?? (proto == "TCPS" ? "2484" : "1521");
            string service = s["ServiceName"] ?? "ORCL";
            string user    = s["UserId"]      ?? "";
            string pass    = s["Password"]    ?? "";

            string dataSource =
                $"(DESCRIPTION=(ADDRESS=(PROTOCOL={proto})(HOST={host})(PORT={port}))" +
                $"(CONNECT_DATA=(SERVICE_NAME={service})))";

            string cs = $"User Id={user};Password={pass};Data Source={dataSource};" +
                        "Pooling=true;Min Pool Size=1;Max Pool Size=20;Statement Cache Size=20;";

            if (proto == "TCPS")
            {
                //string? sqlnetDir = s["SqlNetOraPath"];
                //string? wallet    = s["WalletPath"];
                //string? ca        = s["TrustStore"];
                //string? cert      = s["KeyStore"];
                //string? key       = s["KeyStorePwd"];
                //string? dn        = s["ServerDN"];
                //// sqlnet.ora directory (TNS_ADMIN) takes priority over wallet for path
                //if (!string.IsNullOrEmpty(sqlnetDir)) cs += $"TNS_ADMIN={sqlnetDir};";
                //if (!string.IsNullOrEmpty(wallet))    cs += $"Wallet Location=\"(SOURCE=(METHOD=file)(METHOD_DATA=(DIRECTORY={wallet})))\";";
                //if (string.IsNullOrEmpty(sqlnetDir) && !string.IsNullOrEmpty(wallet)) cs += $"TNS_ADMIN={wallet};";
                //if (!string.IsNullOrEmpty(ca))   cs += $"SSL CA Cert={ca};";
                //if (!string.IsNullOrEmpty(cert)) cs += $"SSL Certificate={cert};";
                //if (!string.IsNullOrEmpty(key))  cs += $"SSL Key={key};";
                //if (!string.IsNullOrEmpty(dn))   cs += $"SSL Server Cert DN=\"{dn}\";";
            }
            {
                //_migration.DBUsername = _migration.DBName;
                cs = string.Format("Data Source={0};persist security info=True;User ID={1};Password={2};enlist=false", dataSource, user, pass);
               //    _migration.DBProvider "Oracle.ManagedDataAccess.Client";
            }

            //if (_migration.DBProvider != null && _migration.DBConnStr != null)
            //{
            //    DbConnection con = null;
            //    DbProviderFactory factory = null;
            //    factory = DbProviderFactories.GetFactory(_migration.DBProvider);
            //    if (factory != null)
            //    {
            //        con = factory.CreateConnection();
            //        con.ConnectionString = _migration.DBConnStr;
            //        if (con != null)
            //        {
            //            using (con)
            //            {
            //                con.Close();
            //            }
            //        }
            //    }
            //}
                return cs;
        }

        // ── Execute SELECT (streaming, non-blocking) ──────────────
        public (DataTable, long) ExecuteQueryAsync(string sql, string env)
        {
            var dt = new DataTable();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            using var conn = new OracleConnection(BuildConnStr(env));
            conn.Open();
            using var cmd = new OracleCommand(sql, conn) { CommandTimeout = 120 };
            // Optimised fetch settings to avoid UI freeze on large result sets
            cmd.InitialLOBFetchSize = -1;
            cmd.FetchSize = cmd.Connection.MaxStatementCacheSize == 0
                ? 1024 * 1024 * 10   // 10 MB fetch buffer
                : 1024 * 1024 * 5;
            using var reader = cmd.ExecuteReader(System.Data.CommandBehavior.Default);
            dt.Load(reader);
            sw.Stop();
            return (dt, sw.ElapsedMilliseconds);
        }

        // ── Execute SELECT (original, kept for compatibility) ─────
        public (DataTable, long) ExecuteQuery(string sql, string env)
            => ExecuteQueryAsync(sql, env);

        // ── Execute DML ───────────────────────────────────────────
        public (int, long) ExecuteNonQuery(string sql, string env)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            using var conn = new OracleConnection(BuildConnStr(env));
            conn.Open();
            using var cmd = new OracleCommand(sql, conn) { CommandTimeout = 120 };
            int rows = cmd.ExecuteNonQuery(); sw.Stop();
            return (rows, sw.ElapsedMilliseconds);
        }

        // ── Object definition ─────────────────────────────────────
        public ObjectDefinitionResult GetObjectDefinition(string objectName, string env, string? preferredType = null)
        {
            using var conn = new OracleConnection(BuildConnStr(env));
            conn.Open();
            string? objectType;
            if (!string.IsNullOrWhiteSpace(preferredType) && preferredType != "ALL")
            {
                // Map short codes to Oracle types
                string oraType = preferredType.ToUpper() switch {
                    "SP" or "PROCEDURE" => "PROCEDURE",
                    "FUNCTION"          => "FUNCTION",
                    "VIEW"              => "VIEW",
                    "TABLE"             => "TABLE",
                    _                   => preferredType.ToUpper()
                };
                using var cmd = new OracleCommand(
                    "SELECT OBJECT_TYPE FROM USER_OBJECTS WHERE OBJECT_NAME=:name AND OBJECT_TYPE=:t FETCH FIRST 1 ROW ONLY", conn);
                cmd.Parameters.Add("name", OracleDbType.Varchar2).Value = objectName.ToUpper();
                cmd.Parameters.Add("t", OracleDbType.Varchar2).Value = oraType;
                objectType = cmd.ExecuteScalar()?.ToString();
            }
            else
            {
                using var cmd = new OracleCommand(
                    "SELECT OBJECT_TYPE FROM USER_OBJECTS WHERE OBJECT_NAME=:name FETCH FIRST 1 ROW ONLY", conn);
                cmd.Parameters.Add("name", OracleDbType.Varchar2).Value = objectName.ToUpper();
                objectType = cmd.ExecuteScalar()?.ToString();
            }

            if (objectType == null) return new ObjectDefinitionResult { Found = false };

            string sqlText;
            if (objectType is "TABLE" or "VIEW")
            {
                using var cmd = new OracleCommand($"SELECT DBMS_METADATA.GET_DDL('{objectType}',:name) FROM DUAL", conn);
                cmd.Parameters.Add("name", OracleDbType.Varchar2).Value = objectName.ToUpper();
                sqlText = cmd.ExecuteScalar()?.ToString() ?? "";
            }
            else
            {
                using var cmd = new OracleCommand(
                    "SELECT TEXT FROM USER_SOURCE WHERE NAME=:name AND TYPE=:type ORDER BY LINE", conn);
                cmd.Parameters.Add("name", OracleDbType.Varchar2).Value = objectName.ToUpper();
                cmd.Parameters.Add("type", OracleDbType.Varchar2).Value = objectType;
                using var r = cmd.ExecuteReader();
                var sb = new System.Text.StringBuilder("CREATE OR REPLACE\n");
                while (r.Read()) sb.Append(r.GetString(0));
                sqlText = sb.ToString();
            }
            return new ObjectDefinitionResult { Found = true, ObjectType = objectType, SqlText = sqlText.Trim() };
        }

        // ── Env configs ───────────────────────────────────────────
        public Dictionary<string, EnvConfig> GetAllEnvConfigs()
        {
            var result = new Dictionary<string, EnvConfig>();
            foreach (var child in _config.GetSection("Environments").GetChildren())
                result[child.Key] = new EnvConfig
                {
                    Label          = child["Label"]          ?? child.Key,
                    Protocol       = child["Protocol"]       ?? "TCP",
                    Host           = child["Host"]           ?? "",
                    Port           = child["Port"]           ?? "1521",
                    ServiceName    = child["ServiceName"]    ?? "",
                    UserId         = child["UserId"]         ?? "",
                    Password       = child["Password"]       ?? "",
                    SqlNetOraPath  = child["SqlNetOraPath"]  ?? "",
                    WalletPath     = child["WalletPath"]     ?? "",
                    TrustStore     = child["TrustStore"]     ?? "",
                    KeyStore       = child["KeyStore"]       ?? "",
                    KeyStorePwd    = child["KeyStorePwd"]    ?? "",
                    ServerDN       = child["ServerDN"]       ?? ""
                };
            return result;
        }

        // ── Test a connection ─────────────────────────────────────
        public (bool ok, string error, long ms) TestConnection(string env)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                using var conn = new OracleConnection(BuildConnStr(env));
                conn.Open();
                using var cmd = new OracleCommand("SELECT 1 FROM DUAL", conn);
                cmd.ExecuteScalar();
                sw.Stop(); return (true, "", sw.ElapsedMilliseconds);
            }
            catch (Exception ex) { sw.Stop(); return (false, ex.Message, sw.ElapsedMilliseconds); }
        }
    }
}

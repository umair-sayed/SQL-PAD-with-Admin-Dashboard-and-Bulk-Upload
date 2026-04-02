namespace OracleSqlPortal.Models
{
    public class EnvConfig
    {
        public string Label        { get; set; } = "";
        // TCP | TCPS
        public string Protocol     { get; set; } = "TCP";
        // Structured connection fields
        public string Host         { get; set; } = "";
        public string Port         { get; set; } = "1521";
        public string ServiceName  { get; set; } = "";
        public string UserId       { get; set; } = "";
        public string Password     { get; set; } = "";
        // TCPS / SSL fields
        public string SqlNetOraPath { get; set; } = "";   // TNS_ADMIN / sqlnet.ora directory
        public string WalletPath   { get; set; } = "";
        public string TrustStore   { get; set; } = "";
        public string KeyStore     { get; set; } = "";
        public string KeyStorePwd  { get; set; } = "";
        public string ServerDN     { get; set; } = "";
    }
}

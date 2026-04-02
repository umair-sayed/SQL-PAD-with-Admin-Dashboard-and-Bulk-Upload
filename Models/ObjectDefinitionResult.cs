namespace OracleSqlPortal.Models
{
    public class ObjectDefinitionResult
    {
        public bool Found { get; set; }
        public string ObjectType { get; set; } = "";
        public string SqlText { get; set; } = "";
    }
}

using OracleSqlPortal.Models;

namespace OracleSqlPortal.Services
{
    public class QueryHistoryService
    {
        private readonly PortalDbService _db;
        public QueryHistoryService(PortalDbService db) => _db = db;

        public void Add(QueryHistory h)                              => _db.RecordQuery(h);
        public List<QueryHistory> GetAll()                           => _db.GetQueryHistory(500);
        public List<QueryHistory> GetRecent(int n)                   => _db.GetQueryHistory(n);
        public List<QueryHistory> GetForUser(string username, int n = 200) => _db.GetQueryHistoryForUser(username, n);
    }
}

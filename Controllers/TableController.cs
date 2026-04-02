using Microsoft.AspNetCore.Mvc;
using OracleSqlPortal.Services;

namespace OracleSqlPortal.Controllers
{
    public class TableController : Controller
    {
        private readonly OracleService _service;

        public TableController(OracleService service)
        {
            _service = service;
        }

        //public IActionResult Index()
        //{
        //    var tables = _service.GetTables();
        //    return View(tables);
        //}

        //public IActionResult Data(string table, int page = 1)
        //{
        //    var data = _service.GetTableData(table, page, 50);

        //    ViewBag.Table = table;
        //    ViewBag.Page = page;

        //    return View(data);
        //}
    }
}
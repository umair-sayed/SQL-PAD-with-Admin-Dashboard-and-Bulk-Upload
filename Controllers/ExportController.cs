using ClosedXML.Excel;
using Microsoft.AspNetCore.Mvc;
using OracleSqlPortal.Services;
using System.Data;
using System.Text;

namespace OracleSqlPortal.Controllers
{
    public class ExportController : Controller
    {
        private readonly OracleService _service;

        private string? CurrentUser => HttpContext.Session.GetString("username");

        public ExportController(OracleService service)
        {
            _service = service;
        }

        // ── GET /Export/Excel ───────────────────────────────────────
        [HttpGet]
        public IActionResult Excel(string sql, string env = "DEV")
        {
            if (CurrentUser == null)
                return RedirectToAction("Login", "Auth");

            if (string.IsNullOrWhiteSpace(sql))
                return BadRequest("No SQL provided.");

            try
            {
                sql = (sql ?? "").Trim().TrimEnd(';');
                var (dt, _) = _service.ExecuteQuery(sql, env);

                using var wb = new XLWorkbook();
                var ws = wb.Worksheets.Add("Results");

                // Global font
                ws.Style.Font.FontName = "Segoe UI";
                ws.Style.Font.FontSize = 11;

                // ================= HEADER =================
                for (int c = 0; c < dt.Columns.Count; c++)
                {
                    var cell = ws.Cell(1, c + 1);
                    cell.SetValue(dt.Columns[c].ColumnName);

                    cell.Style.Font.Bold = true;
                    cell.Style.Font.FontColor = XLColor.White;
                    cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#111827");

                    cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                    cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;

                    cell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                    cell.Style.Border.OutsideBorderColor = XLColor.FromHtml("#374151");
                }

                // ================= DATA =================
                for (int r = 0; r < dt.Rows.Count; r++)
                {
                    for (int c = 0; c < dt.Columns.Count; c++)
                    {
                        var val = dt.Rows[r][c];
                        var cell = ws.Cell(r + 2, c + 1);

                        SetCellValue(cell, val);

                        // Zebra striping
                        cell.Style.Fill.BackgroundColor =
                            (r % 2 == 0)
                            ? XLColor.FromHtml("#f9fafb")
                            : XLColor.White;

                        // Borders
                        cell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                        cell.Style.Border.OutsideBorderColor = XLColor.FromHtml("#e5e7eb");

                        cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
                    }
                }

                // ================= TABLE =================
                if (dt.Rows.Count > 0 && dt.Columns.Count > 0)
                {
                    var range = ws.Range(1, 1, dt.Rows.Count + 1, dt.Columns.Count);
                    var table = range.CreateTable();

                    table.Theme = XLTableTheme.TableStyleMedium2;
                    table.ShowAutoFilter = true;
                }

                // ================= LAYOUT =================
                ws.ColumnsUsed().AdjustToContents();
                ws.SheetView.FreezeRows(1);

                using var stream = new MemoryStream();
                wb.SaveAs(stream);
                stream.Position = 0;

                string fileName = $"query_result_{env}_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";

                return File(
                    stream.ToArray(),
                    "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                    fileName
                );
            }
            catch (Exception ex)
            {
                return Content($"Export failed: {ex.Message}");
            }
        }

        // ── HELPER METHOD (FIXES XLCellValue ISSUE CLEANLY) ─────────
        private void SetCellValue(IXLCell cell, object val)
        {
            if (val == DBNull.Value || val == null)
            {
                cell.SetValue(string.Empty);
                return;
            }

            switch (val)
            {
                case string s:
                    cell.SetValue(s);
                    break;

                case int i:
                    cell.SetValue(i);
                    break;

                case long l:
                    cell.SetValue(l);
                    break;

                case decimal dec:
                    cell.SetValue(dec);
                    cell.Style.NumberFormat.Format = "#,##0.00";
                    cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
                    break;

                case double d:
                    cell.SetValue(d);
                    cell.Style.NumberFormat.Format = "#,##0.00";
                    cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
                    break;

                case float f:
                    cell.SetValue(f);
                    break;

                case DateTime dt:
                    cell.SetValue(dt);
                    cell.Style.DateFormat.Format = "dd-MMM-yyyy HH:mm";
                    break;

                case bool b:
                    cell.SetValue(b);
                    break;

                default:
                    cell.SetValue(val.ToString());
                    break;
            }
        }

        // ── CSV FALLBACK ────────────────────────────────────────────
        [HttpPost]
        public IActionResult Export(string sql, string env = "DEV")
        {
            if (CurrentUser == null)
                return RedirectToAction("Login", "Auth");

            var (dt, _) = _service.ExecuteQuery(sql, env);

            var sb = new StringBuilder();

            // Header
            sb.AppendLine(string.Join(",", dt.Columns
                .Cast<DataColumn>()
                .Select(c => $"\"{c.ColumnName}\"")));

            // Rows
            foreach (DataRow row in dt.Rows)
            {
                sb.AppendLine(string.Join(",", row.ItemArray
                    .Select(v => $"\"{(v == DBNull.Value ? "" : v?.ToString()?.Replace("\"", "\"\""))}\"")));
            }

            return File(
                Encoding.UTF8.GetBytes(sb.ToString()),
                "text/csv",
                $"query_result_{env}_{DateTime.Now:yyyyMMdd_HHmmss}.csv"
            );
        }
    }
}
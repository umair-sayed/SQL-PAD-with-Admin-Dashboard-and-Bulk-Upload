using Microsoft.AspNetCore.Mvc;
using OracleSqlPortal.Models;
using OracleSqlPortal.Services;

namespace OracleSqlPortal.Controllers
{
    public class FeedbackController : Controller
    {
        private readonly PortalDbService _db;
        public FeedbackController(PortalDbService db) => _db = db;

        private string? CurrentUser => HttpContext.Session.GetString("username");
        private string DisplayName => HttpContext.Session.GetString("displayname") ?? CurrentUser ?? "";

        public IActionResult Index()
        {
            if (CurrentUser == null) return RedirectToAction("Login", "Auth");
            ViewBag.MyFeedbacks = _db.GetUserFeedback(CurrentUser);
            return View();
        }

        [HttpPost]
        public IActionResult Submit(string subject, string message)
        {
            if (CurrentUser == null) return RedirectToAction("Login", "Auth");
            _db.AddFeedback(new UserFeedback
            {
                Username = CurrentUser,
                DisplayName = DisplayName,
                Subject = subject,
                Message = message
            });
            TempData["Success"] = "Feedback submitted successfully. Thank you!";
            return RedirectToAction("Index");
        }

        // ── Popup: AJAX acknowledge ────────────────────────────────
        [HttpPost]
        public IActionResult AcknowledgePopup(long popupId)
        {
            if (CurrentUser == null) return Json(new { ok = false });
            _db.AcknowledgePopup(CurrentUser, popupId);
            return Json(new { ok = true });
        }

        // ── Popup: get unseen popups for current user (called on login) ──
        [HttpGet]
        public IActionResult GetUnseenPopups()
        {
            if (CurrentUser == null) return Json(new List<object>());
            var popups = _db.GetUnseenPopups(CurrentUser);
            return Json(popups.Select(p => new { p.Id, p.Title, p.Body }));
        }

        // ── Notifications inbox ────────────────────────────────────
        public IActionResult Notifications()
        {
            if (CurrentUser == null) return RedirectToAction("Login", "Auth");
            ViewBag.AcknowledgedPopups = _db.GetAcknowledgedPopups(CurrentUser);
            return View();
        }
    }
}

namespace OracleSqlPortal.Models
{
    // ─── Feedback ────────────────────────────────────────────────
    public class UserFeedback
    {
        public long Id { get; set; }
        public string Username { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public string Subject { get; set; } = "";
        public string Message { get; set; } = "";
        public string Status { get; set; } = "Open";
        public string? AdminReply { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public DateTime? RepliedAt { get; set; }
    }

    // ─── Popup Notification ──────────────────────────────────────
    public class PopupNotification
    {
        public long Id { get; set; }
        public string Title { get; set; } = "";
        public string Body { get; set; } = "";
        public bool IsActive { get; set; } = true;
        public string CreatedBy { get; set; } = "";
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public DateTime? ExpiresAt { get; set; }
    }

    // ─── App Version ─────────────────────────────────────────────
    public class AppVersion
    {
        public string VersionNumber { get; set; } = "1.0.0";
        public DateTime VersionDate { get; set; } = DateTime.Now;
        public DateTime ExpiryDate { get; set; } = DateTime.Now.AddYears(1);
        public bool IsExpired => DateTime.Now > ExpiryDate;
    }
}

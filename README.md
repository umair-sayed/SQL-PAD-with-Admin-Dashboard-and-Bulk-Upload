# Oracle SQL Portal v10 — User Manual

---

## Table of Contents
1. [Getting Started](#getting-started)
2. [SQL Editor](#sql-editor)
3. [View Object](#view-object)
4. [Execute Stored Procedures](#execute-stored-procedures)
5. [Feedback](#feedback)
6. [Notifications & Popups](#notifications--popups)
7. [Migration](#migration)
8. [Admin Console](#admin-console)
9. [Version & Expiry Management](#version--expiry-management)
10. [External Apps (Hamburger Menu)](#external-apps-hamburger-menu)
11. [Troubleshooting](#troubleshooting)

---

## Getting Started

### Login
1. Open the portal URL in your browser.
2. Enter your **Username** and **Password** then click **Login**.
3. If you don't have an account, click **Sign Up** and wait for admin approval.
4. First-time admin login: username `admin`, password `Admin` (change immediately in Admin → Users).

### Navigation Bar (top)
| Link | Who Sees It | Purpose |
|------|-------------|---------|
| SQL Editor | All users | Run queries against Oracle environments |
| View Object | Users with VIEW_SP permission | Browse table/SP/function definitions |
| Migration | All logged-in users | Upload and execute SQL migration scripts |
| Feedback | All logged-in users | Submit feedback / suggestions |
| Notifications (🔔) | All logged-in users | Read announcements from admin |
| Admin | Admin users only | Full admin console |
| Request Access | Non-admin users | Request additional operation permissions |
| 🔲 (Grid) | All logged-in users | External apps launcher (if configured) |

---

## SQL Editor

### Running a Query
1. Select your **Environment** from the dropdown (SIT, UAT, DEV, PROD, etc.).
2. Your permitted operations are shown as coloured chips next to the environment.
3. Type your SQL in the editor pane.
4. Press **Ctrl+Enter** or click **Run**.

### Multi-Tab Sessions
- Click **New Tab** (top-right) to open a fresh editor tab. Each tab keeps its own SQL text and selected environment.
- Click **Duplicate** to clone the current tab's SQL into a new tab — useful for variations on the same query.
- Tabs are saved in browser session storage, so they survive page refreshes.
- Close a tab with the ✕ on the tab label (cannot close the last tab).

### Commit / Rollback Buttons
- The **Commit** and **Rollback** buttons are enabled **only** when the detected operation is INSERT, UPDATE, DELETE, or TRUNCATE.
- For all other statement types (SELECT, CREATE, etc.) these buttons stay disabled.
- Click **Commit** to execute the DML and commit, or **Rollback** to execute and roll back (useful for testing row counts without persisting).

### Multi-Statement DML (Feature #11)
You can paste multiple INSERT/UPDATE/DELETE statements separated by semicolons:
```sql
INSERT INTO orders(id, status) VALUES(1, 'NEW');
INSERT INTO orders(id, status) VALUES(2, 'PENDING');
UPDATE orders SET status='ACTIVE' WHERE id=1;
```
All statements run in sequence. The result shows total rows affected and total time.

> **Note:** CREATE, DECLARE, and BEGIN blocks are treated as a **single** statement and are not split on semicolons.

### CREATE Commands — Semicolons Preserved
When your statement starts with `CREATE`, `DECLARE`, or `BEGIN`, trailing semicolons and internal semicolons are **not trimmed**. This ensures stored procedures, packages, and triggers compile correctly.

### TRUNCATE Operation (Feature #8)
TRUNCATE is now a supported operation. Users need the `TRUNCATE` permission granted on the relevant environment to run TRUNCATE TABLE statements.

### Large Result Sets (Feature #4)
- Results are loaded with an optimised fetch buffer (10 MB) to avoid UI freezing.
- Tables with more than **200 rows** are automatically paginated client-side. Use the **First / Prev / Next / Last** buttons at the bottom of the result panel.
- Use the **Filter results…** box above the table to search across all visible cells.

### Export to Excel
Click **Export** in the result header to download the current query result as an Excel file.

---

## View Object

### Searching Objects (Feature #7)
1. Select your **Environment**.
2. Choose **Object Type** from the dropdown:
   - All Types, Table, View, Stored Procedure, Function
3. Type a partial name in **Object Name** — the search uses `LIKE %pattern%`.
4. Click **Search**. A dropdown appears with all matching objects.
5. Select the desired object from the dropdown, then click **Lookup Object** to view its definition.

> **Tip:** If exactly one object matches, it is auto-filled into the name field.

### Viewing the Definition
The full DDL / source code is displayed in a syntax-highlighted pane. Use the **Copy** button to copy it to clipboard.

### Executing SPs / Functions (Feature #1 & #2)
1. After looking up a Stored Procedure or Function, click **Load Parameters**.
2. Input fields appear for each IN / INOUT parameter. OUT parameters show as read-only.
3. Fill in the input values, then click **Execute**.
4. Output parameter values are shown below.

---

## Feedback

### Submitting Feedback (Feature #3)
1. Click **Feedback** in the navbar.
2. Enter a **Subject** and a **Message**.
3. Click **Submit**. The feedback is stored and visible to admins.

### My Feedback History
The right panel shows all your previously submitted feedback along with any admin replies and the current status (Open / In Progress / Closed).

---

## Notifications & Popups (Feature #10)

### Popup on Login
After logging in, if the admin has posted a new notification that you haven't read yet, a **modal popup** appears automatically. Click **Mark as Read** to acknowledge it. If multiple popups are pending, they appear one after another.

### Notifications Inbox
Click the **🔔 Notifications** link in the navbar to see all popups you have previously acknowledged. This is your personal notification history.

---

## Migration

1. Click **Migration** in the navbar.
2. Select the **Environment** and upload your SQL script file.
3. Review the parsed statements, then click **Execute Migration**.
4. Results show each statement's success/failure status.

> Migration access must be explicitly granted by an admin.

---

## Admin Console

Access via **Admin** in the navbar. All sections are tabs at the top.

### Approvals
Review and approve/reject new user signup requests.

### Access Requests
Approve or reject user requests for specific operation permissions (SELECT, INSERT, EXECUTE, VIEW_SP, MIGRATION, TRUNCATE, etc.).

### Users
- **Add User**: create a new user with direct approval.
- **Edit** each user's password, display name, email, and per-environment permissions.
- Toggle **Migration Access** with a dedicated switch.

### Connections
Add, edit, or delete Oracle environment connections. Changes are saved to `appsettings.json` live — no restart needed.

### Portal DB
View and update the portal's own Oracle database connection settings.

### Feedback Management (Feature #3, Admin)
Under the **Feedback** tab:
- View all submitted feedback with subject, message, user, and timestamp.
- Write a reply in the text area and update the status (Open / In Progress / Closed).
- Click **Reply** to save.

### Popup Management (Feature #10, Admin)
Under the **Popup Mgmt** tab:
- **Create Notification**: enter a Title, Body, and optional Expiry Date, then click **Post Notification**. All logged-in users will see this popup the next time they load a page (until they mark it as read).
- **Manage existing** popups: Activate or Deactivate any notification from the list.

### Query History / Login History / Migration Log / Activity History / Logs
Tabular audit logs. All are read-only.

---

## Version & Expiry Management (Feature #13)

### How It Works
- The table `APP_VERSION` holds the version number, version date, and an expiry date.
- The portal reads this on every page load.
- If `SYSDATE > EXPIRY_DATE`, all query execution is **blocked** with a message:
  > "QueryPad version X.X.X has expired on DD-MMM-YYYY. A new build is to be updated in QueryPad, kindly contact admin."
- The version number and date appear in the **footer** of every page.

### Updating the Version (Admin/DBA Task)
Run the following in SQL*Plus or SQL Developer against the PortalDB schema:

```sql
-- Update existing version row
UPDATE APP_VERSION
   SET VERSION_NUMBER = '1.1.0',
       VERSION_DATE   = SYSDATE,
       EXPIRY_DATE    = ADD_MONTHS(SYSDATE, 12)
 WHERE ID = (SELECT MAX(ID) FROM APP_VERSION);
COMMIT;

-- OR insert a new version record
INSERT INTO APP_VERSION(VERSION_NUMBER, VERSION_DATE, EXPIRY_DATE)
VALUES('2.0.0', SYSDATE, ADD_MONTHS(SYSDATE, 12));
COMMIT;
```

---

## External Apps (Hamburger Menu) (Feature #12)

### Configuration
Add entries under `ExternalApps` in `appsettings.json`:

```json
"ExternalApps": [
  { "Name": "SBI Portal",      "Url": "https://sbi.co.in" },
  { "Name": "Confluence Docs", "Url": "https://confluence.example.com" },
  { "Name": "Jira Board",      "Url": "https://jira.example.com/board" }
]
```

### Usage
- The **🔲 grid icon** appears in the top-right of the navbar when at least one app is configured.
- Click it to open a dropdown listing all configured external apps.
- Click any app name to open it in a new browser tab.

---

## Troubleshooting

| Symptom | Likely Cause | Fix |
|---------|-------------|-----|
| "Access Denied — TRUNCATE not permitted" | User doesn't have TRUNCATE permission | Admin → Users → grant TRUNCATE for the environment |
| "QueryPad Expired" banner | APP_VERSION.EXPIRY_DATE < SYSDATE | Update APP_VERSION table (see above) |
| CREATE procedure fails | Missing semicolons being trimmed | Ensure statement starts with CREATE — v10 preserves semicolons for CREATE blocks |
| Popup not showing | Already acknowledged, or no active popups | Admin → Popup Mgmt → check IS_ACTIVE=1 and EXPIRES_AT is future |
| Version shows "Loading…" in footer | `/api/version` endpoint unreachable or APP_VERSION table missing | Run `portal_v10_schema.sql` migration |
| External apps icon missing | ExternalApps section missing or empty in appsettings.json | Add at least one entry (see above) |
| Multi-statement DML only executes first | Statements not separated by semicolons | Separate each DML statement with `;` on its own line |
| Large query freezes browser | Very large result set (100K+ rows) | Add `WHERE ROWNUM <= 10000` or use a more selective filter |
| SP Execute shows no parameters | SP not in USER_ARGUMENTS or wrong environment | Verify SP name and environment; check USER_ARGUMENTS with: `SELECT * FROM USER_ARGUMENTS WHERE OBJECT_NAME='SP_NAME'` |

---

*Oracle SQL Portal v10 — Built on ASP.NET Core 8 + Oracle Managed Data Access*

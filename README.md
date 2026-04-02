# 🚀 Oracle SQL Portal (v9)

A secure, web-based SQL management platform for Oracle environments — designed to provide safe, controlled access to databases without sharing credentials or installing heavy tools.

---

## ✨ Features

- Execute SQL queries across multiple Oracle environments  
- Upload and run migration scripts with transactional support (commit/rollback)  
- View database objects (tables, views, etc.)  
- Fine-grained role-based access control (RBAC)  
- Access request and approval workflow  
- Admin dashboard for managing users, permissions, and environments  
- Full audit logging (logins, queries, admin actions)  
- Export query results to Excel (.xlsx)  

---

## 📸 Screenshots  

### 🔐 Login  
<img width="444" height="580" alt="Login" src="https://github.com/user-attachments/assets/23fb627a-f06e-47e5-a22d-c719a7bc9fc6" />

---

### 💻 SQL Editor  
<img width="1559" height="691" alt="SQL Editor" src="https://github.com/user-attachments/assets/5e82e036-8db8-4694-832e-3bd9fe49f47c" />

---

### 📂 View Database Objects  
<img width="1550" height="367" alt="View Object" src="https://github.com/user-attachments/assets/27af7d68-69eb-490f-ba6b-fba615c5414b" />

---

### 📤 Migration via File Upload  
<img width="1570" height="542" alt="Migration Upload" src="https://github.com/user-attachments/assets/9226ffcc-e109-4009-9bb6-f08c242865e8" />

---

### ⚙️ Admin Console  
<img width="1562" height="676" alt="Admin Console" src="https://github.com/user-attachments/assets/72d81df8-7789-4c68-92ed-e75168d9d98b" />

---

## 🧱 Architecture Overview

Oracle SQL Portal is built as a full-stack web application:

- **Frontend**: Razor Pages + Bootstrap 5 + Vanilla JavaScript  
- **Backend**: ASP.NET Core 8 (.NET 8)  
- **Database**: Oracle (12c+)  
- **Driver**: Oracle Managed Data Access (ODP.NET)  

---

## ⚙️ Tech Stack

- ASP.NET Core 8 (MVC + Razor Pages)  
- C# 12  
- Oracle Database  
- Oracle.ManagedDataAccess (ODP.NET Managed Driver)  
- Bootstrap 5  
- Vanilla JavaScript (ES6+)  
- CodeMirror (SQL Editor)  
- ClosedXML (Excel Export)  

---

## 🔐 Security & Access Control

- Per-user, per-environment, per-operation permissions  
- SQL intent detection before execution  
- Admin-controlled access grants  
- Migration access toggle per user  
- Complete audit trail stored in Oracle  

---

## 📦 Key Highlights

- Custom SQL*Plus script parser (supports SET, PROMPT, DEFINE, etc.)  
- Fully transactional migrations with rollback support  
- Zero-downtime configuration updates using live reload  
- Auto schema provisioning using idempotent PL/SQL blocks  

---

## ⚡ Getting Started

### Prerequisites

- .NET 8 SDK  
- Oracle Database (12c or higher)  
- IIS (Windows) or Linux server (optional for deployment)  

---

### 🔧 Setup

1. Clone the repository:
```bash
git clone https://github.com/umair-sayed/SQL-PAD-with-Admin-Dashboard-and-Bulk-Upload.git
cd SQL-PAD-with-Admin-Dashboard-and-Bulk-Upload

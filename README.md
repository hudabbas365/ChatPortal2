# AI Insights 365

AI-Powered Data Conversations Portal

## Features
- JWT Authentication with role-based access
- Chat workspace with Cohere AI agent integration
- Full charting/dashboard module (72+ chart types)
- Iframe embeddable chat widget
- Soft blue modern design theme
- Multi-tenant organization support

## Tech Stack
- ASP.NET Core 8 MVC + Web API
- Bootstrap 5.3, jQuery 3.7, ApexCharts 3.54
- Entity Framework Core with SQL Server
- JWT Bearer Authentication

## Getting Started
```bash
dotnet restore
dotnet run
```

## Super Admin: Backups, Announcements, SEO, Images

- **Blog / Documentation images**: Super Admin Blog and Documentation pages now support a featured image plus gallery/content images with server-side validation and public rendering on detail pages.
- **SEO keywords**: Super Admin Blog includes a keyword chip editor and a **Suggest keywords from content** action that generates 15+ content-based keyword candidates and updates public blog metadata.
- **Feature announcements**: Blog posts can be marked as feature announcements, targeted to subscription tiers or all subscribers, and queued for background email delivery with resend-failed support.
- **Organization backup & restore**: Visit **Super Admin → Backup & Restore** (`/superadmin/organizations/backup-restore`) to export ZIP/JSON backups, review backup history, and restore in Merge or Replace mode.

## AI Insight ETL Transform Layer (TOML)

AI Insights now supports a datasource-bound **Transform** step in the flow:

`Datasource -> Transform -> Dashboard -> Report`

- Transform rules are stored per datasource (`TransformEnabled`, `TransformToml`).
- Rules are authored in TOML and applied automatically to query results before dashboards/reports consume them.
- Supported categories include:
  - data cleansing (`remove_duplicates`, `handle_nulls`, `standardize_format`)
  - normalization (`convert_units`, `apply_naming_convention`, `map_codes`)
  - business logic (`kpi_classification`, `sentiment_score`, `derived_field` with DAX-like `IF(...)` / `DATEDIFF(...)`)
  - aggregation/restructuring (`aggregate`, `flatten_json`)
  - validation/audit (`validate_schema`, `referential_integrity`, transform audit trail)

Example:

```toml
[transform]
name = "Support ETL"
enabled = true
dax_like_expressions = true

[[rules]]
type = "remove_duplicates"
keys = ["TicketId"]

[[rules]]
type = "derived_field"
target_field = "ResolutionMinutes"
expression = "DATEDIFF(minute, [IncidentDateTime], [CloseDateTime])"
```

## Subscription Gate (Read-Only Mode)

When an organization's subscription ends (after the `SubscriptionNextBillingDate` passes following a cancellation), the `SubscriptionExpiryJob` sets `Plan = Free` and `SubscriptionStatus = EXPIRED`. At that point a server-side subscription gate activates for regular users (`Role = "User"`):

- **OrgAdmin** and **SuperAdmin** retain full access so they can re-subscribe and manage users.
- **Regular users** get a read-only experience: all `GET` endpoints remain accessible, but `POST`, `PUT`, and `DELETE` requests to workspace, dashboard, report, agent, datasource, and chat endpoints return HTTP 403 with:
  ```json
  { "error": "...", "code": "subscription_expired", "status": "EXPIRED", "plan": "Free" }
  ```
- The UI displays a dismissible banner and toast notifications explaining the restriction, with a different message for OrgAdmins (directing them to Settings → Billing).

An org is considered gated when **any** of these is true:
- `org.IsBlocked == true`
- `org.SubscriptionStatus == "EXPIRED"`
- `org.Plan == PlanType.Free` and `org.SubscriptionStatus != "APPROVAL_PENDING"`

Implemented via `Filters/RequireActiveSubscriptionAttribute.cs` applied to write endpoints in `WorkspaceController`, `DashboardController`, `ReportController`, `AgentController`, `DatasourceController`, `ChatController`, and `AutoReportController`.



**⚠️ Never commit real secrets to this repository.**

All sensitive configuration values must be provided via environment variables or `dotnet user-secrets`. The `appsettings.json` file contains only placeholder values.

### Required Configuration Keys

| Key | Description |
|-----|-------------|
| `Jwt:Key` | JWT signing key — must be at least 32 characters |
| `Cohere:ApiKey` | Cohere AI API key from [dashboard.cohere.com](https://dashboard.cohere.com/) |
| `ConnectionStrings:DefaultConnection` | SQL Server connection string |

### Setting Secrets with `dotnet user-secrets`

```bash
dotnet user-secrets set "Jwt:Key" "your-random-32-char-secret-key-here"
dotnet user-secrets set "Cohere:ApiKey" "your-cohere-api-key"
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Server=...;Database=ChatPortal2;..."
```

### Setting Secrets via Environment Variables

```bash
export Jwt__Key="your-random-32-char-secret-key-here"
export Cohere__ApiKey="your-cohere-api-key"
```

> Note: Use double underscores (`__`) as the hierarchy separator in environment variable names.

### ChatPortal2.SuperAdmin Configuration

The SuperAdmin project requires its own JWT key:

```bash
cd ChatPortal2.SuperAdmin
dotnet user-secrets set "Jwt:Key" "your-superadmin-jwt-secret-key-here"
```

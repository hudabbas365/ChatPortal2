# AIInsights

AI-Powered Data Conversations Portal with integrated charting (migrated from ManageCharts).

## Features
- JWT Authentication with role-based access
- Chat workspace with Cohere AI agent integration
- Full charting/dashboard module (72+ chart types)
- Iframe embeddable chat widget
- Soft blue modern design theme
- Multi-tenant organization support

## Tech Stack
- ASP.NET Core 8 MVC + Web API
- Bootstrap 5.3, jQuery 3.7, Chart.js 4.4
- Entity Framework Core with SQL Server
- JWT Bearer Authentication

## Getting Started
```bash
dotnet restore
dotnet run
```

## Configuration

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

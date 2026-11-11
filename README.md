# Smart Asset Tracking

A menu-driven **C# / .NET 10 console application** for tracking company assets (laptops, desktops, phones, tablets and office equipment) across global offices. It uses **Entity Framework Core 10** with **SQL Server Express**.

The full-screen console UI is built with [Spectre.Console]: arrow-key menus, coloured tables, panels and bar charts.

```
▣ Smart Asset Tracking                                   Logged in as admin  Admin
── COMPANY ASSETS ─────────────────────────────────────────────────────────────────
╭────┬────────────────┬────────┬─────────────┬───────────────┬────────────┬──────────────┬────────────┬─────────╮
│ ID │ Type           │ Brand  │ Model       │ Office        │ Purchased  │  Local price │  Remaining │ Status  │
│    │ Computers      │        │             │               │            │              │            │         │
│  1 │ Laptop         │ Lenovo │ ThinkPad X1 │ Sweden Office │ 2023-08-05 │ 16 500,00 kr │     1m 21d │ YELLOW  │
│  7 │ Laptop         │ Asus   │ ZenBook 14  │ USA Office    │ 2023-10-15 │    $1,200.00 │      4m 0d │  RED    │
│ 12 │ Laptop         │ Lenovo │ Legion 5    │ Germany Office│ 2024-12-15 │   1.305,00 € │      1y 6m │ NORMAL  │
│    │ Mobile Devices │        │             │               │            │              │            │         │
│ 13 │ Mobile Phone   │ Nokia  │ XR20        │ Germany Office│ 2023-07-26 │     449,10 € │     1m 11d │ YELLOW  │
╰────┴────────────────┴────────┴─────────────┴───────────────┴────────────┴──────────────┴────────────┴─────────╯
```

## Getting started

### Requirements
* [.NET 10 SDK](https://dotnet.microsoft.com/download)
* SQL Server Express. The default instance name is `.\SQLEXPRESS`, but any SQL Server works if you change the connection string.

### Run

```powershell
git clone https://github.com/Lundqwist/Smart-Asset-Tracking.git
cd Smart-Asset-Tracking
dotnet run --project src/SmartAssetTracking
```

The first time the app starts, it does three things automatically:
1. Creates the `SmartAssetTracking` database by applying the EF Core migrations.
2. Adds the four offices (Sweden, USA, Germany, Turkey) through migration seed data.
3. Seeds demo employees, users and 20 assets. Their purchase dates are relative to today, so every lifecycle status is always shown.

Run it in a real terminal, such as Windows Terminal or the VS Code terminal. A window of 160 or more columns gives the best layout.

### Demo logins

| Username   | Password | Role     | Can do |
|------------|----------|----------|--------|
| `admin`    | `1234`   | Admin    | Everything, including user and office management |
| `manager`  | `1234`   | Manager  | Asset CRUD, assignments, maintenance, reports, exports |
| `employee` | `1234`   | Employee | View and search assets, and see "My assets" (linked to Sarah Johnson) |

Passwords are stored as salted PBKDF2-SHA256 hashes.

### Configuration – `src/SmartAssetTracking/appsettings.json`

| Section | Purpose |
|---|---|
| `ConnectionStrings:DefaultConnection` | SQL Server connection string |
| `Lifecycle` | Lifetime (3 years), YELLOW / RED thresholds in months, maintenance "due soon" window |
| `Currency` | Reporting currency, fixed USD exchange rates, symbols, formatting culture, live-rate API URL |
| `App` | Page size, export folder, log folder, maximum login attempts |

To override settings locally, create an `appsettings.Local.json` file (git-ignored) or set environment variables with the prefix `SMARTASSET_`. For example, `SMARTASSET_ConnectionStrings__DefaultConnection`.

### Migrations

```powershell
dotnet tool restore
cd src/SmartAssetTracking
dotnet ef migrations add <Name> -o Data/Migrations
dotnet ef database update
```

### Tests

```powershell
dotnet test                                   # unit and UI tests (EF Core InMemory)
$env:SMARTASSET_SQL_TESTS = "1"; dotnet test  # also runs the SQL Server integration test
```

The suite has 54 tests:
* Unit tests for lifecycle rules, currency conversion, validation, CRUD, search, reports, export, authentication and input parsing.
* Scripted UI tests that drive the real menus through `Spectre.Console.Testing`.
* An opt-in integration test that runs the migrations and every LINQ query against a temporary SQL Server database.

## Features by level

| Level | Requirement | Where |
|---|---|---|
| **1** | `Asset` base class → `ComputerAsset` (`Laptop`, `Desktop`) and `MobileAsset` (`MobilePhone` for iPhone/Samsung/Nokia, `Tablet`), plus `OfficeEquipment` | `Models/` |
| 1 | Create, save and list assets with EF Core (table-per-hierarchy with an `AssetType` discriminator) | `Data/AppDbContext.cs` |
| **2** | Full CRUD with validation, including confirmation before deleting | `Services/AssetService.cs`, `UI/Screens/AssetScreens.cs` |
| 2 | Sort by category, then purchase date. Colour-coded lifecycle status | `AssetService.SortByCategoryThenPurchaseDate`, `Services/LifecycleService.cs` |
| 2 | Menu-driven navigation and validated input (dates, numbers, empty fields) | `UI/MenuRunner.cs`, `Helpers/ConsoleInput.cs` |
| **3** | Offices with name, country and currency. Admins can add, edit and delete offices | `Models/Office.cs`, `Services/OfficeService.cs` |
| 3 | USD → local currency with correct symbols (`kr`, `€`, `₺`, `$`). Fixed rates, or live rates from an API (Admin) | `Services/CurrencyService.cs` |
| 3 | Reports: value and count per office, assets near expiration, most expensive assets | `Services/ReportService.cs`, `UI/Screens/ReportScreens.cs` |
| **4** | Search by brand, model, office or purchase year, or several criteria combined | `Repositories/AssetQuery.cs` |
| 4 | Filters: expired, expiring soon, computers, mobiles, office equipment, by office, unassigned | `AssetScreens.FilterAsync` |
| 4 | Export any list or report to **TXT, CSV or JSON** | `Reports/ExportService.cs` |
| 4 | Async EF Core, pagination, Serilog file logging (`logs/`), configuration files | throughout |
| **5** | Roles (Admin / Manager / Employee) with simulated login and role-based authorization in the services | `Services/AuthService.cs`, `Services/UserSession.cs` |
| 5 | Employees (ID, full name, department, email) with assigned assets | `Models/Employee.cs`, `UI/Screens/EmployeeScreens.cs` |
| 5 | Maintenance tracking: last date, next date, notes, and a schedule showing overdue and due-soon items | `UI/Screens/MaintenanceScreens.cs` |
| 5 | Dashboard: total assets, employees, expiring assets, company value, top asset type, most valuable office, assets per employee | `ReportScreens.DashboardAsync` |
| Bonus | Unit, UI and integration tests. Dependency injection | `tests/` |

### Lifecycle rules

Every asset's lifetime is **3 years** from its purchase date. The status thresholds follow the project specification exactly:

| Remaining lifetime | Status |
|---|---|
| less than 3 months | **YELLOW** |
| less than 6 months | **RED** |
| more than 6 months | NORMAL |
| end of life reached | **EXPIRED** (added for the "show only expired assets" filter) |

The specification makes the *closer* deadline YELLOW and the further one RED. The thresholds are configurable in `appsettings.json`, so you can swap them if you prefer RED for the most urgent assets.

## Project structure

```
src/SmartAssetTracking
├── Models/          Asset hierarchy, Office, Employee, User, enums
├── Data/            AppDbContext, migrations, demo-data seeder, design-time factory
├── Repositories/    EF Core data access (one short-lived DbContext per operation)
├── Services/        Business logic: assets, offices, employees, auth, lifecycle, currency, reports
├── Reports/         TXT / CSV / JSON export
├── Helpers/         Validated console input, password hashing, options, DI registration
├── UI/              Application shell, menu runner, tables and screens (Spectre.Console)
└── appsettings.json
tests/SmartAssetTracking.Tests   xUnit tests
```

[Spectre.Console]: https://spectreconsole.net/

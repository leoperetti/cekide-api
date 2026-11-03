# cekide-api

Backend (.NET 10) of the gym management app (`cekide-gym-web`): students, plans, routine PDFs and monthly / short-term payments.
Authentication is delegated to `authcraft-api`: this API only validates its JWT (tenant `cekide-app`; roles `owner`, `teacher`, `user`).

## Layout

```text
cekide-api.slnx
src/
  CekideApi/                  Web API: controllers, auth policies, CORS, error middleware, Program.cs
  CekideApi.Application/      use cases (PlanService, StudentService, RoutineService, PaymentService), DTOs, abstractions (IClock, IRoutineStorage, repositories)
  CekideApi.Domain/           entities and rules (Plan, Student, MonthlyPayment, PaymentRules, GymCalendar...)
  CekideApi.Infrastructure/   EF Core (SQL Server `cekide-db`, migrations), Azure Blob / in-memory routine storage, system clock
tests/
  CekideApi.UnitTests/        domain rules and middleware
  CekideApi.IntegrationTests/ WebApplicationFactory + in-memory SQLite, signed test JWTs
```

## Configuration (no secrets in the repo)

| Key | Purpose |
|-----|---------|
| `Jwt:Key`, `Jwt:Issuer`, `Jwt:Audience` | Same values as authcraft-api (required) |
| `Tenant:Id` | Required `tenant` claim, default `cekide-app` |
| `Cors:AllowedOrigins` | Allowed SPA origins (localhost is always allowed) |
| `CekideDatabaseConnectionString` | SQL Server database `cekide-db` (Development defaults to LocalDB) |
| `Storage:ConnectionString`, `Storage:RoutinesContainer` | Private Azure Blob container for routine PDFs (Development: Azurite) |
| `KeyVault:Name` | Optional; loaded in Production/Staging |
| `Brevo:ApiKey`, `Brevo:FromEmail`, `Brevo:FromName` | Brevo transactional email for overdue notices (key from user-secrets / Key Vault) |
| `Authcraft:BaseUrl` | Base URL of authcraft-api; student invitations are delegated there with the caller's bearer token |

Local secrets:

```powershell
dotnet user-secrets set "Jwt:Key" "<same key as authcraft-api>" --project src\CekideApi
dotnet user-secrets set "Jwt:Issuer" "<issuer>" --project src\CekideApi
dotnet user-secrets set "Jwt:Audience" "<audience>" --project src\CekideApi
dotnet user-secrets set "Brevo:ApiKey" "<brevo api key>" --project src\CekideApi
dotnet user-secrets set "Brevo:FromEmail" "<verified sender>" --project src\CekideApi
dotnet user-secrets set "Authcraft:BaseUrl" "<authcraft-api url>" --project src\CekideApi
```

## Run

```powershell
dotnet ef database update --project src\CekideApi.Infrastructure --startup-project src\CekideApi
dotnet run --project src\CekideApi          # http://localhost:5180
```

New migrations: `dotnet ef migrations add <Name> --project src\CekideApi.Infrastructure --startup-project src\CekideApi --output-dir Migrations`
(a test fails when the model and the snapshot differ).

## Test

```powershell
dotnet test
```

Development is test-first (Red -> Green -> Refactor).

## Business rules in short

- Payment window: days 1-10 of the month in `America/Argentina/Buenos_Aires`; unpaid months are overdue from day 11. The obligation is derived, never stored.
- No partial payments: the amount must equal the plan price (snapshot stored in the payment). One `Paid` monthly payment per student and month (409 otherwise).
- A student who starts the monthly membership after day 10 owes that month from that day and gets no overdue notice for it or earlier months.
- Short-term passes (`day`, `week`, `fifteen_days`) are prepaid and never overdue.
- Only `owner` modifies or voids payments (`PUT`/`DELETE /api/gym/payments/{id}`), every change is written to `payment_history`.
- JSON uses `snake_case` properties and lower snake_case enums. `kind=monthly|pass` (query) selects the id space for payment ids.
- Overdue notices: the hourly `OverdueNotificationJob` acts from day 11 (Buenos Aires); one Spanish email per student and month (unique `student_id`+`period`), `Failed` ones are retried on the next run, a payment stops them. Staff list/retry them at `/api/gym/notifications`.
- Invitations: `POST /api/gym/students/{id}/invitations[/resend]` delegate to authcraft-api and record `student_invitations`. When a `user` account first calls `GET /api/gym/me/payments`, it is linked to the active student with the same email (`identity_id` = JWT `sub`).

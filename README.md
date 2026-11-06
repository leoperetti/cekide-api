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

## Attendance, shifts, and recovery operations

See the [attendance operations guide](docs/attendance-operations.md) for
kiosk setup, shift capacity, recovery reservations, quota email handling,
invitation synchronization, and deployment order.

Key rules:

- Attendance check-in is one record per student and local gym date. It does
  not require a published shift or reservation, and it must not reject an
  arrival because a shift is full/cancelled or the student's weekly/monthly
  quota is exceeded. Planned capacity applies only to habitual occupancies
  and reservations; actual attendance may exceed twelve.
- Recovery eligibility is captured as a weekly snapshot and closed after the
  week ends. Credits are derived from that saved eligibility and origin-month
  balance; do not reconstruct past eligibility from today's plan or invent
  credits when a historical snapshot is missing.
- `RecoveryAccrualJob` runs every minute and logs run failures for retry on
  its next tick. There is currently no dedicated automatic alert for a missing
  weekly eligibility snapshot. Monitor the job's error logs and investigate
  gaps in closed-week history; do not backfill eligibility or credits without
  authoritative historical data.
- Quota emails are created only when an ordinary attendance reaches the
  consumed monthly limit. A `delivery_unknown` result requires staff
  reconciliation against provider evidence and is not resent automatically.

Apply database changes and deploy the matching APIs before enabling the
updated UI. Use the existing authcraft SQL deployment process for invitation
schema changes; do not run gym or authcraft migrations against production
without the environment's review and approval process.

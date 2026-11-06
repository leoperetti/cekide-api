using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CekideApi.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAttendanceAndKioskSessions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddUniqueConstraint(
                name: "AK_plan_attendance_limits_plan_id_id",
                table: "plan_attendance_limits",
                columns: new[] { "plan_id", "id" });

            migrationBuilder.CreateTable(
                name: "attendance_periods",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    student_id = table.Column<int>(type: "int", nullable: false),
                    period = table.Column<string>(type: "char(7)", nullable: false),
                    plan_id = table.Column<int>(type: "int", nullable: false),
                    plan_limits_id = table.Column<int>(type: "int", nullable: false),
                    plan_name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    weekly_limit = table.Column<int>(type: "int", nullable: false),
                    monthly_limit = table.Column<int>(type: "int", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_attendance_periods", x => x.id);
                    table.UniqueConstraint("AK_attendance_periods_student_id_id", x => new { x.student_id, x.id });
                    table.CheckConstraint("CK_attendance_periods_monthly_limit", "monthly_limit > 0");
                    table.CheckConstraint("CK_attendance_periods_weekly_limit", "weekly_limit BETWEEN 1 AND 7");
                    table.ForeignKey(
                        name: "FK_attendance_periods_plan_attendance_limits_plan_id_plan_limits_id",
                        columns: x => new { x.plan_id, x.plan_limits_id },
                        principalTable: "plan_attendance_limits",
                        principalColumns: new[] { "plan_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_attendance_periods_plans_plan_id",
                        column: x => x.plan_id,
                        principalTable: "plans",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_attendance_periods_students_student_id",
                        column: x => x.student_id,
                        principalTable: "students",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "kiosk_sessions",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    label = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    tenant = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    created_by = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    activation_code_hash = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    activation_expires_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    activated_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    token_hash = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    expires_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    revoked_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    revoked_by = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    row_version = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_kiosk_sessions", x => x.id);
                    table.CheckConstraint("CK_kiosk_sessions_activation_expiry", "activation_expires_at > created_at");
                    table.CheckConstraint("CK_kiosk_sessions_activation_state", "(activated_at IS NULL AND token_hash IS NULL AND expires_at IS NULL) OR (activated_at IS NOT NULL AND token_hash IS NOT NULL AND expires_at > activated_at)");
                });

            migrationBuilder.CreateTable(
                name: "attendance_quota_notifications",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    student_id = table.Column<int>(type: "int", nullable: false),
                    attendance_period_id = table.Column<int>(type: "int", nullable: false),
                    period = table.Column<string>(type: "char(7)", unicode: false, fixedLength: true, nullable: false),
                    recipient_email = table.Column<string>(type: "nvarchar(254)", maxLength: 254, nullable: false),
                    recipient_name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    plan_name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    monthly_limit = table.Column<int>(type: "int", nullable: false),
                    recommended_plan_id = table.Column<int>(type: "int", nullable: true),
                    recommended_plan_name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    recommended_monthly_days = table.Column<int>(type: "int", nullable: true),
                    recommended_price = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    attempts = table.Column<int>(type: "int", nullable: false),
                    next_attempt_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    claimed_until = table.Column<DateTime>(type: "datetime2", nullable: true),
                    claim_id = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    sent_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    last_error = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    resolved_by = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    resolved_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    resolution_evidence = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_attendance_quota_notifications", x => x.id);
                    table.CheckConstraint("CK_attendance_quota_notifications_attempts", "attempts >= 0");
                    table.CheckConstraint("CK_attendance_quota_notifications_monthly_limit", "monthly_limit > 0");
                    table.ForeignKey(
                        name: "FK_attendance_quota_notifications_attendance_periods_student_id_attendance_period_id",
                        columns: x => new { x.student_id, x.attendance_period_id },
                        principalTable: "attendance_periods",
                        principalColumns: new[] { "student_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_attendance_quota_notifications_students_student_id",
                        column: x => x.student_id,
                        principalTable: "students",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "attendances",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    student_id = table.Column<int>(type: "int", nullable: false),
                    attendance_period_id = table.Column<int>(type: "int", nullable: false),
                    local_date = table.Column<DateOnly>(type: "date", nullable: false),
                    checked_in_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    source = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    kiosk_session_id = table.Column<int>(type: "int", nullable: true),
                    weekly_exceeded = table.Column<bool>(type: "bit", nullable: false),
                    monthly_exceeded = table.Column<bool>(type: "bit", nullable: false),
                    monthly_exhausted = table.Column<bool>(type: "bit", nullable: false),
                    charge_kind = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_attendances", x => x.id);
                    table.CheckConstraint("CK_attendances_source_kiosk", "(source = 'kiosk' AND kiosk_session_id IS NOT NULL) OR (source = 'self' AND kiosk_session_id IS NULL)");
                    table.ForeignKey(
                        name: "FK_attendances_attendance_periods_student_id_attendance_period_id",
                        columns: x => new { x.student_id, x.attendance_period_id },
                        principalTable: "attendance_periods",
                        principalColumns: new[] { "student_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_attendances_kiosk_sessions_kiosk_session_id",
                        column: x => x.kiosk_session_id,
                        principalTable: "kiosk_sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_attendance_periods_plan_id_plan_limits_id",
                table: "attendance_periods",
                columns: new[] { "plan_id", "plan_limits_id" });

            migrationBuilder.CreateIndex(
                name: "IX_attendance_periods_student_id_period",
                table: "attendance_periods",
                columns: new[] { "student_id", "period" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_attendance_quota_notifications_status_next_attempt_at",
                table: "attendance_quota_notifications",
                columns: new[] { "status", "next_attempt_at" });

            migrationBuilder.CreateIndex(
                name: "IX_attendance_quota_notifications_student_id_attendance_period_id",
                table: "attendance_quota_notifications",
                columns: new[] { "student_id", "attendance_period_id" });

            migrationBuilder.CreateIndex(
                name: "IX_attendance_quota_notifications_student_id_period",
                table: "attendance_quota_notifications",
                columns: new[] { "student_id", "period" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_attendances_kiosk_session_id",
                table: "attendances",
                column: "kiosk_session_id");

            migrationBuilder.CreateIndex(
                name: "IX_attendances_student_id_attendance_period_id",
                table: "attendances",
                columns: new[] { "student_id", "attendance_period_id" });

            migrationBuilder.CreateIndex(
                name: "IX_attendances_student_id_local_date",
                table: "attendances",
                columns: new[] { "student_id", "local_date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_kiosk_sessions_activation_code_hash",
                table: "kiosk_sessions",
                column: "activation_code_hash",
                unique: true,
                filter: "activation_code_hash IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_kiosk_sessions_tenant_created_at",
                table: "kiosk_sessions",
                columns: new[] { "tenant", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_kiosk_sessions_token_hash",
                table: "kiosk_sessions",
                column: "token_hash",
                unique: true,
                filter: "token_hash IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "attendance_quota_notifications");

            migrationBuilder.DropTable(
                name: "attendances");

            migrationBuilder.DropTable(
                name: "attendance_periods");

            migrationBuilder.DropTable(
                name: "kiosk_sessions");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_plan_attendance_limits_plan_id_id",
                table: "plan_attendance_limits");
        }
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CekideApi.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddRecoveryCreditsAndReservations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "recovery_credit_id",
                table: "attendances",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "recovery_weeks",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    student_id = table.Column<int>(type: "int", nullable: false),
                    week_start = table.Column<DateOnly>(type: "date", nullable: false),
                    week_end_exclusive = table.Column<DateOnly>(type: "date", nullable: false),
                    origin_period = table.Column<string>(type: "char(7)", nullable: false),
                    attendance_period_id = table.Column<int>(type: "int", nullable: false),
                    plan_limits_id = table.Column<int>(type: "int", nullable: false),
                    weekly_target = table.Column<int>(type: "int", nullable: false),
                    eligibility = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    closed_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    attended_count = table.Column<int>(type: "int", nullable: true),
                    granted_count = table.Column<int>(type: "int", nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_recovery_weeks", x => x.id);
                    table.UniqueConstraint("AK_recovery_weeks_student_id_id", x => new { x.student_id, x.id });
                    table.CheckConstraint("CK_recovery_weeks_close_counts", "(closed_at IS NULL AND attended_count IS NULL AND granted_count IS NULL) OR (closed_at IS NOT NULL AND attended_count IS NOT NULL AND attended_count >= 0 AND granted_count IS NOT NULL AND granted_count BETWEEN 0 AND weekly_target)");
                    table.CheckConstraint("CK_recovery_weeks_eligibility", "eligibility IN ('eligible', 'excluded')");
                    table.CheckConstraint("CK_recovery_weeks_weekly_target", "weekly_target BETWEEN 1 AND 5");
                    table.ForeignKey(
                        name: "FK_recovery_weeks_attendance_periods_student_id_attendance_period_id",
                        columns: x => new { x.student_id, x.attendance_period_id },
                        principalTable: "attendance_periods",
                        principalColumns: new[] { "student_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_recovery_weeks_plan_attendance_limits_plan_limits_id",
                        column: x => x.plan_limits_id,
                        principalTable: "plan_attendance_limits",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_recovery_weeks_students_student_id",
                        column: x => x.student_id,
                        principalTable: "students",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "recovery_credits",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    recovery_week_id = table.Column<int>(type: "int", nullable: false),
                    student_id = table.Column<int>(type: "int", nullable: false),
                    ordinal = table.Column<int>(type: "int", nullable: false),
                    origin_period = table.Column<string>(type: "char(7)", nullable: false),
                    attendance_period_id = table.Column<int>(type: "int", nullable: false),
                    plan_limits_id = table.Column<int>(type: "int", nullable: false),
                    normal_start = table.Column<DateOnly>(type: "date", nullable: true),
                    normal_end_exclusive = table.Column<DateOnly>(type: "date", nullable: true),
                    exception_start = table.Column<DateOnly>(type: "date", nullable: true),
                    exception_end_exclusive = table.Column<DateOnly>(type: "date", nullable: true),
                    expires_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    used_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_recovery_credits", x => x.id);
                    table.UniqueConstraint("AK_recovery_credits_student_id_id", x => new { x.student_id, x.id });
                    table.CheckConstraint("CK_recovery_credits_exception_window", "(exception_start IS NULL AND exception_end_exclusive IS NULL) OR (exception_start IS NOT NULL AND exception_end_exclusive IS NOT NULL AND exception_start < exception_end_exclusive)");
                    table.CheckConstraint("CK_recovery_credits_normal_window", "(normal_start IS NULL AND normal_end_exclusive IS NULL) OR (normal_start IS NOT NULL AND normal_end_exclusive IS NOT NULL AND normal_start < normal_end_exclusive)");
                    table.CheckConstraint("CK_recovery_credits_ordinal", "ordinal > 0");
                    table.CheckConstraint("CK_recovery_credits_status", "status IN ('available', 'reserved', 'used', 'expired')");
                    table.CheckConstraint("CK_recovery_credits_used_at", "(status = 'used' AND used_at IS NOT NULL) OR (status <> 'used' AND used_at IS NULL)");
                    table.ForeignKey(
                        name: "FK_recovery_credits_attendance_periods_student_id_attendance_period_id",
                        columns: x => new { x.student_id, x.attendance_period_id },
                        principalTable: "attendance_periods",
                        principalColumns: new[] { "student_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_recovery_credits_plan_attendance_limits_plan_limits_id",
                        column: x => x.plan_limits_id,
                        principalTable: "plan_attendance_limits",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_recovery_credits_recovery_weeks_student_id_recovery_week_id",
                        columns: x => new { x.student_id, x.recovery_week_id },
                        principalTable: "recovery_weeks",
                        principalColumns: new[] { "student_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "recovery_reservations",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    student_id = table.Column<int>(type: "int", nullable: false),
                    recovery_credit_id = table.Column<int>(type: "int", nullable: false),
                    shift_id = table.Column<int>(type: "int", nullable: false),
                    local_date = table.Column<DateOnly>(type: "date", nullable: false),
                    status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    cancelled_reason = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    confirmed_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    attended_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    cancelled_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_recovery_reservations", x => x.id);
                    table.CheckConstraint("CK_recovery_reservations_cancelled_reason", "(status = 'cancelled' AND cancelled_reason IN ('student', 'shift_cancelled')) OR (status <> 'cancelled' AND cancelled_reason IS NULL)");
                    table.CheckConstraint("CK_recovery_reservations_status", "status IN ('confirmed', 'attended', 'cancelled', 'missed')");
                    table.CheckConstraint("CK_recovery_reservations_transition_audit", "(status = 'confirmed' AND attended_at IS NULL AND cancelled_at IS NULL AND cancelled_reason IS NULL) OR (status = 'attended' AND attended_at IS NOT NULL AND cancelled_at IS NULL AND cancelled_reason IS NULL) OR (status = 'cancelled' AND attended_at IS NULL AND cancelled_at IS NOT NULL AND cancelled_reason IS NOT NULL) OR (status = 'missed' AND attended_at IS NULL AND cancelled_at IS NULL AND cancelled_reason IS NULL)");
                    table.ForeignKey(
                        name: "FK_recovery_reservations_gym_shifts_shift_id",
                        column: x => x.shift_id,
                        principalTable: "gym_shifts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_recovery_reservations_recovery_credits_student_id_recovery_credit_id",
                        columns: x => new { x.student_id, x.recovery_credit_id },
                        principalTable: "recovery_credits",
                        principalColumns: new[] { "student_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_recovery_reservations_students_student_id",
                        column: x => x.student_id,
                        principalTable: "students",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_attendances_recovery_credit_id",
                table: "attendances",
                column: "recovery_credit_id",
                unique: true,
                filter: "recovery_credit_id IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_attendances_charge_recovery",
                table: "attendances",
                sql: "(charge_kind = 'ordinary' AND recovery_credit_id IS NULL) OR (charge_kind = 'recovery' AND recovery_credit_id IS NOT NULL)");

            migrationBuilder.CreateIndex(
                name: "IX_recovery_credits_plan_limits_id",
                table: "recovery_credits",
                column: "plan_limits_id");

            migrationBuilder.CreateIndex(
                name: "IX_recovery_credits_recovery_week_id_ordinal",
                table: "recovery_credits",
                columns: new[] { "recovery_week_id", "ordinal" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_recovery_credits_student_id_attendance_period_id",
                table: "recovery_credits",
                columns: new[] { "student_id", "attendance_period_id" });

            migrationBuilder.CreateIndex(
                name: "IX_recovery_credits_student_id_recovery_week_id",
                table: "recovery_credits",
                columns: new[] { "student_id", "recovery_week_id" });

            migrationBuilder.CreateIndex(
                name: "IX_recovery_credits_student_id_status",
                table: "recovery_credits",
                columns: new[] { "student_id", "status" });

            migrationBuilder.CreateIndex(
                name: "IX_recovery_reservations_recovery_credit_id",
                table: "recovery_reservations",
                column: "recovery_credit_id",
                unique: true,
                filter: "status = 'confirmed'");

            migrationBuilder.CreateIndex(
                name: "IX_recovery_reservations_shift_id",
                table: "recovery_reservations",
                column: "shift_id");

            migrationBuilder.CreateIndex(
                name: "IX_recovery_reservations_student_id_local_date",
                table: "recovery_reservations",
                columns: new[] { "student_id", "local_date" },
                unique: true,
                filter: "status = 'confirmed'");

            migrationBuilder.CreateIndex(
                name: "IX_recovery_reservations_student_id_recovery_credit_id",
                table: "recovery_reservations",
                columns: new[] { "student_id", "recovery_credit_id" });

            migrationBuilder.CreateIndex(
                name: "IX_recovery_weeks_plan_limits_id",
                table: "recovery_weeks",
                column: "plan_limits_id");

            migrationBuilder.CreateIndex(
                name: "IX_recovery_weeks_student_id_attendance_period_id",
                table: "recovery_weeks",
                columns: new[] { "student_id", "attendance_period_id" });

            migrationBuilder.CreateIndex(
                name: "IX_recovery_weeks_student_id_week_start",
                table: "recovery_weeks",
                columns: new[] { "student_id", "week_start" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_attendances_recovery_credits_recovery_credit_id",
                table: "attendances",
                column: "recovery_credit_id",
                principalTable: "recovery_credits",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_attendances_recovery_credits_recovery_credit_id",
                table: "attendances");

            migrationBuilder.DropTable(
                name: "recovery_reservations");

            migrationBuilder.DropTable(
                name: "recovery_credits");

            migrationBuilder.DropTable(
                name: "recovery_weeks");

            migrationBuilder.DropIndex(
                name: "IX_attendances_recovery_credit_id",
                table: "attendances");

            migrationBuilder.DropCheckConstraint(
                name: "CK_attendances_charge_recovery",
                table: "attendances");

            migrationBuilder.DropColumn(
                name: "recovery_credit_id",
                table: "attendances");
        }
    }
}

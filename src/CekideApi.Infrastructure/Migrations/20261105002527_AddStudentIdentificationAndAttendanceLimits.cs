using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CekideApi.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddStudentIdentificationAndAttendanceLimits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "dni",
                table: "students",
                type: "nvarchar(8)",
                maxLength: 8,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "first_name",
                table: "students",
                type: "nvarchar(150)",
                maxLength: 150,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "last_name",
                table: "students",
                type: "nvarchar(150)",
                maxLength: 150,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "plan_attendance_limits",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    plan_id = table.Column<int>(type: "int", nullable: false),
                    weekly_days = table.Column<int>(type: "int", nullable: false),
                    monthly_days = table.Column<int>(type: "int", nullable: false),
                    effective_from = table.Column<DateOnly>(type: "date", nullable: false),
                    configured_by = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_plan_attendance_limits", x => x.id);
                    table.CheckConstraint("CK_plan_attendance_limits_monthly_days", "monthly_days > 0");
                    table.CheckConstraint("CK_plan_attendance_limits_weekly_days", "weekly_days BETWEEN 1 AND 7");
                    table.ForeignKey(
                        name: "FK_plan_attendance_limits_plans_plan_id",
                        column: x => x.plan_id,
                        principalTable: "plans",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_students_dni",
                table: "students",
                column: "dni",
                unique: true,
                filter: "dni IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_plan_attendance_limits_plan_id_effective_from",
                table: "plan_attendance_limits",
                columns: new[] { "plan_id", "effective_from" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "plan_attendance_limits");

            migrationBuilder.DropIndex(
                name: "IX_students_dni",
                table: "students");

            migrationBuilder.DropColumn(
                name: "dni",
                table: "students");

            migrationBuilder.DropColumn(
                name: "first_name",
                table: "students");

            migrationBuilder.DropColumn(
                name: "last_name",
                table: "students");
        }
    }
}

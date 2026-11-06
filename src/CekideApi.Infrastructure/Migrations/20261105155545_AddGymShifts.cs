using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CekideApi.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddGymShifts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "gym_shifts",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    local_date = table.Column<DateOnly>(type: "date", nullable: false),
                    starts_at = table.Column<TimeOnly>(type: "time(0)", nullable: false),
                    ends_at = table.Column<TimeOnly>(type: "time(0)", nullable: false),
                    capacity = table.Column<int>(type: "int", nullable: false, defaultValue: 12),
                    status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    created_by = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    cancelled_by = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    cancelled_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_gym_shifts", x => x.id);
                    table.CheckConstraint("CK_gym_shifts_cancellation_audit", "(status = 'open' AND cancelled_by IS NULL AND cancelled_at IS NULL) OR (status = 'cancelled' AND cancelled_by IS NOT NULL AND cancelled_at IS NOT NULL)");
                    table.CheckConstraint("CK_gym_shifts_capacity", "capacity = 12");
                    table.CheckConstraint("CK_gym_shifts_duration", "ends_at > starts_at AND DATEDIFF(MINUTE, starts_at, ends_at) = 60");
                    table.CheckConstraint("CK_gym_shifts_status", "status IN ('open', 'cancelled')");
                });

            migrationBuilder.CreateTable(
                name: "shift_habitual_occupancies",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    shift_id = table.Column<int>(type: "int", nullable: false),
                    student_id = table.Column<int>(type: "int", nullable: false),
                    local_date = table.Column<DateOnly>(type: "date", nullable: false),
                    status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    registered_by = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    removed_by = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    removed_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_shift_habitual_occupancies", x => x.id);
                    table.CheckConstraint("CK_shift_habitual_occupancies_removal_audit", "(status = 'active' AND removed_by IS NULL AND removed_at IS NULL) OR (status = 'removed' AND removed_by IS NOT NULL AND removed_at IS NOT NULL)");
                    table.CheckConstraint("CK_shift_habitual_occupancies_status", "status IN ('active', 'removed')");
                    table.ForeignKey(
                        name: "FK_shift_habitual_occupancies_gym_shifts_shift_id",
                        column: x => x.shift_id,
                        principalTable: "gym_shifts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_shift_habitual_occupancies_students_student_id",
                        column: x => x.student_id,
                        principalTable: "students",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_gym_shifts_local_date_starts_at",
                table: "gym_shifts",
                columns: new[] { "local_date", "starts_at" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_gym_shifts_status",
                table: "gym_shifts",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "IX_shift_habitual_occupancies_shift_id_student_id",
                table: "shift_habitual_occupancies",
                columns: new[] { "shift_id", "student_id" },
                unique: true,
                filter: "status = 'active'");

            migrationBuilder.CreateIndex(
                name: "IX_shift_habitual_occupancies_student_id_local_date",
                table: "shift_habitual_occupancies",
                columns: new[] { "student_id", "local_date" },
                unique: true,
                filter: "status = 'active'");

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "shift_habitual_occupancies");

            migrationBuilder.DropTable(
                name: "gym_shifts");

        }
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CekideApi.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPayments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "monthly_payments",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    student_id = table.Column<int>(type: "int", nullable: false),
                    period = table.Column<string>(type: "char(7)", unicode: false, fixedLength: true, maxLength: 7, nullable: false),
                    amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    status = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    paid_on = table.Column<DateOnly>(type: "date", nullable: false),
                    registered_by = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    registered_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_monthly_payments", x => x.id);
                    table.ForeignKey(
                        name: "FK_monthly_payments_students_student_id",
                        column: x => x.student_id,
                        principalTable: "students",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "payment_history",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    payment_kind = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    payment_id = table.Column<int>(type: "int", nullable: false),
                    action = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    old_values = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    new_values = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    performed_by = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    performed_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_payment_history", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "short_term_passes",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    student_id = table.Column<int>(type: "int", nullable: false),
                    type = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    valid_from = table.Column<DateOnly>(type: "date", nullable: false),
                    valid_until = table.Column<DateOnly>(type: "date", nullable: false),
                    status = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    registered_by = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    registered_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_short_term_passes", x => x.id);
                    table.ForeignKey(
                        name: "FK_short_term_passes_students_student_id",
                        column: x => x.student_id,
                        principalTable: "students",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_monthly_payments_period",
                table: "monthly_payments",
                column: "period");

            migrationBuilder.CreateIndex(
                name: "IX_monthly_payments_student_id_period",
                table: "monthly_payments",
                columns: new[] { "student_id", "period" },
                unique: true,
                filter: "[status] = 'Paid'");

            migrationBuilder.CreateIndex(
                name: "IX_payment_history_payment_kind_payment_id",
                table: "payment_history",
                columns: new[] { "payment_kind", "payment_id" });

            migrationBuilder.CreateIndex(
                name: "IX_short_term_passes_student_id",
                table: "short_term_passes",
                column: "student_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "monthly_payments");

            migrationBuilder.DropTable(
                name: "payment_history");

            migrationBuilder.DropTable(
                name: "short_term_passes");
        }
    }
}

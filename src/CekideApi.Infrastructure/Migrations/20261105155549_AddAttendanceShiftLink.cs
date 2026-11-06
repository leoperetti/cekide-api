using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CekideApi.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAttendanceShiftLink : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "shift_id",
                table: "attendances",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_attendances_shift_id",
                table: "attendances",
                column: "shift_id");

            migrationBuilder.AddForeignKey(
                name: "FK_attendances_gym_shifts_shift_id",
                table: "attendances",
                column: "shift_id",
                principalTable: "gym_shifts",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_attendances_gym_shifts_shift_id",
                table: "attendances");

            migrationBuilder.DropIndex(
                name: "IX_attendances_shift_id",
                table: "attendances");

            migrationBuilder.DropColumn(
                name: "shift_id",
                table: "attendances");
        }
    }
}

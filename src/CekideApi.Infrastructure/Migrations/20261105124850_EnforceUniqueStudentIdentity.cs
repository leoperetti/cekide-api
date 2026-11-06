using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CekideApi.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class EnforceUniqueStudentIdentity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (
                    SELECT [identity_id]
                    FROM [students]
                    WHERE [identity_id] IS NOT NULL
                    GROUP BY [identity_id]
                    HAVING COUNT(*) > 1
                )
                    THROW 51000, 'Cannot enforce unique student identities while duplicate identity_id values exist.', 1;
                """);

            migrationBuilder.DropIndex(
                name: "IX_students_identity_id",
                table: "students");

            migrationBuilder.CreateIndex(
                name: "IX_students_identity_id",
                table: "students",
                column: "identity_id",
                unique: true,
                filter: "identity_id IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_students_identity_id",
                table: "students");

            migrationBuilder.CreateIndex(
                name: "IX_students_identity_id",
                table: "students",
                column: "identity_id");
        }
    }
}

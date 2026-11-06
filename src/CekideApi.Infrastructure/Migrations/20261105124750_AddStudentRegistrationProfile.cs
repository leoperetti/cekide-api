using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CekideApi.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddStudentRegistrationProfile : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "dni",
                table: "student_invitations",
                type: "nvarchar(8)",
                maxLength: 8,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "first_name",
                table: "student_invitations",
                type: "nvarchar(150)",
                maxLength: 150,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "full_name",
                table: "student_invitations",
                type: "nvarchar(150)",
                maxLength: 150,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "last_name",
                table: "student_invitations",
                type: "nvarchar(150)",
                maxLength: 150,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "phone_number",
                table: "student_invitations",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "profile_synced_at",
                table: "student_invitations",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "requires_registration_profile",
                table: "student_invitations",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "dni",
                table: "student_invitations");

            migrationBuilder.DropColumn(
                name: "first_name",
                table: "student_invitations");

            migrationBuilder.DropColumn(
                name: "full_name",
                table: "student_invitations");

            migrationBuilder.DropColumn(
                name: "last_name",
                table: "student_invitations");

            migrationBuilder.DropColumn(
                name: "phone_number",
                table: "student_invitations");

            migrationBuilder.DropColumn(
                name: "profile_synced_at",
                table: "student_invitations");

            migrationBuilder.DropColumn(
                name: "requires_registration_profile",
                table: "student_invitations");
        }
    }
}

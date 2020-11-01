using Microsoft.EntityFrameworkCore.Migrations;

namespace QuizBowlDiscordScoreTracker.Migrations
{
#pragma warning disable CA1062 // Validate arguments of public methods
    public partial class ReaderRolePrefix : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ReaderRolePrefix",
                table: "Guilds",
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ReaderRolePrefix",
                table: "Guilds");
        }
    }
#pragma warning restore CA1062 // Validate arguments of public methods
}

using Microsoft.EntityFrameworkCore.Migrations;

namespace QuizBowlDiscordScoreTracker.Migrations
{
#pragma warning disable CA1062 // Validate arguments of public methods
#pragma warning disable CA1711 // Identifiers should not have incorrect suffix
    public partial class DisableBuzzQueue : Migration
#pragma warning restore CA1711 // Identifiers should not have incorrect suffix
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "DisableBuzzQueue",
                table: "Guilds",
                nullable: false,
                defaultValue: false);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DisableBuzzQueue",
                table: "Guilds");
        }
    }
#pragma warning disable CA1062 // Validate arguments of public methods
}

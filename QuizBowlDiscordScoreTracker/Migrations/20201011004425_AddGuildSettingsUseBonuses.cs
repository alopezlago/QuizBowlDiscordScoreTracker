using Microsoft.EntityFrameworkCore.Migrations;

namespace QuizBowlDiscordScoreTracker.Migrations
{
#pragma warning disable CA1062 // Validate arguments of public methods
    public partial class AddGuildSettingsUseBonuses : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "UseBonuses",
                table: "Guilds",
                nullable: false,
                defaultValue: false);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "UseBonuses",
                table: "Guilds");
        }
    }
#pragma warning restore CA1062 // Validate arguments of public methods
}

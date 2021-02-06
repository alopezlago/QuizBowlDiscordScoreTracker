using Microsoft.EntityFrameworkCore.Migrations;

namespace QuizBowlDiscordScoreTracker.Migrations
{
#pragma warning disable CA1062 // Validate arguments of public methods
    public partial class DisableBuzzQueue : Migration
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

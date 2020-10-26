using Microsoft.EntityFrameworkCore.Migrations;

namespace QuizBowlDiscordScoreTracker.Migrations
{
#pragma warning disable CA1062 // Validate arguments of public methods
    public partial class ExportThrottle : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ExportCount",
                table: "Guilds",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "LastExportDay",
                table: "Guilds",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    UserSettingId = table.Column<ulong>(nullable: false)
                        .Annotation("Sqlite:Autoincrement", false),
                    ExportCount = table.Column<int>(nullable: false),
                    LastExportDay = table.Column<int>(nullable: false),
                    CommandBanned = table.Column<bool>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.UserSettingId);
                });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Users");

            migrationBuilder.DropColumn(
                name: "ExportCount",
                table: "Guilds");

            migrationBuilder.DropColumn(
                name: "LastExportDay",
                table: "Guilds");
        }
    }
#pragma warning restore CA1062 // Validate arguments of public methods
}

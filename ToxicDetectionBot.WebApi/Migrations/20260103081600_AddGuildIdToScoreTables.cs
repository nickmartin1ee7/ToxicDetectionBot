using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ToxicDetectionBot.WebApi.Migrations
{
    /// <inheritdoc />
    public partial class AddGuildIdToScoreTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "GuildId",
                table: "UserSentimentScores",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "GuildId",
                table: "UserAlignmentScores",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_UserSentimentScores_GuildId",
                table: "UserSentimentScores",
                column: "GuildId");

            migrationBuilder.CreateIndex(
                name: "IX_UserSentimentScores_UserId",
                table: "UserSentimentScores",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_UserAlignmentScores_GuildId",
                table: "UserAlignmentScores",
                column: "GuildId");

            migrationBuilder.CreateIndex(
                name: "IX_UserAlignmentScores_UserId",
                table: "UserAlignmentScores",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_UserSentimentScores_GuildId",
                table: "UserSentimentScores");

            migrationBuilder.DropIndex(
                name: "IX_UserSentimentScores_UserId",
                table: "UserSentimentScores");

            migrationBuilder.DropIndex(
                name: "IX_UserAlignmentScores_GuildId",
                table: "UserAlignmentScores");

            migrationBuilder.DropIndex(
                name: "IX_UserAlignmentScores_UserId",
                table: "UserAlignmentScores");

            migrationBuilder.DropColumn(
                name: "GuildId",
                table: "UserSentimentScores");

            migrationBuilder.DropColumn(
                name: "GuildId",
                table: "UserAlignmentScores");
        }
    }
}

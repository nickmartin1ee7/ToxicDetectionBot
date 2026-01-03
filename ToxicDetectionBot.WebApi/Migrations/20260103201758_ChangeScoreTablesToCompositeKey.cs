using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ToxicDetectionBot.WebApi.Migrations
{
    /// <inheritdoc />
    public partial class ChangeScoreTablesToCompositeKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_UserSentimentScores",
                table: "UserSentimentScores");

            migrationBuilder.DropPrimaryKey(
                name: "PK_UserAlignmentScores",
                table: "UserAlignmentScores");

            migrationBuilder.AddPrimaryKey(
                name: "PK_UserSentimentScores",
                table: "UserSentimentScores",
                columns: new[] { "UserId", "GuildId" });

            migrationBuilder.AddPrimaryKey(
                name: "PK_UserAlignmentScores",
                table: "UserAlignmentScores",
                columns: new[] { "UserId", "GuildId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_UserSentimentScores",
                table: "UserSentimentScores");

            migrationBuilder.DropPrimaryKey(
                name: "PK_UserAlignmentScores",
                table: "UserAlignmentScores");

            migrationBuilder.AddPrimaryKey(
                name: "PK_UserSentimentScores",
                table: "UserSentimentScores",
                column: "UserId");

            migrationBuilder.AddPrimaryKey(
                name: "PK_UserAlignmentScores",
                table: "UserAlignmentScores",
                column: "UserId");
        }
    }
}

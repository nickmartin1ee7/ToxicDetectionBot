using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ToxicDetectionBot.WebApi.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "UserAlignmentScores",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "TEXT", nullable: false),
                    LawfulGoodCount = table.Column<int>(type: "INTEGER", nullable: false),
                    NeutralGoodCount = table.Column<int>(type: "INTEGER", nullable: false),
                    ChaoticGoodCount = table.Column<int>(type: "INTEGER", nullable: false),
                    LawfulNeutralCount = table.Column<int>(type: "INTEGER", nullable: false),
                    TrueNeutralCount = table.Column<int>(type: "INTEGER", nullable: false),
                    ChaoticNeutralCount = table.Column<int>(type: "INTEGER", nullable: false),
                    LawfulEvilCount = table.Column<int>(type: "INTEGER", nullable: false),
                    NeutralEvilCount = table.Column<int>(type: "INTEGER", nullable: false),
                    ChaoticEvilCount = table.Column<int>(type: "INTEGER", nullable: false),
                    DominantAlignment = table.Column<string>(type: "TEXT", nullable: false),
                    SummarizedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserAlignmentScores", x => x.UserId);
                });

            migrationBuilder.CreateTable(
                name: "UserOptOuts",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "TEXT", nullable: false),
                    IsOptedOut = table.Column<bool>(type: "INTEGER", nullable: false),
                    LastChangedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserOptOuts", x => x.UserId);
                });

            migrationBuilder.CreateTable(
                name: "UserSentiments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserId = table.Column<string>(type: "TEXT", nullable: false),
                    GuildId = table.Column<string>(type: "TEXT", nullable: false),
                    MessageId = table.Column<string>(type: "TEXT", nullable: false),
                    MessageContent = table.Column<string>(type: "TEXT", nullable: false),
                    Username = table.Column<string>(type: "TEXT", nullable: false),
                    GuildName = table.Column<string>(type: "TEXT", nullable: false),
                    ChannelName = table.Column<string>(type: "TEXT", nullable: false),
                    IsToxic = table.Column<bool>(type: "INTEGER", nullable: false),
                    Alignment = table.Column<string>(type: "TEXT", nullable: false),
                    IsSummarized = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserSentiments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "UserSentimentScores",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "TEXT", nullable: false),
                    TotalMessages = table.Column<int>(type: "INTEGER", nullable: false),
                    ToxicMessages = table.Column<int>(type: "INTEGER", nullable: false),
                    NonToxicMessages = table.Column<int>(type: "INTEGER", nullable: false),
                    ToxicityPercentage = table.Column<double>(type: "REAL", nullable: false),
                    SummarizedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserSentimentScores", x => x.UserId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UserOptOuts_IsOptedOut",
                table: "UserOptOuts",
                column: "IsOptedOut");

            migrationBuilder.CreateIndex(
                name: "IX_UserSentiments_ChannelName",
                table: "UserSentiments",
                column: "ChannelName");

            migrationBuilder.CreateIndex(
                name: "IX_UserSentiments_GuildId",
                table: "UserSentiments",
                column: "GuildId");

            migrationBuilder.CreateIndex(
                name: "IX_UserSentiments_GuildName",
                table: "UserSentiments",
                column: "GuildName");

            migrationBuilder.CreateIndex(
                name: "IX_UserSentiments_IsSummarized",
                table: "UserSentiments",
                column: "IsSummarized");

            migrationBuilder.CreateIndex(
                name: "IX_UserSentiments_UserId",
                table: "UserSentiments",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UserAlignmentScores");

            migrationBuilder.DropTable(
                name: "UserOptOuts");

            migrationBuilder.DropTable(
                name: "UserSentiments");

            migrationBuilder.DropTable(
                name: "UserSentimentScores");
        }
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ConstructionSaaS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTaskNotificationsAndProjectAudit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ProjectId",
                table: "WorkspaceActivities",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "WorkspaceNotifications",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CompanyId = table.Column<int>(type: "int", nullable: false),
                    ProjectId = table.Column<int>(type: "int", nullable: false),
                    RecipientUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    Message = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ReadAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkspaceNotifications", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WorkspaceActivities_CompanyId_ProjectId_OccurredAtUtc",
                table: "WorkspaceActivities",
                columns: new[] { "CompanyId", "ProjectId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkspaceNotifications_CompanyId_RecipientUserId_CreatedAtUtc",
                table: "WorkspaceNotifications",
                columns: new[] { "CompanyId", "RecipientUserId", "CreatedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WorkspaceNotifications");

            migrationBuilder.DropIndex(
                name: "IX_WorkspaceActivities_CompanyId_ProjectId_OccurredAtUtc",
                table: "WorkspaceActivities");

            migrationBuilder.DropColumn(
                name: "ProjectId",
                table: "WorkspaceActivities");
        }
    }
}

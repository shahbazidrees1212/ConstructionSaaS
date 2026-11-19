using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ConstructionSaaS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDocumentUploaderIdentity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "UploadedByUserId",
                table: "ProjectDocuments",
                type: "nvarchar(450)",
                maxLength: 450,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "UploadedByUserId",
                table: "ProjectDocuments");
        }
    }
}

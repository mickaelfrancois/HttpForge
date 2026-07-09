using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HttpForge.Migrations
{
    /// <inheritdoc />
    public partial class AddOpenApiRefreshTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SourceOperationKey",
                table: "Requests",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SourceOpenApiUrl",
                table: "Collections",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SourceOperationKey",
                table: "Requests");

            migrationBuilder.DropColumn(
                name: "SourceOpenApiUrl",
                table: "Collections");
        }
    }
}

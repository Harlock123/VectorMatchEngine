using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VectorMatchEngine.Data.Migrations
{
    /// <inheritdoc />
    public partial class VectorEncoding : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // How each dataset's vectors were made: matching key and sounds-alike columns (see VectorEncoding).
            migrationBuilder.AddColumn<string>(
                name: "Encoding",
                table: "Datasets",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "trigram");   // every dataset so far used the original encoding

            migrationBuilder.AddColumn<string>(
                name: "KeyFingerprint",
                table: "Datasets",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SoundsAlikeColumnsJson",
                table: "Datasets",
                type: "NVARCHAR(MAX)",
                nullable: false,
                defaultValue: "[]");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Encoding",
                table: "Datasets");

            migrationBuilder.DropColumn(
                name: "KeyFingerprint",
                table: "Datasets");

            migrationBuilder.DropColumn(
                name: "SoundsAlikeColumnsJson",
                table: "Datasets");
        }
    }
}

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VectorMatchEngine.Data.Migrations
{
    /// <inheritdoc />
    public partial class FieldScoring : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Field-by-field scoring: per-field vectors on each record, field types (clean-up) on each dataset, a
            // job's settings and pairs compared, and each result's field scores and runner-up. Everything from
            // before has none of these, and is matched whole-row as it always was.
            migrationBuilder.AddColumn<string>(
                name: "FieldScores",
                table: "MatchResults",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "RunnerUpScore",
                table: "MatchResults",
                type: "float",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "PairsCompared",
                table: "MatchJobs",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SettingsJson",
                table: "MatchJobs",
                type: "NVARCHAR(MAX)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Cleanup",
                table: "Datasets",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FieldKindsJson",
                table: "Datasets",
                type: "NVARCHAR(MAX)",
                nullable: false,
                defaultValue: "{}");   // no field types: nothing was cleaned up

            migrationBuilder.AddColumn<bool>(
                name: "HasFieldVectors",
                table: "Datasets",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<byte[]>(
                name: "FieldVectorData",
                table: "DatasetRecords",
                type: "VARBINARY(MAX)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FieldScores",
                table: "MatchResults");

            migrationBuilder.DropColumn(
                name: "RunnerUpScore",
                table: "MatchResults");

            migrationBuilder.DropColumn(
                name: "PairsCompared",
                table: "MatchJobs");

            migrationBuilder.DropColumn(
                name: "SettingsJson",
                table: "MatchJobs");

            migrationBuilder.DropColumn(
                name: "Cleanup",
                table: "Datasets");

            migrationBuilder.DropColumn(
                name: "FieldKindsJson",
                table: "Datasets");

            migrationBuilder.DropColumn(
                name: "HasFieldVectors",
                table: "Datasets");

            migrationBuilder.DropColumn(
                name: "FieldVectorData",
                table: "DatasetRecords");
        }
    }
}

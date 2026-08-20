using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VectorMatchEngine.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Datasets",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    RowCount = table.Column<int>(type: "int", nullable: false),
                    VectorizedColumnsJson = table.Column<string>(type: "NVARCHAR(MAX)", nullable: false),
                    PreservedColumnsJson = table.Column<string>(type: "NVARCHAR(MAX)", nullable: false),
                    VectorDimensions = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Datasets", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DatasetRecords",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DatasetId = table.Column<int>(type: "int", nullable: false),
                    RowIndex = table.Column<int>(type: "int", nullable: false),
                    VectorData = table.Column<byte[]>(type: "VARBINARY(MAX)", nullable: false),
                    PreservedDataJson = table.Column<string>(type: "NVARCHAR(MAX)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DatasetRecords", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DatasetRecords_Datasets_DatasetId",
                        column: x => x.DatasetId,
                        principalTable: "Datasets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MatchJobs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DatasetAId = table.Column<int>(type: "int", nullable: false),
                    DatasetBId = table.Column<int>(type: "int", nullable: false),
                    Threshold = table.Column<double>(type: "float", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    TotalMatchesFound = table.Column<int>(type: "int", nullable: false),
                    ErrorMessage = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MatchJobs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MatchJobs_Datasets_DatasetAId",
                        column: x => x.DatasetAId,
                        principalTable: "Datasets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MatchJobs_Datasets_DatasetBId",
                        column: x => x.DatasetBId,
                        principalTable: "Datasets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MatchResults",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    MatchJobId = table.Column<int>(type: "int", nullable: false),
                    RecordAId = table.Column<long>(type: "bigint", nullable: false),
                    RecordBId = table.Column<long>(type: "bigint", nullable: false),
                    SimilarityScore = table.Column<double>(type: "float", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MatchResults", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MatchResults_DatasetRecords_RecordAId",
                        column: x => x.RecordAId,
                        principalTable: "DatasetRecords",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MatchResults_DatasetRecords_RecordBId",
                        column: x => x.RecordBId,
                        principalTable: "DatasetRecords",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MatchResults_MatchJobs_MatchJobId",
                        column: x => x.MatchJobId,
                        principalTable: "MatchJobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DatasetRecords_DatasetId",
                table: "DatasetRecords",
                column: "DatasetId");

            migrationBuilder.CreateIndex(
                name: "IX_MatchJobs_DatasetAId",
                table: "MatchJobs",
                column: "DatasetAId");

            migrationBuilder.CreateIndex(
                name: "IX_MatchJobs_DatasetBId",
                table: "MatchJobs",
                column: "DatasetBId");

            migrationBuilder.CreateIndex(
                name: "IX_MatchResults_MatchJobId",
                table: "MatchResults",
                column: "MatchJobId");

            migrationBuilder.CreateIndex(
                name: "IX_MatchResults_RecordAId",
                table: "MatchResults",
                column: "RecordAId");

            migrationBuilder.CreateIndex(
                name: "IX_MatchResults_RecordBId",
                table: "MatchResults",
                column: "RecordBId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MatchResults");

            migrationBuilder.DropTable(
                name: "DatasetRecords");

            migrationBuilder.DropTable(
                name: "MatchJobs");

            migrationBuilder.DropTable(
                name: "Datasets");
        }
    }
}

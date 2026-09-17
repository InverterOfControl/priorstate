using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PriorState.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddApiSourceExecutions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "source_executions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BindingId = table.Column<Guid>(type: "uuid", nullable: false),
                    RunId = table.Column<Guid>(type: "uuid", nullable: true),
                    State = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    QueuedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    FinishedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    SnapshotId = table.Column<Guid>(type: "uuid", nullable: true),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: true),
                    MediaType = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Error = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_source_executions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_source_executions_plugin_binding_versions_BindingId",
                        column: x => x.BindingId,
                        principalTable: "plugin_binding_versions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_source_executions_runs_RunId",
                        column: x => x.RunId,
                        principalTable: "runs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_source_executions_RunId_BindingId",
                table: "source_executions",
                columns: new[] { "RunId", "BindingId" },
                unique: true,
                filter: "\"RunId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_source_executions_State_QueuedAt",
                table: "source_executions",
                columns: new[] { "State", "QueuedAt" });

            migrationBuilder.CreateIndex(
                name: "ix_source_executions_pending_test",
                table: "source_executions",
                column: "BindingId",
                unique: true,
                filter: "\"RunId\" IS NULL AND \"State\" IN ('Queued', 'Running')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "source_executions");
        }
    }
}

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PriorState.Data.Migrations
{
    /// <inheritdoc />
    public partial class RenameSnapshotPayloadColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "WaczSizeBytes",
                table: "snapshots",
                newName: "PayloadSizeBytes");

            migrationBuilder.RenameColumn(
                name: "WaczSha256",
                table: "snapshots",
                newName: "PayloadSha256");

            migrationBuilder.RenameColumn(
                name: "WaczObjectKey",
                table: "snapshots",
                newName: "PayloadObjectKey");

            migrationBuilder.RenameIndex(
                name: "ix_snapshots_wacz_sha256",
                table: "snapshots",
                newName: "ix_snapshots_payload_sha256");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "PayloadSizeBytes",
                table: "snapshots",
                newName: "WaczSizeBytes");

            migrationBuilder.RenameColumn(
                name: "PayloadSha256",
                table: "snapshots",
                newName: "WaczSha256");

            migrationBuilder.RenameColumn(
                name: "PayloadObjectKey",
                table: "snapshots",
                newName: "WaczObjectKey");

            migrationBuilder.RenameIndex(
                name: "ix_snapshots_payload_sha256",
                table: "snapshots",
                newName: "ix_snapshots_wacz_sha256");
        }
    }
}

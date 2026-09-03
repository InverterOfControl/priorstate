using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PriorState.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCapturePluginBindings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<int>(
                name: "Conditions_ViewportWidth",
                table: "snapshots",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<int>(
                name: "Conditions_ViewportHeight",
                table: "snapshots",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<string>(
                name: "Conditions_UserAgent",
                table: "snapshots",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(512)",
                oldMaxLength: 512);

            migrationBuilder.AlterColumn<int>(
                name: "Conditions_JavaScriptSettleMs",
                table: "snapshots",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<string>(
                name: "Conditions_CrawlerVersion",
                table: "snapshots",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64);

            migrationBuilder.AlterColumn<string>(
                name: "Conditions_CookieBanner",
                table: "snapshots",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(32)",
                oldMaxLength: 32);

            migrationBuilder.AlterColumn<string>(
                name: "Conditions_ChromiumVersion",
                table: "snapshots",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64);

            migrationBuilder.AlterColumn<bool>(
                name: "Conditions_AuthenticatedSession",
                table: "snapshots",
                type: "boolean",
                nullable: true,
                oldClrType: typeof(bool),
                oldType: "boolean");

            migrationBuilder.AlterColumn<bool>(
                name: "Conditions_AdBlockerActive",
                table: "snapshots",
                type: "boolean",
                nullable: true,
                oldClrType: typeof(bool),
                oldType: "boolean");

            migrationBuilder.AddColumn<string>(
                name: "CanonicalFormVersion",
                table: "snapshots",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "priorstate-snapshot-v1");

            migrationBuilder.AddColumn<string>(
                name: "PayloadMediaType",
                table: "snapshots",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "application/wacz");

            migrationBuilder.AddColumn<Guid>(
                name: "PluginBindingVersionId",
                table: "snapshots",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PluginVersion",
                table: "snapshots",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "plugin_binding_versions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    PluginId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    ConfigurationJson = table.Column<string>(type: "text", nullable: false),
                    SecretRef = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Rationale = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    Required = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    SupersededAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_plugin_binding_versions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_plugin_binding_versions_projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_snapshots_PluginBindingVersionId",
                table: "snapshots",
                column: "PluginBindingVersionId");

            migrationBuilder.CreateIndex(
                name: "ix_plugin_binding_versions_project_name_version",
                table: "plugin_binding_versions",
                columns: new[] { "ProjectId", "Name", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_plugin_binding_versions_project_superseded_at",
                table: "plugin_binding_versions",
                columns: new[] { "ProjectId", "SupersededAt" });

            migrationBuilder.AddForeignKey(
                name: "FK_snapshots_plugin_binding_versions_PluginBindingVersionId",
                table: "snapshots",
                column: "PluginBindingVersionId",
                principalTable: "plugin_binding_versions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            // --- The backfilled columns above are set through ADD COLUMN ... DEFAULT, which fills
            // existing rows as part of the DDL rather than through an UPDATE. The append-only row
            // trigger on snapshots is therefore never involved, and no entry hash changes: neither
            // column is an input to any canonical form.
            migrationBuilder.Sql("""
                ALTER TABLE snapshots ALTER COLUMN "CanonicalFormVersion" DROP DEFAULT;
                """);

            migrationBuilder.Sql("""
                ALTER TABLE snapshots ALTER COLUMN "PayloadMediaType" DROP DEFAULT;
                """);

            // --- plugin_binding_versions: named in the protocol of every snapshot a plugin made,
            // and its digest is part of that snapshot's entry hash. Same guarantee as capture
            // profiles: superseding is the only permitted change, and the database enforces it.
            migrationBuilder.Sql("""
                CREATE TRIGGER plugin_binding_versions_append_only
                    BEFORE UPDATE OR DELETE ON plugin_binding_versions
                    FOR EACH ROW EXECUTE FUNCTION priorstate_set_once_only('SupersededAt');
                """);

            migrationBuilder.Sql("""
                CREATE TRIGGER plugin_binding_versions_no_truncate
                    BEFORE TRUNCATE ON plugin_binding_versions
                    FOR EACH STATEMENT EXECUTE FUNCTION priorstate_reject_mutation();
                """);

            // Without this the application role has no privilege on the new table at all, and
            // every insert fails in any deployment that runs as priorstate_app.
            migrationBuilder.Sql("""
                GRANT SELECT, INSERT ON plugin_binding_versions TO priorstate_app;
                """);

            migrationBuilder.Sql("""
                GRANT UPDATE ("SupersededAt") ON plugin_binding_versions TO priorstate_app;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "DROP TRIGGER IF EXISTS plugin_binding_versions_append_only ON plugin_binding_versions;");
            migrationBuilder.Sql(
                "DROP TRIGGER IF EXISTS plugin_binding_versions_no_truncate ON plugin_binding_versions;");

            migrationBuilder.DropForeignKey(
                name: "FK_snapshots_plugin_binding_versions_PluginBindingVersionId",
                table: "snapshots");

            migrationBuilder.DropTable(
                name: "plugin_binding_versions");

            migrationBuilder.DropIndex(
                name: "IX_snapshots_PluginBindingVersionId",
                table: "snapshots");

            migrationBuilder.DropColumn(
                name: "CanonicalFormVersion",
                table: "snapshots");

            migrationBuilder.DropColumn(
                name: "PayloadMediaType",
                table: "snapshots");

            migrationBuilder.DropColumn(
                name: "PluginBindingVersionId",
                table: "snapshots");

            migrationBuilder.DropColumn(
                name: "PluginVersion",
                table: "snapshots");

            migrationBuilder.AlterColumn<int>(
                name: "Conditions_ViewportWidth",
                table: "snapshots",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "Conditions_ViewportHeight",
                table: "snapshots",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Conditions_UserAgent",
                table: "snapshots",
                type: "character varying(512)",
                maxLength: 512,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(512)",
                oldMaxLength: 512,
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "Conditions_JavaScriptSettleMs",
                table: "snapshots",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Conditions_CrawlerVersion",
                table: "snapshots",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Conditions_CookieBanner",
                table: "snapshots",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(32)",
                oldMaxLength: 32,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Conditions_ChromiumVersion",
                table: "snapshots",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64,
                oldNullable: true);

            migrationBuilder.AlterColumn<bool>(
                name: "Conditions_AuthenticatedSession",
                table: "snapshots",
                type: "boolean",
                nullable: false,
                defaultValue: false,
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldNullable: true);

            migrationBuilder.AlterColumn<bool>(
                name: "Conditions_AdBlockerActive",
                table: "snapshots",
                type: "boolean",
                nullable: false,
                defaultValue: false,
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldNullable: true);
        }
    }
}

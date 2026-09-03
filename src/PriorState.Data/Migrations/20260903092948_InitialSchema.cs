using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace PriorState.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AspNetRoles",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    NormalizedName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    ConcurrencyStamp = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetRoles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUsers",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    DisplayName = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    LastSignInAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    ExternalProvider = table.Column<string>(type: "text", nullable: true),
                    UserName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    NormalizedUserName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    NormalizedEmail = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    EmailConfirmed = table.Column<bool>(type: "boolean", nullable: false),
                    PasswordHash = table.Column<string>(type: "text", nullable: true),
                    SecurityStamp = table.Column<string>(type: "text", nullable: true),
                    ConcurrencyStamp = table.Column<string>(type: "text", nullable: true),
                    PhoneNumber = table.Column<string>(type: "text", nullable: true),
                    PhoneNumberConfirmed = table.Column<bool>(type: "boolean", nullable: false),
                    TwoFactorEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    LockoutEnd = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    LockoutEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    AccessFailedCount = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUsers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "audit_log",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OccurredAtUtc = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    UserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    UserName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Action = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SubjectType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SubjectId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Detail = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    RemoteAddress = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_audit_log", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "capture_profile_versions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    Rationale = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    SupersededAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    Conditions_AdBlockerActive = table.Column<bool>(type: "boolean", nullable: false),
                    Conditions_AuthenticatedSession = table.Column<bool>(type: "boolean", nullable: false),
                    Conditions_ChromiumVersion = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Conditions_CookieBanner = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Conditions_CrawlerVersion = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Conditions_JavaScriptSettleMs = table.Column<int>(type: "integer", nullable: false),
                    Conditions_UserAgent = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    Conditions_ViewportHeight = table.Column<int>(type: "integer", nullable: false),
                    Conditions_ViewportWidth = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_capture_profile_versions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "timestamp_anchors",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CoversDateUtc = table.Column<DateOnly>(type: "date", nullable: false),
                    FirstChainSequence = table.Column<long>(type: "bigint", nullable: false),
                    LastChainSequence = table.Column<long>(type: "bigint", nullable: false),
                    MerkleRoot = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    TimestampToken = table.Column<byte[]>(type: "bytea", nullable: false),
                    TsaUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    TsaGeneralizedTime = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    QualifiedProvider = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_timestamp_anchors", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AspNetRoleClaims",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RoleId = table.Column<string>(type: "text", nullable: false),
                    ClaimType = table.Column<string>(type: "text", nullable: true),
                    ClaimValue = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetRoleClaims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AspNetRoleClaims_AspNetRoles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "AspNetRoles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserClaims",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<string>(type: "text", nullable: false),
                    ClaimType = table.Column<string>(type: "text", nullable: true),
                    ClaimValue = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserClaims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AspNetUserClaims_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserLogins",
                columns: table => new
                {
                    LoginProvider = table.Column<string>(type: "text", nullable: false),
                    ProviderKey = table.Column<string>(type: "text", nullable: false),
                    ProviderDisplayName = table.Column<string>(type: "text", nullable: true),
                    UserId = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserLogins", x => new { x.LoginProvider, x.ProviderKey });
                    table.ForeignKey(
                        name: "FK_AspNetUserLogins_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserRoles",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "text", nullable: false),
                    RoleId = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserRoles", x => new { x.UserId, x.RoleId });
                    table.ForeignKey(
                        name: "FK_AspNetUserRoles_AspNetRoles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "AspNetRoles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AspNetUserRoles_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserTokens",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "text", nullable: false),
                    LoginProvider = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Value = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserTokens", x => new { x.UserId, x.LoginProvider, x.Name });
                    table.ForeignKey(
                        name: "FK_AspNetUserTokens_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "projects",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    SeedUrls = table.Column<string>(type: "jsonb", nullable: false),
                    ScopeIncludes = table.Column<string>(type: "jsonb", nullable: false),
                    ScopeExcludes = table.Column<string>(type: "jsonb", nullable: false),
                    Schedule = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    RetentionYears = table.Column<int>(type: "integer", nullable: false),
                    CaptureProfileVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    Archived = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_projects", x => x.Id);
                    table.ForeignKey(
                        name: "FK_projects_capture_profile_versions_CaptureProfileVersionId",
                        column: x => x.CaptureProfileVersionId,
                        principalTable: "capture_profile_versions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "runs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    CaptureProfileVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Trigger = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    QueuedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    FinishedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    CrawlerExitCode = table.Column<int>(type: "integer", nullable: true),
                    FailureReason = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    CrawlerArguments = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_runs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_runs_capture_profile_versions_CaptureProfileVersionId",
                        column: x => x.CaptureProfileVersionId,
                        principalTable: "capture_profile_versions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_runs_projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "crawl_jobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RunId = table.Column<Guid>(type: "uuid", nullable: false),
                    State = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    AvailableAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    MaxAttempts = table.Column<int>(type: "integer", nullable: false),
                    ClaimedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ClaimedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    LastError = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_crawl_jobs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_crawl_jobs_runs_RunId",
                        column: x => x.RunId,
                        principalTable: "runs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "deployment_ledger_entries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    CommitSha = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CommitMessage = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Environment = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    DeployedAtUtc = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    RunId = table.Column<Guid>(type: "uuid", nullable: true),
                    Source = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    RecordedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_deployment_ledger_entries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_deployment_ledger_entries_projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_deployment_ledger_entries_runs_RunId",
                        column: x => x.RunId,
                        principalTable: "runs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "snapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RunId = table.Column<Guid>(type: "uuid", nullable: false),
                    Url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    FinalUrl = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    CapturedAtUtc = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    WaczSha256 = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    WaczObjectKey = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    WaczSizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    CaptureProfileVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExtractedText = table.Column<string>(type: "text", nullable: true),
                    ChainSequence = table.Column<long>(type: "bigint", nullable: false),
                    PreviousHash = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    EntryHash = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    TimestampAnchorId = table.Column<Guid>(type: "uuid", nullable: true),
                    StorageWorm = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    WormRetainUntil = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    Conditions_AdBlockerActive = table.Column<bool>(type: "boolean", nullable: false),
                    Conditions_AuthenticatedSession = table.Column<bool>(type: "boolean", nullable: false),
                    Conditions_ChromiumVersion = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Conditions_CookieBanner = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Conditions_CrawlerVersion = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Conditions_JavaScriptSettleMs = table.Column<int>(type: "integer", nullable: false),
                    Conditions_UserAgent = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    Conditions_ViewportHeight = table.Column<int>(type: "integer", nullable: false),
                    Conditions_ViewportWidth = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_snapshots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_snapshots_capture_profile_versions_CaptureProfileVersionId",
                        column: x => x.CaptureProfileVersionId,
                        principalTable: "capture_profile_versions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_snapshots_runs_RunId",
                        column: x => x.RunId,
                        principalTable: "runs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_snapshots_timestamp_anchors_TimestampAnchorId",
                        column: x => x.TimestampAnchorId,
                        principalTable: "timestamp_anchors",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AspNetRoleClaims_RoleId",
                table: "AspNetRoleClaims",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "RoleNameIndex",
                table: "AspNetRoles",
                column: "NormalizedName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserClaims_UserId",
                table: "AspNetUserClaims",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserLogins_UserId",
                table: "AspNetUserLogins",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserRoles_RoleId",
                table: "AspNetUserRoles",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "EmailIndex",
                table: "AspNetUsers",
                column: "NormalizedEmail");

            migrationBuilder.CreateIndex(
                name: "UserNameIndex",
                table: "AspNetUsers",
                column: "NormalizedUserName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_audit_log_occurred_at",
                table: "audit_log",
                column: "OccurredAtUtc");

            migrationBuilder.CreateIndex(
                name: "ix_audit_log_subject",
                table: "audit_log",
                columns: new[] { "SubjectType", "SubjectId" });

            migrationBuilder.CreateIndex(
                name: "ix_capture_profile_versions_name_version",
                table: "capture_profile_versions",
                columns: new[] { "Name", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_crawl_jobs_RunId",
                table: "crawl_jobs",
                column: "RunId");

            migrationBuilder.CreateIndex(
                name: "ix_crawl_jobs_state_available_at",
                table: "crawl_jobs",
                columns: new[] { "State", "AvailableAt" });

            migrationBuilder.CreateIndex(
                name: "IX_deployment_ledger_entries_RunId",
                table: "deployment_ledger_entries",
                column: "RunId");

            migrationBuilder.CreateIndex(
                name: "ix_deployment_ledger_commit_sha",
                table: "deployment_ledger_entries",
                column: "CommitSha");

            migrationBuilder.CreateIndex(
                name: "ix_deployment_ledger_project_deployed_at",
                table: "deployment_ledger_entries",
                columns: new[] { "ProjectId", "DeployedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_projects_CaptureProfileVersionId",
                table: "projects",
                column: "CaptureProfileVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_projects_Name",
                table: "projects",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_runs_CaptureProfileVersionId",
                table: "runs",
                column: "CaptureProfileVersionId");

            migrationBuilder.CreateIndex(
                name: "ix_runs_project_queued_at",
                table: "runs",
                columns: new[] { "ProjectId", "QueuedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_snapshots_CaptureProfileVersionId",
                table: "snapshots",
                column: "CaptureProfileVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_snapshots_RunId",
                table: "snapshots",
                column: "RunId");

            migrationBuilder.CreateIndex(
                name: "IX_snapshots_TimestampAnchorId",
                table: "snapshots",
                column: "TimestampAnchorId");

            migrationBuilder.CreateIndex(
                name: "ix_snapshots_chain_sequence",
                table: "snapshots",
                column: "ChainSequence",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_snapshots_entry_hash",
                table: "snapshots",
                column: "EntryHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_snapshots_url_captured_at",
                table: "snapshots",
                columns: new[] { "Url", "CapturedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "ix_snapshots_wacz_sha256",
                table: "snapshots",
                column: "WaczSha256");

            migrationBuilder.CreateIndex(
                name: "ix_timestamp_anchors_covers_date",
                table: "timestamp_anchors",
                column: "CoversDateUtc",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AspNetRoleClaims");

            migrationBuilder.DropTable(
                name: "AspNetUserClaims");

            migrationBuilder.DropTable(
                name: "AspNetUserLogins");

            migrationBuilder.DropTable(
                name: "AspNetUserRoles");

            migrationBuilder.DropTable(
                name: "AspNetUserTokens");

            migrationBuilder.DropTable(
                name: "audit_log");

            migrationBuilder.DropTable(
                name: "crawl_jobs");

            migrationBuilder.DropTable(
                name: "deployment_ledger_entries");

            migrationBuilder.DropTable(
                name: "snapshots");

            migrationBuilder.DropTable(
                name: "AspNetRoles");

            migrationBuilder.DropTable(
                name: "AspNetUsers");

            migrationBuilder.DropTable(
                name: "runs");

            migrationBuilder.DropTable(
                name: "timestamp_anchors");

            migrationBuilder.DropTable(
                name: "projects");

            migrationBuilder.DropTable(
                name: "capture_profile_versions");
        }
    }
}

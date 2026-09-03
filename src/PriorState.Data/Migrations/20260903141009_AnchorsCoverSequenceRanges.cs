using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PriorState.Data.Migrations
{
    /// <summary>
    /// Anchors stop being one-per-calendar-day and start covering a range of chain sequences.
    ///
    /// The unique index on CoversDateUtc was a trap. A capture starting at 23:59 is dated to the
    /// previous day, and if that day had already been anchored the entry could never be anchored:
    /// the scheduled job would try to insert a second anchor for the same date, hit the constraint,
    /// and fail again every hour. Allowing several anchors per day removes the trap and is what
    /// makes anchoring on demand possible.
    /// </summary>
    public partial class AnchorsCoverSequenceRanges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_timestamp_anchors_covers_date",
                table: "timestamp_anchors");

            migrationBuilder.DropColumn(
                name: "CoversDateUtc",
                table: "timestamp_anchors");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CoversFromUtc",
                table: "timestamp_anchors",
                type: "timestamptz",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CoversUntilUtc",
                table: "timestamp_anchors",
                type: "timestamptz",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            // Any anchor that already exists gets its real range recovered from the entries it
            // covers, rather than being left at the column default. This is an evidence table, and
            // a placeholder date in it would be a false statement about when a snapshot was made.
            //
            // The append-only trigger rejects UPDATE, including from the owner, so it is switched
            // off for the length of the backfill and switched straight back on. Schema migrations
            // are the one context where that is legitimate; nothing that feeds a hash is touched.
            migrationBuilder.Sql("ALTER TABLE timestamp_anchors DISABLE TRIGGER timestamp_anchors_append_only;");

            migrationBuilder.Sql("""
                UPDATE timestamp_anchors AS a
                   SET "CoversFromUtc"  = COALESCE(r.first_capture, a."CreatedAt"),
                       "CoversUntilUtc" = COALESCE(r.last_capture,  a."CreatedAt")
                  FROM (
                        SELECT "TimestampAnchorId" AS anchor_id,
                               MIN("CapturedAtUtc") AS first_capture,
                               MAX("CapturedAtUtc") AS last_capture
                          FROM snapshots
                         WHERE "TimestampAnchorId" IS NOT NULL
                         GROUP BY "TimestampAnchorId"
                       ) AS r
                 WHERE r.anchor_id = a."Id";
                """);

            // An anchor with no surviving snapshots cannot have a range recovered; fall back to
            // when it was created, which is at least a true statement about the anchor itself.
            migrationBuilder.Sql("""
                UPDATE timestamp_anchors
                   SET "CoversFromUtc"  = "CreatedAt",
                       "CoversUntilUtc" = "CreatedAt"
                 WHERE "CoversFromUtc" = '0001-01-01T00:00:00Z';
                """);

            migrationBuilder.Sql("ALTER TABLE timestamp_anchors ENABLE TRIGGER timestamp_anchors_append_only;");

            migrationBuilder.CreateIndex(
                name: "ix_timestamp_anchors_covers_from",
                table: "timestamp_anchors",
                column: "CoversFromUtc");

            migrationBuilder.CreateIndex(
                name: "ix_timestamp_anchors_first_sequence",
                table: "timestamp_anchors",
                column: "FirstChainSequence");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_timestamp_anchors_covers_from",
                table: "timestamp_anchors");

            migrationBuilder.DropIndex(
                name: "ix_timestamp_anchors_first_sequence",
                table: "timestamp_anchors");

            migrationBuilder.DropColumn(
                name: "CoversFromUtc",
                table: "timestamp_anchors");

            migrationBuilder.DropColumn(
                name: "CoversUntilUtc",
                table: "timestamp_anchors");

            migrationBuilder.AddColumn<DateOnly>(
                name: "CoversDateUtc",
                table: "timestamp_anchors",
                type: "date",
                nullable: false,
                defaultValue: new DateOnly(1, 1, 1));

            migrationBuilder.CreateIndex(
                name: "ix_timestamp_anchors_covers_date",
                table: "timestamp_anchors",
                column: "CoversDateUtc",
                unique: true);
        }
    }
}

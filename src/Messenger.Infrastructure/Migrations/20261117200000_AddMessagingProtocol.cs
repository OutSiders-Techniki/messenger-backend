using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Messenger.Infrastructure.Migrations;

public partial class AddMessagingProtocol : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<long>(name: "message_revision", table: "dialogs", type: "bigint", nullable: false, defaultValue: 0L);
        migrationBuilder.CreateTable(
            name: "message_receipts",
            columns: table => new
            {
                dialog_id = table.Column<Guid>(type: "uuid", nullable: false),
                client_message_id = table.Column<Guid>(type: "uuid", nullable: false),
                sender_id = table.Column<Guid>(type: "uuid", nullable: false),
                fingerprint = table.Column<byte[]>(type: "bytea", nullable: false),
                revision = table.Column<long>(type: "bigint", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_message_receipts", x => new { x.dialog_id, x.client_message_id });
                table.ForeignKey("fk_message_receipts_dialogs_dialog_id", x => x.dialog_id, "dialogs", "id", onDelete: ReferentialAction.Cascade);
                table.ForeignKey("fk_message_receipts_users_sender_id", x => x.sender_id, "users", "id", onDelete: ReferentialAction.Restrict);
            });
        migrationBuilder.CreateIndex(name: "ix_message_receipts_sender_id", table: "message_receipts", column: "sender_id");
        migrationBuilder.CreateTable(
            name: "pending_file_deletions",
            columns: table => new { storage_key = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false) },
            constraints: table => table.PrimaryKey("pk_pending_file_deletions", x => x.storage_key));
        migrationBuilder.CreateIndex(name: "ix_messages_dialog_id_owner_id_sent_at_client_message_id", table: "messages", columns: new[] { "dialog_id", "owner_id", "sent_at", "client_message_id" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "pending_file_deletions");
        migrationBuilder.DropTable(name: "message_receipts");
        migrationBuilder.DropIndex(name: "ix_messages_dialog_id_owner_id_sent_at_client_message_id", table: "messages");
        migrationBuilder.DropColumn(name: "message_revision", table: "dialogs");
    }
}

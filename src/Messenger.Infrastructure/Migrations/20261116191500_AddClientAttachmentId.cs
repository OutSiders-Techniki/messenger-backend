using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Messenger.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddClientAttachmentId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Индекс по внешнему ключу заменяем составным уникальным:
            // он покрывает и поиск по message_id.
            migrationBuilder.Sql("DROP INDEX IF EXISTS ix_attachments_message_id");

            // Сначала nullable: так колонку можно заполнить построчно.
            migrationBuilder.AddColumn<Guid>(
                name: "client_attachment_id",
                table: "attachments",
                type: "uuid",
                nullable: true);

            // Старые строки получают идентификатор самой строки: он уникален
            // в пределах копии, поэтому уникальный индекс создаётся без
            // ошибок.
            migrationBuilder.Sql(
                "UPDATE attachments SET client_attachment_id = id WHERE client_attachment_id IS NULL");

            migrationBuilder.Sql(
                "ALTER TABLE attachments ALTER COLUMN client_attachment_id SET NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_attachments_message_id_client_attachment_id",
                table: "attachments",
                columns: new[] { "message_id", "client_attachment_id" },
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_attachments_client_id",
                table: "attachments",
                sql: "client_attachment_id <> '00000000-0000-0000-0000-000000000000'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_attachments_message_id_client_attachment_id",
                table: "attachments");

            migrationBuilder.DropCheckConstraint(
                name: "ck_attachments_client_id",
                table: "attachments");

            migrationBuilder.DropColumn(
                name: "client_attachment_id",
                table: "attachments");

            migrationBuilder.CreateIndex(
                name: "ix_attachments_message_id",
                table: "attachments",
                column: "message_id");
        }
    }
}

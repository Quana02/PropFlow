using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PropFlow.Modules.AiChatbot.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddKnowledgeDocuments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "ai_chatbot");

            migrationBuilder.CreateTable(
                name: "knowledge_documents",
                schema: "ai_chatbot",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LatestRevision = table.Column<int>(type: "integer", nullable: false),
                    PublishedRevision = table.Column<int>(type: "integer", nullable: true),
                    Token = table.Column<Guid>(type: "uuid", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_knowledge_documents", x => x.Id);
                    table.CheckConstraint("ck_knowledge_revision", "\"LatestRevision\" > 0 AND (\"PublishedRevision\" IS NULL OR \"PublishedRevision\" BETWEEN 1 AND \"LatestRevision\")");
                });

            migrationBuilder.CreateTable(
                name: "knowledge_revisions",
                schema: "ai_chatbot",
                columns: table => new
                {
                    DocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Number = table.Column<int>(type: "integer", nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Content = table.Column<string>(type: "text", nullable: false),
                    Audience = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_knowledge_revisions", x => new { x.DocumentId, x.Number });
                    table.CheckConstraint("ck_knowledge_audience", "\"Audience\" IN ('RESIDENT','INTERNAL')");
                    table.CheckConstraint("ck_knowledge_content", "length(btrim(\"Title\")) BETWEEN 1 AND 200 AND length(btrim(\"Content\")) BETWEEN 1 AND 60000 AND \"Number\" > 0");
                    table.ForeignKey(
                        name: "FK_knowledge_revisions_knowledge_documents_DocumentId",
                        column: x => x.DocumentId,
                        principalSchema: "ai_chatbot",
                        principalTable: "knowledge_documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "knowledge_activity",
                schema: "ai_chatbot",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Revision = table.Column<int>(type: "integer", nullable: false),
                    Action = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ActorId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_knowledge_activity", x => x.Id);
                    table.ForeignKey(
                        name: "FK_knowledge_activity_knowledge_revisions_DocumentId_Revision",
                        columns: x => new { x.DocumentId, x.Revision },
                        principalSchema: "ai_chatbot",
                        principalTable: "knowledge_revisions",
                        principalColumns: new[] { "DocumentId", "Number" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_knowledge_activity_DocumentId_CreatedAt",
                schema: "ai_chatbot",
                table: "knowledge_activity",
                columns: new[] { "DocumentId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_knowledge_activity_DocumentId_Revision",
                schema: "ai_chatbot",
                table: "knowledge_activity",
                columns: new[] { "DocumentId", "Revision" });

            migrationBuilder.CreateIndex(
                name: "IX_knowledge_documents_UpdatedAt",
                schema: "ai_chatbot",
                table: "knowledge_documents",
                column: "UpdatedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "knowledge_activity",
                schema: "ai_chatbot");

            migrationBuilder.DropTable(
                name: "knowledge_revisions",
                schema: "ai_chatbot");

            migrationBuilder.DropTable(
                name: "knowledge_documents",
                schema: "ai_chatbot");
        }
    }
}

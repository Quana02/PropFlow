using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PropFlow.Modules.AiChatbot.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddResidentChatKnowledgeChunks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "knowledge_chunks",
                schema: "ai_chatbot",
                columns: table => new
                {
                    DocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Revision = table.Column<int>(type: "integer", nullable: false),
                    Number = table.Column<int>(type: "integer", nullable: false),
                    Content = table.Column<string>(type: "character varying(1200)", maxLength: 1200, nullable: false),
                    SearchText = table.Column<string>(type: "text", nullable: false),
                    Embedding = table.Column<float[]>(type: "real[]", nullable: true),
                    EmbeddingModel = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    AttemptedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_knowledge_chunks", x => new { x.DocumentId, x.Revision, x.Number });
                    table.CheckConstraint("ck_chunk_text", "length(\"Content\") BETWEEN 1 AND 1200 AND \"Number\" > 0");
                    table.ForeignKey(
                        name: "FK_knowledge_chunks_knowledge_revisions_DocumentId_Revision",
                        columns: x => new { x.DocumentId, x.Revision },
                        principalSchema: "ai_chatbot",
                        principalTable: "knowledge_revisions",
                        principalColumns: new[] { "DocumentId", "Number" },
                        onDelete: ReferentialAction.Restrict);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "knowledge_chunks",
                schema: "ai_chatbot");
        }
    }
}

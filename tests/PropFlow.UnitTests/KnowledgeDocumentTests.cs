using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using PropFlow.Modules.AiChatbot.Application;
using PropFlow.Modules.AiChatbot.Contracts;
using PropFlow.Modules.AiChatbot.Domain;
using PropFlow.Modules.AiChatbot.Infrastructure;
using PropFlow.Modules.AiChatbot.Infrastructure.Persistence;
using PropFlow.Modules.AiChatbot.Presentation;

namespace PropFlow.UnitTests;

[Trait("Feature", "FE-12-ScopeExtension-Knowledge")]
public sealed class KnowledgeDocumentTests
{
    [Theory]
    [InlineData("ADMIN", "MANAGE_INTERNAL_ACCOUNTS", true, true)]
    [InlineData("MANAGER", "MANAGE_OPERATIONS", true, true)]
    [InlineData("STAFF", "PERFORM_ASSIGNED_OPERATIONS", true, false)]
    [InlineData("RESIDENT", "USE_RESIDENT_SERVICES", false, false)]
    [InlineData("ACCOUNTANT", "MANAGE_FINANCE", false, false)]
    [InlineData("STAFF", "MANAGE_OPERATIONS", false, false)]
    [InlineData("ADMIN", "", false, false)]
    public void RolesAndPermissionsBothRequired(string role, string permission, bool read, bool manage)
    {
        var user = new ClaimsPrincipal(new ClaimsIdentity([new(ClaimTypes.Role, role), new("permission", permission)], "test"));
        Assert.Equal(read, KnowledgePolicies.CanRead(user)); Assert.Equal(manage, KnowledgePolicies.CanManage(user));
        Assert.False(KnowledgePolicies.CanRead(new ClaimsPrincipal(new ClaimsIdentity(user.Claims))));
    }

    [Theory]
    [InlineData("Create")][InlineData("Update")][InlineData("Publish")][InlineData("Unpublish")]
    public void AllWritesRequireManagementPolicy(string action)
    {
        var method = typeof(KnowledgeDocumentsController).GetMethod(action)!;
        Assert.Equal(KnowledgePolicies.Manage, method.GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>().Single().Policy);
        Assert.Equal(KnowledgePolicies.Read, typeof(KnowledgeDocumentsController).GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>().Single().Policy);
    }

    [Fact]
    public async Task DraftEditsNeverLeakThroughStaffOrResidentSource()
    {
        await using var db = Database(); var service = new KnowledgeDocuments(db); var actor = Guid.NewGuid();
        var id = await service.SaveAsync(null, new("Nội quy", "Nội dung công khai", "RESIDENT", null), actor, default);
        Assert.Null(await service.GetAsync(id, false, default));
        Assert.Null(await service.FindResidentSourceAsync(id, default));
        var draft = (await service.GetAsync(id, true, default))!;
        await service.SetPublicationAsync(id, draft.Document.Token, true, actor, default);
        var published = (await service.GetAsync(id, true, default))!;
        await service.SaveAsync(id, new("Quy trình nội bộ", "Bí mật nội bộ", "INTERNAL", published.Document.Token), actor, default);
        var staff = (await service.GetAsync(id, false, default))!;
        Assert.Equal("Nội dung công khai", staff.Revision.Content); Assert.Single(staff.History); Assert.Empty(staff.Activity);
        Assert.Equal("Nội dung công khai", (await service.FindResidentSourceAsync(id, default))!.Content);
        var latest = (await service.GetAsync(id, true, default))!;
        await service.SetPublicationAsync(id, latest.Document.Token, true, actor, default);
        Assert.Equal("Bí mật nội bộ", (await service.GetAsync(id, false, default))!.Revision.Content);
        Assert.Null(await service.FindResidentSourceAsync(id, default));
        latest = (await service.GetAsync(id, true, default))!;
        await service.SetPublicationAsync(id, latest.Document.Token, false, actor, default);
        Assert.Null(await service.GetAsync(id, false, default)); Assert.Equal(0, (await service.ListAsync(false, null, 1, default)).Total);
        Assert.Equal(2, await db.Revisions.CountAsync()); Assert.Equal(5, await db.Activities.CountAsync());
    }

    [Fact]
    public async Task StaleEditorsCannotOverwriteOrPublishNewerDrafts()
    {
        await using var db = Database(); var service = new KnowledgeDocuments(db); var actor = Guid.NewGuid();
        var id = await service.SaveAsync(null, new("Nội quy", "Bản đầu", "RESIDENT", null), actor, default);
        var token = (await service.GetAsync(id, true, default))!.Document.Token;
        await service.SaveAsync(id, new("Nội quy", "Bản mới", "RESIDENT", token), actor, default);
        await Assert.ThrowsAsync<KnowledgeConflictException>(() => service.SaveAsync(id, new("Lỗi", "Ghi đè", "INTERNAL", token), actor, default));
        await Assert.ThrowsAsync<KnowledgeConflictException>(() => service.SetPublicationAsync(id, token, true, actor, default));
        Assert.Null(await service.FindResidentSourceAsync(id, default));
    }

    [Fact]
    public async Task NormalizesUnicodeAndPaginatesOnServer()
    {
        await using var db = Database(); var service = new KnowledgeDocuments(db); var actor = Guid.NewGuid();
        for (var i = 0; i < 13; i++) await service.SaveAsync(null, new("Nội quy ".Normalize(System.Text.NormalizationForm.FormD) + i, "Hướng dẫn", "RESIDENT", null), actor, default);
        var page = await service.ListAsync(true, "Nội quy".Normalize(System.Text.NormalizationForm.FormD), 1, default);
        Assert.Equal(13, page.Total); Assert.Equal(12, page.Items.Count);
        Assert.Single((await service.ListAsync(true, "Nội quy", 2, default)).Items);
        Assert.Equal(0, (await service.ListAsync(false, null, 1, default)).Total);
    }

    [Theory]
    [InlineData("", "Nội dung", "RESIDENT")]
    [InlineData("Tên", "", "RESIDENT")]
    [InlineData("Tên", "Nội dung", "INVALID")]
    [InlineData("Tên", "Không\0hợp lệ", "INTERNAL")]
    public void InvalidDocumentsAreRejected(string title, string content, string audience) =>
        Assert.Throws<KnowledgeValidationException>(() => KnowledgeDocument.Create().Save(title, content, audience, Guid.NewGuid(), DateTimeOffset.UtcNow));

    private static AiChatbotDbContext Database() => new(new DbContextOptionsBuilder<AiChatbotDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
}

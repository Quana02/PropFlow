using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PropFlow.Modules.AiChatbot.Contracts;
using PropFlow.Modules.AiChatbot.Infrastructure.Persistence;

namespace PropFlow.IntegrationTests;

public sealed partial class AiAuthorizationHttpTests
{
    private const string Knowledge = "knowledge/documents";
    private const string Ask = "resident/chatbot/ask";
    private const string SyntheticJwt = "eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiJCNy10ZXN0In0.syntheticSignatureB7";
    private const string SyntheticKey = "b7_synthetic_api_key_canary";
    private const string SyntheticPassword = "b7_synthetic_password_canary";

    [Fact, Trait("Feature", "FE-12")]
    public async Task Knowledge_mutations_require_admin_or_manager_and_keep_drafts_tokens_and_audiences_isolated()
    {
        var title = "Nội quy phòng sinh hoạt B7";
        var seed = new SaveKnowledgeDocument(title, "Nội dung đã duyệt", "RESIDENT", null);
        var injected = new { title, content = seed.Content, audience = seed.Audience,
            createdBy = Staff.Id, revision = 950, publishedRevision = 950, publicationState = "PUBLISHED" };
        await Reject(HttpMethod.Post, Knowledge, injected, KnowledgeState, fixture.KnowledgeDenials.ToArray());
        await Reject(HttpMethod.Post, Knowledge, seed with { Audience = "PUBLIC" }, KnowledgeState, (Admin.Client, HttpStatusCode.BadRequest));

        var first = await Call<Guid>(Admin, HttpMethod.Post, Knowledge, injected);
        var second = await Call<Guid>(Manager, HttpMethod.Post, Knowledge, seed with { Title = "Nội quy khác B7" });
        var path = Knowledge + "/" + first;
        var otherPath = Knowledge + "/" + second;
        var draft = await ReadKnowledge(Admin, first);
        var otherDraft = await ReadKnowledge(Manager, second);
        Assert.Null(draft.Document.PublishedRevision);
        Assert.Equal(1, draft.Revision.Number);
        await Reject(HttpMethod.Get, path, null, KnowledgeState, (Staff.Client, HttpStatusCode.NotFound));

        var publish = new ChangeKnowledgePublication(draft.Document.Token);
        await Reject(HttpMethod.Post, path + "/publication", publish, KnowledgeState, fixture.KnowledgeDenials.ToArray());
        await Reject(HttpMethod.Post, path + "/publication", new ChangeKnowledgePublication(otherDraft.Document.Token),
            KnowledgeState, (Admin.Client, HttpStatusCode.Conflict));
        await OneKnowledgeWinner(HttpMethod.Post, path + "/publication", publish, Admin, HttpMethod.Post, path + "/publication", publish, Admin);
        var published = await ReadKnowledge(Staff, first);
        Assert.Equal(Guid.Empty, published.Document.Token);
        Assert.Equal(seed.Content, published.Revision.Content);
        Assert.Equal(1, published.Document.PublishedRevision);
        Assert.Empty(published.Activity);
        var staffListBefore = await Call<KnowledgeDocumentPage>(Staff, HttpMethod.Get, Knowledge);
        await Reject(HttpMethod.Post, path + "/publication", publish, KnowledgeState, (Admin.Client, HttpStatusCode.Conflict));

        var current = await ReadKnowledge(Manager, first);
        var edit = new { title, content = "Nháp B7 riêng", audience = "INTERNAL", expectedToken = current.Document.Token,
            createdBy = Staff.Id, publishedRevision = 2, revision = 99 };
        await Reject(HttpMethod.Put, path, edit, KnowledgeState, fixture.KnowledgeDenials.ToArray());
        await Reject(HttpMethod.Put, path, seed with { ExpectedToken = otherDraft.Document.Token }, KnowledgeState, (Manager.Client, HttpStatusCode.Conflict));
        await Reject(HttpMethod.Put, path, seed with { ExpectedToken = draft.Document.Token }, KnowledgeState, (Manager.Client, HttpStatusCode.Conflict));
        await OneKnowledgeWinner(HttpMethod.Put, path, edit, Manager, HttpMethod.Put, path, edit, Manager);
        var edited = await ReadKnowledge(Admin, first);
        Assert.Equal(2, edited.Revision.Number);
        Assert.Equal("INTERNAL", edited.Revision.Audience);
        var stillPublished = await ReadKnowledge(Staff, first);
        Assert.Equal(published.Document, stillPublished.Document);
        var staffListAfter = await Call<KnowledgeDocumentPage>(Staff, HttpMethod.Get, Knowledge);
        Assert.Equal(staffListBefore.Items.ToArray(), staffListAfter.Items.ToArray());
        Assert.Equal("RESIDENT", stillPublished.Revision.Audience);
        Assert.Equal(seed.Content, Assert.Single(stillPublished.History).Content);

        // Use the second document to prove the complementary positive role for every route.
        await Call<Guid>(Admin, HttpMethod.Put, otherPath, seed with { Audience = "INTERNAL", ExpectedToken = otherDraft.Document.Token });
        var editedOther = await ReadKnowledge(Manager, second);
        await Call<Guid>(Manager, HttpMethod.Post, otherPath + "/publication", new ChangeKnowledgePublication(editedOther.Document.Token));
        Assert.Equal("INTERNAL", (await ReadKnowledge(Staff, second)).Revision.Audience);
        var publishedOther = await ReadKnowledge(Admin, second);
        await Call<Guid>(Admin, HttpMethod.Post, otherPath + "/unpublish", new ChangeKnowledgePublication(publishedOther.Document.Token));

        var unpublish = new ChangeKnowledgePublication(edited.Document.Token);
        await Reject(HttpMethod.Post, path + "/unpublish", unpublish, KnowledgeState, fixture.KnowledgeDenials.ToArray());
        await Reject(HttpMethod.Post, path + "/unpublish", publish, KnowledgeState, (Manager.Client, HttpStatusCode.Conflict));
        await OneKnowledgeWinner(HttpMethod.Post, path + "/unpublish", unpublish, Manager, HttpMethod.Post, path + "/unpublish", unpublish, Manager);
        await Reject(HttpMethod.Post, path + "/unpublish", unpublish, KnowledgeState, (Manager.Client, HttpStatusCode.Conflict));
        await Reject(HttpMethod.Get, path, null, KnowledgeState, (Staff.Client, HttpStatusCode.NotFound));
        var withdrawn = await ReadKnowledge(Admin, first);
        Assert.Null(withdrawn.Document.PublishedRevision);
        Assert.Equal(2, withdrawn.History.Count);
        using var scope = Database.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AiChatbotDbContext>();
        var activities = await db.Activities.AsNoTracking().Where(x => x.DocumentId == first).ToArrayAsync();
        Assert.Equal(4, activities.Length);
        foreach (var (action, actor) in new[] { ("CREATE", Admin.Id), ("EDIT", Manager.Id), ("PUBLISH", Admin.Id), ("UNPUBLISH", Manager.Id) })
            Assert.Equal(actor, Assert.Single(activities, x => x.Action == action).ActorId);
        Assert.Equal(Manager.Id, await db.Revisions.Where(x => x.DocumentId == first && x.Number == 2).Select(x => x.CreatedBy).SingleAsync());
        var otherAfter = await ReadKnowledge(Admin, second);
        Assert.Equal(2, otherAfter.Revision.Number);
        Assert.Null(otherAfter.Document.PublishedRevision);

        // A competing edit and publication may win in either order, but cannot both use the same token.
        var editAgain = seed with { ExpectedToken = withdrawn.Document.Token, Content = "Bản mới cạnh tranh" };
        await OneKnowledgeWinner(HttpMethod.Put, path, editAgain, Manager, HttpMethod.Post, path + "/publication",
            new ChangeKnowledgePublication(withdrawn.Document.Token), Admin);
        var settled = await ReadKnowledge(Admin, first);
        Assert.True((settled.Revision.Number, settled.Document.PublishedRevision) is (3, null) or (2, 2));
    }

    private Task<KnowledgeDocumentDetail> ReadKnowledge(AiAuthorizationFixture.Actor actor, Guid id) =>
        Call<KnowledgeDocumentDetail>(actor, HttpMethod.Get, Knowledge + "/" + id);

    private async Task OneKnowledgeWinner(HttpMethod method, string path, object input, AiAuthorizationFixture.Actor actor,
        HttpMethod secondMethod, string secondPath, object secondInput, AiAuthorizationFixture.Actor secondActor)
    {
        var responses = await Task.WhenAll(Send(actor.Client, method, path, input), Send(secondActor.Client, secondMethod, secondPath, secondInput));
        using var first = responses[0];
        using var second = responses[1];
        await Expect(responses.OrderBy(x => (int)x.StatusCode).First(), HttpStatusCode.OK);
        await Expect(responses.OrderBy(x => (int)x.StatusCode).Last(), HttpStatusCode.Conflict);
    }

    private async Task<string> KnowledgeState()
    {
        using var scope = Database.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AiChatbotDbContext>();
        return JsonSerializer.Serialize(new
        {
            Documents = await Rows(db.Documents), Revisions = await Rows(db.Revisions),
            Audit = await Rows(db.Activities), Chunks = await Rows(db.Chunks)
        });
    }
}

using System.Net;
using Microsoft.AspNetCore.Components;
using PropFlow.Modules.ServiceRequests.Contracts;
using PropFlow.Web.Client.Features.Resident.Portal.Services;

namespace PropFlow.UnitTests;

public sealed partial class FacilityContinuousConversationTests
{
    [Fact]
    public Task Reusing_a_stale_description_after_resident_adds_items_is_not_reported_as_an_updated_draft() => WithPage(async f =>
    {
        f.Player.LiveSupported = true; await f.InitializeVoice();
        const string description = "Đăng ký chuyển đồ ra ngoài căn hộ ngày 11/12/2026.";
        await f.Page.OnLiveUserTurn(f.Epoch, 1, description);
        f.Transport.DraftReview = new(true, [new("moving_items", "Bạn muốn chuyển những đồ gì?")]);
        await f.Page.OnLivePrepareRequest(f.Epoch, "initial", description);
        await f.Page.OnLiveUserTurn(f.Epoch, 2, "Gồm quạt, PC và tủ");
        var result = await f.Page.OnLivePrepareRequest(f.Epoch, "stale-update", description);
        Assert.Contains("error", System.Text.Json.JsonSerializer.Serialize(result));
        var key = f.Get<Guid?>("draftId");
        var replacement = await f.Page.OnLivePrepareRequest(f.Epoch, "incorrect-new", "Chuyển toàn bộ đồ cá nhân ra khỏi chung cư.", true);
        Assert.Contains("error", System.Text.Json.JsonSerializer.Serialize(replacement));
        Assert.Equal(key, f.Get<Guid?>("draftId"));
    });
    [Fact]
    public Task Moving_supplements_preserve_specific_items_in_the_visible_draft_without_requiring_categories() => WithPage(async f =>
    {
        f.Player.LiveSupported = true; await f.InitializeVoice();
        const string initial = "Đăng ký chuyển đồ ra ngoài căn hộ ngày 11/12/2026.";
        await f.Page.OnLiveUserTurn(f.Epoch, 1, initial);
        f.Transport.DraftReview = new(true, [new("moving_items", "Bạn muốn chuyển những đồ gì?")]);
        await f.Page.OnLivePrepareRequest(f.Epoch, "initial", initial);
        var before = f.Get<Guid?>("draftId")!.Value;
        await f.Page.OnLiveUserTurn(f.Epoch, 2, "Gồm quạt, PC và tủ");
        f.Transport.DraftReview = new(true, []);
        await f.Page.OnLiveUpdateRequest(f.Epoch, "items", before.ToString());
        var current = f.Get<Guid?>("draftId")!.Value;
        Assert.NotEqual(before, current);
        Assert.Contains(initial, f.Get<string>("draftDescription"));
        Assert.Contains("quạt, PC và tủ", f.Get<string>("draftDescription"));
        Assert.Empty(f.Get<ResidentServiceRequestDraftReview>("draftReview").MissingInformation);
        await f.Page.OnLiveUpdateRequest(f.Epoch, "duplicate", current.ToString());
        Assert.Equal(current, f.Get<Guid?>("draftId"));
        await f.Page.OnLiveUserTurn(f.Epoch, 3, "Có cả đồ nội thất và đồ gia dụng");
        await f.Page.OnLiveUpdateRequest(f.Epoch, "category", current.ToString());
        Assert.Contains("quạt, PC và tủ", f.Get<string>("draftDescription"));
        Assert.Contains("đồ gia dụng", f.Get<string>("draftDescription"));
        await f.Page.OnLiveUserTurn(f.Epoch, 4, "Gửi đơn này");
        await f.Page.OnLiveSubmitRequest(f.Epoch, f.Get<Guid?>("draftId")!.Value.ToString());
        Assert.Contains("quạt, PC và tủ", Assert.Single(f.Transport.Submissions).Description);
    });
    [Fact]
    public Task Continued_input_and_a_combined_edit_send_keep_all_facts_and_require_fresh_confirmation() => WithPage(async f =>
    {
        f.Player.LiveSupported = true; await f.InitializeVoice();
        const string initial = "Chuyển quạt ra ngoài căn hộ.";
        await f.Page.OnLiveUserTurn(f.Epoch, 1, initial);
        await f.Page.OnLivePrepareRequest(f.Epoch, "initial", initial);
        await f.Page.OnLiveUserTurn(f.Epoch, 1, initial + " Có cả tủ nữa.");
        await f.Page.OnLiveUpdateRequest(f.Epoch, "continued", f.Get<Guid?>("draftId")!.Value.ToString());
        Assert.Contains("Bổ sung: Có cả tủ nữa.", f.Get<string>("draftDescription"));
        await f.Page.OnLiveUserTurn(f.Epoch, 2, "Thêm PC nữa rồi gửi đơn này");
        await f.Page.OnLiveSubmitRequest(f.Epoch, f.Get<Guid?>("draftId")!.Value.ToString());
        Assert.Empty(f.Transport.Submissions);
        await f.Page.OnLiveUpdateRequest(f.Epoch, "pc", f.Get<Guid?>("draftId")!.Value.ToString());
        await f.Page.OnLiveSubmitRequest(f.Epoch, f.Get<Guid?>("draftId")!.Value.ToString());
        Assert.Empty(f.Transport.Submissions);
        await f.Page.OnLiveUserTurn(f.Epoch, 3, "Gửi đơn này");
        await f.Page.OnLiveSubmitRequest(f.Epoch, f.Get<Guid?>("draftId")!.Value.ToString());
        var sent = Assert.Single(f.Transport.Submissions);
        Assert.Contains(initial, sent.Description); Assert.Contains("tủ", sent.Description); Assert.Contains("PC", sent.Description);
    });
    [Theory]
    [InlineData("Gửi đơn này cho BQL", true)]
    [InlineData("Bạn gửi yêu cầu này giúp tôi nhé", true)]
    [InlineData("Ok, gửi đi", true)]
    [InlineData("Nộp đơn này", true)]
    [InlineData("Gửi luôn", true)]
    [InlineData("Ok", false)]
    [InlineData("Chưa gửi đơn này", false)]
    [InlineData("Đừng gửi, sửa lại vị trí", false)]
    [InlineData("Gửi đơn này như thế nào", false)]
    [InlineData("Có thể gửi đơn này không", false)]
    [InlineData("Câu 'gửi đơn này' nghĩa là gì", false)]
    [InlineData("Nếu tôi nói gửi đơn này", false)]
    [InlineData("Nội quy nói phải gửi đơn", false)]
    [InlineData("Gửi đơn này vào ngày mai", false)]
    [InlineData("Thêm một PC nữa rồi gửi đơn này", false)]
    [InlineData("Khoan, đừng gửi đơn", false)]
    public void Sending_a_draft_requires_a_clear_action_instead_of_keywords(string text, bool accepted) =>
        Assert.Equal(accepted, AssistantSubmissionIntent.IsExplicitSend(text));

    [Fact, Trait("UseCase", "FE-05.1")]
    public Task Assistant_previews_then_sends_only_on_a_new_explicit_confirmation_and_reports_the_real_receipt() => WithPage(async f =>
    {
        f.Player.LiveSupported = true; await f.InitializeVoice(); var epoch = f.Epoch;
        await f.Page.OnLiveUserTurn(epoch, 1, "Vòi nước bếp bị rò, tạo rồi gửi đơn giúp tôi");
        await f.Page.OnLivePrepareRequest(epoch, "prepare", "Vòi nước trong bếp bị rò liên tục. Nhờ BQL hỗ trợ kiểm tra.");
        var id = f.Get<Guid?>("draftId")!.Value;
        Assert.Empty(f.Transport.Submissions);
        var html = WebUtility.HtmlDecode(f.Html()); Assert.Contains("Cùng xem lại trước khi gửi", html); Assert.Contains("Vòi nước trong bếp", html);
        await f.Export("request");
        await f.Page.OnLiveSubmitRequest(epoch, id.ToString()); Assert.Empty(f.Transport.Submissions);
        await f.Page.OnLiveUserTurn(epoch, 2, "Chưa gửi đơn này");
        await f.Page.OnLiveSubmitRequest(epoch, id.ToString()); Assert.Empty(f.Transport.Submissions);
        await f.Page.OnLiveUserTurn(epoch, 3, "Gửi đơn này như thế nào");
        await f.Page.OnLiveSubmitRequest(epoch, id.ToString()); Assert.Empty(f.Transport.Submissions);
        await f.Page.OnLiveUserTurn(epoch, 4, "Gửi đơn này cho BQL");
        await f.Page.OnLiveSubmitRequest(epoch, id.ToString());
        var sent = Assert.Single(f.Transport.Submissions);
        Assert.Equal(id, sent.SubmissionId); Assert.Null(sent.CategoryCode); Assert.Null(sent.PriorityCode);
        Assert.Contains(f.Transport.Receipt.RequestNumber, f.Html());
        Assert.Contains("Đã gửi đến Ban quản lý", WebUtility.HtmlDecode(f.Html()));
        await f.Page.OnLiveSubmitRequest(epoch, id.ToString()); Assert.Single(f.Transport.Submissions);
    });

    [Theory, Trait("UseCase", "FE-05.1")]
    [InlineData(true)] [InlineData(false)]
    public Task Incomplete_or_unavailable_review_does_not_send_an_automatic_request(bool available) => WithPage(async f =>
    {
        f.Player.LiveSupported = true; await f.InitializeVoice();
        f.Transport.DraftReview = new(available, available ? [new("vehicle_plate", "Biển số xe")] : [], Notice: available ? null : "Chưa kiểm tra được");
        await f.Page.OnLiveUserTurn(f.Epoch, 1, "Tôi muốn đăng ký xe máy");
        await f.Page.OnLivePrepareRequest(f.Epoch, "prepare", "Tôi muốn đăng ký xe máy màu đen.");
        await f.Page.OnLiveUserTurn(f.Epoch, 2, "Gửi đơn này");
        await f.Page.OnLiveSubmitRequest(f.Epoch, f.Get<Guid?>("draftId")!.Value.ToString());
        Assert.Empty(f.Transport.Submissions);
        Assert.Contains("Mở form tạo yêu cầu", WebUtility.HtmlDecode(f.Html()));
    });

    [Fact, Trait("UseCase", "FE-05.1")]
    public Task Edited_drafts_invalidate_old_ids_reviews_and_confirmation() => WithPage(async f =>
    {
        f.Player.LiveSupported = true; await f.InitializeVoice(); var epoch = f.Epoch;
        await f.Page.OnLiveUserTurn(epoch, 1, "Sửa vòi nước trong bếp");
        await f.Page.OnLivePrepareRequest(epoch, "one", "Vòi nước trong bếp bị rò liên tục.");
        var old = f.Get<Guid?>("draftId")!.Value;
        await f.Page.OnLiveUserTurn(epoch, 2, "Gửi đơn này");
        await f.CallSync("EditDraft", new ChangeEventArgs { Value = "Vòi nước trong phòng tắm bị rò liên tục." });
        Assert.NotEqual(old, f.Get<Guid?>("draftId"));
        await f.Page.OnLiveSubmitRequest(epoch, old.ToString());
        await f.Page.OnLiveSubmitRequest(epoch, f.Get<Guid?>("draftId")!.Value.ToString());
        Assert.Empty(f.Transport.Submissions);
        await f.Call("ReviewEditedDraft");
        await f.Page.OnLiveUserTurn(epoch, 3, "Gửi đơn này cho BQL");
        await f.Page.OnLiveSubmitRequest(epoch, f.Get<Guid?>("draftId")!.Value.ToString());
        Assert.Contains("phòng tắm", Assert.Single(f.Transport.Submissions).Description);
    });

    [Fact, Trait("Feature", "FE-12")]
    public Task Documents_appear_in_the_right_panel_as_escaped_published_sources() => WithPage(async f =>
    {
        f.Player.LiveSupported = true; await f.InitializeVoice();
        var result = await f.Page.OnLiveDocuments(f.Epoch, "docs", "Nội quy sử dụng tiện ích"); Assert.NotNull(result);
        var html = f.Html(); Assert.Contains("Tài liệu chung cư", WebUtility.HtmlDecode(html));
        Assert.Contains("&lt;script&gt;", html); Assert.DoesNotContain("<script>", html);
        Assert.Contains("Phiên bản 2", WebUtility.HtmlDecode(html));
        Assert.Empty(f.Transport.Submissions);
        await f.Export("documents");
        await f.Call("ReadAssistantSource", f.Transport.Documents.Sources[0]);
        await f.CallSync("StateHasChanged");
        Assert.Contains("Toàn văn nội quy", WebUtility.HtmlDecode(f.Html()));
        Assert.DoesNotContain("<script>", f.Html());
    });
    [Fact, Trait("UseCase", "FE-05.1")]
    public Task A_failed_send_keeps_the_same_draft_key_and_does_not_show_a_success_receipt() => WithPage(async f =>
    {
        f.Player.LiveSupported = true; await f.InitializeVoice();
        const string description = "Vòi nước trong bếp bị rò liên tục.";
        await f.Page.OnLiveUserTurn(f.Epoch, 1, "Tạo đơn sửa vòi nước trong bếp");
        await f.Page.OnLivePrepareRequest(f.Epoch, "prepare", description);
        var key = f.Get<Guid?>("draftId")!.Value;
        await f.Page.OnLivePrepareRequest(f.Epoch, "prepare-again", description);
        Assert.Equal(key, f.Get<Guid?>("draftId"));
        f.Transport.RejectSubmission = true;
        await f.Page.OnLiveUserTurn(f.Epoch, 2, "Gửi đơn này");
        await f.Page.OnLiveSubmitRequest(f.Epoch, key.ToString());
        Assert.Null(f.Get<ResidentServiceRequestCreatedResponse?>("submittedRequest"));
        Assert.DoesNotContain(f.Transport.Receipt.RequestNumber, f.Html());
        f.Transport.RejectSubmission = false;
        await f.Page.OnLiveSubmitRequest(f.Epoch, key.ToString());
        Assert.Equal(2, f.Transport.Submissions.Count);
        Assert.All(f.Transport.Submissions, sent => Assert.Equal(key, sent.SubmissionId));
        Assert.NotNull(f.Get<ResidentServiceRequestCreatedResponse?>("submittedRequest"));
    });
}

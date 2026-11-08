using PropFlow.Web.Client.Shared.Feedback;
namespace PropFlow.UnitTests;
public sealed class ToastOperationTests
{
    [Fact]
    public void Navigation_away_and_back_does_not_revalidate_old_operation()
    {
        var service = new ToastService(); using var owner = new ToastLifetime();
        var old = service.BeginOperation(owner);
        service.NavigationChanged("other"); service.NavigationChanged("original");
        old.Publish(() => service.Error("Cũ"));
        Assert.False(old.IsCurrent); Assert.Empty(service.Messages);
    }
    [Fact]
    public void Closing_and_reopening_dialog_invalidates_only_its_old_operation()
    {
        var service = new ToastService(); using var page = new ToastLifetime();
        using var dialog = new ToastLifetime(); using var otherDialog = new ToastLifetime();
        var old = service.BeginOperation(page, dialog); var independent = service.BeginOperation(page, otherDialog);
        dialog.Invalidate();
        old.Publish(() => service.Error("Dialog cũ"));
        independent.Publish(() => service.Info("Dialog khác vẫn hợp lệ"));
        var next = service.BeginOperation(page, dialog); next.Publish(() => service.Success("Dialog mới"));
        Assert.Equal(new[] { "Dialog khác vẫn hợp lệ", "Dialog mới" }, service.Messages.Select(x => x.Message));
    }
    [Fact]
    public void Disposed_owner_cannot_start_a_fresh_valid_operation_on_destination()
    {
        var service = new ToastService(); var owner = new ToastLifetime();
        owner.Dispose(); service.NavigationChanged("destination");
        var lateChildCallback = service.BeginOperation(owner);
        lateChildCallback.Publish(() => service.Success("Cũ"));
        Assert.Empty(service.Messages);
    }
    [Fact]
    public void Intentional_redirect_success_survives_navigation_and_origin_disposal()
    {
        var service = new ToastService(); var owner = new ToastLifetime();
        var operation = service.BeginOperation(owner);
        operation.Publish(() => service.SuccessAfterNavigation("Thành công", "Đăng nhập lại", "login"));
        owner.Dispose(); service.NavigationChanged("login");
        var message = Assert.Single(service.Messages);
        Assert.Equal("Thành công", message.Title); Assert.Equal(3000, message.DurationMs);
        operation.Publish(() => service.Error("Callback cũ"));
        Assert.Equal(message, Assert.Single(service.Messages));
    }
    [Fact]
    public void Stale_operation_cannot_overwrite_new_redirect_success()
    {
        var service = new ToastService(); var old = service.BeginOperation();
        service.NavigationChanged("new");
        var current = service.BeginOperation();
        current.Publish(() => service.SuccessAfterNavigation("Mới", "Kết quả mới", "login"));
        old.Publish(() => service.SuccessAfterNavigation("Cũ", "Kết quả cũ", "wrong-page"));
        service.NavigationChanged("login");
        Assert.Equal("Mới", Assert.Single(service.Messages).Title);
    }
    [Fact]
    public void Valid_persistent_publication_is_retained_when_later_operation_becomes_stale()
    {
        var service = new ToastService(); var operation = service.BeginOperation();
        operation.Publish(() => service.Show("Quan trọng", severity: "error", durationMs: 0));
        var message = Assert.Single(service.Messages);
        service.NavigationChanged("new");
        operation.Publish(() => service.Error("Cũ"));
        Assert.Equal(message, Assert.Single(service.Messages));
    }
}

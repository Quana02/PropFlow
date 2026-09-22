namespace PropFlow.Web.Client.Shared.Forms;

public sealed record PropFlowSelectOption(string Value, string Label, bool Disabled = false);

public static class PropFlowSelectOptions
{
    public static readonly PropFlowSelectOption[] UnavailableBuilding = [new(string.Empty, "Chọn tòa nhà")];
}

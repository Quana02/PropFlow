using Microsoft.AspNetCore.Components.Forms;
using PropFlow.Web.Client.Services.Api;

namespace PropFlow.Web.Client.Shared.Forms;

// Whitelisted API field names keep server validation beside the corresponding form input.
public sealed class ApiValidationMessages : IDisposable
{
    private readonly EditContext context;
    private readonly ValidationMessageStore messages;
    private readonly Dictionary<string, string> fields;
    public ApiValidationMessages(EditContext context, params (string ApiField, string FormField)[] fields)
    {
        this.context = context;
        messages = new(context);
        this.fields = fields.ToDictionary(item => item.ApiField, item => item.FormField, StringComparer.OrdinalIgnoreCase);
        context.OnFieldChanged += FieldChanged;
    }
    public void Apply(ApiResult result)
    {
        messages.Clear();
        if (result.ValidationErrors is not null)
            foreach (var entry in result.ValidationErrors)
                if (fields.TryGetValue(entry.Key.Split('.').Last(), out var field))
                    messages.Add(new FieldIdentifier(context.Model, field), entry.Value);
        context.NotifyValidationStateChanged();
    }
    public void Clear() { messages.Clear(); context.NotifyValidationStateChanged(); }
    private void FieldChanged(object? sender, FieldChangedEventArgs args)
    { messages.Clear(args.FieldIdentifier); context.NotifyValidationStateChanged(); }
    public void Dispose() => context.OnFieldChanged -= FieldChanged;
}

using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using Microsoft.Extensions.Logging.Abstractions;
using PropFlow.Web.Client.Features.Finance.Services;
using PropFlow.Web.Client.Features.Resident.Portal.Components;
using PropFlow.Web.Client.Features.Resident.Portal.Models;
using PropFlow.Web.Client.Features.Resident.Shared.Models;
using PropFlow.Web.Client.Services.Api;

namespace PropFlow.UnitTests;

public sealed class ResidentHeaderFinanceTests
{
    [Fact]
    public async Task Menu_shows_real_outstanding_for_current_apartment_and_excludes_rent()
    {
        var transport = new Transport();
        using var header = Header(transport);
        await Load(header);
        Assert.Equal(1101000m, Field<decimal?>(header, "serviceOutstanding"));
        Assert.Equal("/api/v1/resident/billing/invoices", transport.RequestedPath);
    }

    [Fact]
    public async Task Failure_is_unknown_rather_than_zero_and_reopening_retries()
    {
        var transport = new Transport { Fail = true };
        using var header = Header(transport);
        await Load(header);
        Assert.Null(Field<decimal?>(header, "serviceOutstanding"));
        Assert.NotNull(Field<string?>(header, "financeError"));
        transport.Fail = false;
        await Load(header);
        Assert.Equal(1101000m, Field<decimal?>(header, "serviceOutstanding"));
    }

    [Fact]
    public async Task Late_response_is_ignored_if_current_apartment_changes()
    {
        var transport = new Transport { Gate = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        using var header = Header(transport);
        var pending = Load(header);
        Set(header, "ProfileContext", Context("OTHER"));
        transport.Gate.SetResult();
        await pending;
        Assert.Null(Field<decimal?>(header, "serviceOutstanding"));
    }

    private static ResidentHeader Header(Transport transport)
    {
        var header = new ResidentHeader();
        Set(header, "Finance", new FinanceCycleApi(new AuthenticatedApiClient(new HttpClient(transport)
            { BaseAddress = new("https://resident.test/") }, NullLogger<ApiClient>.Instance)));
        Set(header, "ProfileContext", Context("DEMO-1801"));
        return header;
    }
    private static ResidentPortalProfileContext Context(string unit) => new(new ResidentDetailResponse(
        Guid.NewGuid(), "CD-TEST", "Cư dân", null, null, null, null, null, null, null, null, null, null,
        "ACTIVE", true, [new OwnershipItem(Guid.NewGuid(), Guid.NewGuid(), unit, 18, DateOnly.FromDateTime(DateTime.Today.AddDays(-1)), null)], []), false, null);
    private static void Set(object component, string name, object value) => component.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.SetValue(component, value);
    private static T Field<T>(object component, string name) => (T)component.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(component)!;
    private static Task Load(ResidentHeader header) => (Task)typeof(ResidentHeader).GetMethod("LoadFinanceAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(header, null)!;
    private sealed class Transport : HttpMessageHandler
    {
        public bool Fail;
        public string? RequestedPath;
        public TaskCompletionSource? Gate;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            RequestedPath = request.RequestUri!.AbsolutePath;
            if (Gate is not null) await Gate.Task;
            if (Fail) return new(HttpStatusCode.ServiceUnavailable) { Content = JsonContent.Create(new { title = "Tải hóa đơn thất bại" }) };
            HouseholdInvoice Invoice(decimal amount, string apartment = "DEMO-1801", string type = "SERVICE_REQUEST") =>
                new(Guid.NewGuid(), "DV-TEST", apartment, "Cư dân", new(2026, 11, 1), null, amount, 0, amount, InvoiceType: type);
            return new(HttpStatusCode.OK) { Content = JsonContent.Create(new[]
            { Invoice(1000000), Invoice(100000), Invoice(1000), Invoice(0), Invoice(5000000, type: "MONTHLY_RENTAL"), Invoice(9000000, apartment: "OTHER") }) };
        }
    }
}

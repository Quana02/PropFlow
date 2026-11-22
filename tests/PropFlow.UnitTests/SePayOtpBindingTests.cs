using System.Reflection;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.RenderTree;
using PropFlow.Web.Client.Features.Administration.Components;
using PropFlow.Web.Client.Features.Administration.Models;

namespace PropFlow.UnitTests;

#pragma warning disable BL0006
public sealed class SePayOtpBindingTests
{
    [Theory]
    [InlineData(typeof(PaymentDestinationSettings), "otpError", null)]
    [InlineData(typeof(PaymentDestinationSettings), "otpError", "Mã xác minh không đúng")]
    [InlineData(typeof(SePayOAuthConnect), "message", null)]
    [InlineData(typeof(SePayOAuthConnect), "message", "Mã xác minh đã hết hạn")]
    public void Otp_dialog_receives_error_value_instead_of_field_name(Type parent, string field, string? error)
    {
        var component = (ComponentBase)Activator.CreateInstance(parent)!;
        Set(component, "challenge", new PrivilegedActionChallengeResult(Guid.NewGuid(), DateTimeOffset.UtcNow.AddMinutes(5), DateTimeOffset.UtcNow, "a***@example.invalid"));
        Set(component, field, error);
        Assert.Equal(error, Parameters(component)["Error"]);
    }

    [Fact]
    public void Destination_dialog_receives_the_pending_action_value()
    {
        var component = new PaymentDestinationSettings();
        Set(component, "challenge", new PrivilegedActionChallengeResult(Guid.NewGuid(), DateTimeOffset.UtcNow.AddMinutes(5), DateTimeOffset.UtcNow, "a***@example.invalid"));
        Set(component, "pendingAction", "DEACTIVATE");
        Assert.Equal("DEACTIVATE", Parameters(component)["Action"]);
    }

    private static void Set(object component, string name, object? value) =>
        component.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(component, value);

    private static Dictionary<string, object> Parameters(ComponentBase component)
    {
        using var builder = new RenderTreeBuilder();
        component.GetType().GetMethod("BuildRenderTree", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(component, [builder]);
        var frames = builder.GetFrames();
        for (var i = 0; i < frames.Count; i++)
            if (frames.Array[i].FrameType == RenderTreeFrameType.Component && frames.Array[i].ComponentType == typeof(PaymentDestinationOtpDialog))
                return frames.Array.Skip(i + 1).TakeWhile(f => f.FrameType == RenderTreeFrameType.Attribute)
                    .ToDictionary(f => f.AttributeName, f => f.AttributeValue);
        throw new InvalidOperationException("OTP dialog was not rendered.");
    }
}

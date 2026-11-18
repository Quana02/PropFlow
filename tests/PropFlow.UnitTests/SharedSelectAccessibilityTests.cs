using System.Reflection;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using PropFlow.Web.Client.Shared.Forms;

namespace PropFlow.UnitTests;

public sealed class SharedSelectAccessibilityTests
{
    [Fact]
    public async Task Keyboard_JS_callback_closes_and_renders_the_listbox_even_when_the_value_does_not_change()
    {
        var capture=new Capture();
        await using var services=new ServiceCollection().AddLogging().AddSingleton<IJSRuntime,NoJavaScript>()
            .AddSingleton<IComponentActivator>(capture).BuildServiceProvider();
        await using var renderer=new HtmlRenderer(services,services.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async()=>
        {
            var root=await renderer.RenderComponentAsync<PropFlowSelect>(ParameterView.FromDictionary(new Dictionary<string,object?>
            { [nameof(PropFlowSelect.Value)]="a",[nameof(PropFlowSelect.Options)]=new PropFlowSelectOption[]{new("a","A"),new("b","B")} }));
            Assert.Contains("aria-expanded=\"false\"",root.ToHtmlString());
            // Arrange the open UI state; invoke the same public method called by the Enter-key JS listener.
            typeof(PropFlowSelect).GetField("open",BindingFlags.NonPublic|BindingFlags.Instance)!.SetValue(capture.Select,true);
            typeof(ComponentBase).GetMethod("StateHasChanged",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(capture.Select,null);
            Assert.Contains("role=\"listbox\"",root.ToHtmlString());
            Assert.Contains("aria-selected=\"true\"",root.ToHtmlString());Assert.Contains("aria-selected=\"false\"",root.ToHtmlString());
            await capture.Select!.SelectActiveFromKeyboard();
            Assert.Contains("aria-expanded=\"false\"",root.ToHtmlString());Assert.DoesNotContain("role=\"listbox\"",root.ToHtmlString());
        });
    }
    private sealed class Capture:IComponentActivator
    {
        public PropFlowSelect? Select;
        public IComponent CreateInstance(Type type){var instance=(IComponent)Activator.CreateInstance(type)!;if(instance is PropFlowSelect select)Select=select;return instance;}
    }
    private sealed class NoJavaScript:IJSRuntime
    {
        public ValueTask<T> InvokeAsync<T>(string identifier,object?[]? args)=>throw new NotSupportedException("Static render must not invoke JavaScript.");
        public ValueTask<T> InvokeAsync<T>(string identifier,CancellationToken ct,object?[]? args)=>InvokeAsync<T>(identifier,args);
    }
}

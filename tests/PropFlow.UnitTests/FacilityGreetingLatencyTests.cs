using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.JSInterop;
using PropFlow.Web.Client.Features.Facility.Services;
using PropFlow.Web.Client.Features.Resident.Portal.Pages;
using PropFlow.Web.Client.Services.Api;

namespace PropFlow.UnitTests;

public sealed class FacilityGreetingLatencyTests
{
    [Fact]
    public async Task Welcome_plays_a_bundled_recording_without_waiting_for_the_speech_API()
    {
        using var handler=new OfflineSpeech();using var http=new HttpClient(handler){BaseAddress=new("https://facility.test/")};
        var capture=new Capture();var js=new Browser();
        await using var services=new ServiceCollection().AddLogging().AddSingleton(new AuthenticatedApiClient(http,NullLogger<ApiClient>.Instance))
            .AddSingleton<NavigationManager,FacilityTestNavigation>().AddSingleton<FacilityRecommendationsApi>().AddSingleton<IJSRuntime>(js).AddSingleton<IComponentActivator>(capture).BuildServiceProvider();
        await using var renderer=new HtmlRenderer(services,services.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async()=>{
            await renderer.RenderComponentAsync<ResidentFacilities>(ParameterView.Empty);
            await (Task)typeof(ResidentFacilities).GetMethod("OnAfterRenderAsync",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(capture.Page,[true])!;
            Assert.Equal(0,handler.SpeechCalls);
            Assert.Contains("WELCOME",js.Player.Greetings);
        });
    }
    private sealed class OfflineSpeech:HttpMessageHandler
    {
        public int SpeechCalls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
        {
            if(request.Method==HttpMethod.Post){SpeechCalls++;return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable){Content=JsonContent.Create(new{title="TTS provider offline"})});}
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=JsonContent.Create(new ResidentFacilityCatalogue([],new(2026,11,17)))});
        }
    }
    private sealed class Capture:IComponentActivator
    {
        public ResidentFacilities? Page;
        public IComponent CreateInstance(Type type){var item=(IComponent)Activator.CreateInstance(type)!;if(item is ResidentFacilities page)Page=page;return item;}
    }
    private sealed class Browser:IJSRuntime
    {
        public Player Player {get;}=new();
        public ValueTask<T> InvokeAsync<T>(string name,object?[]? args)=>ValueTask.FromResult((T)(object)new Module(Player,args?[0]?.ToString()?.Contains("facility-live-voice")==true));
        public ValueTask<T> InvokeAsync<T>(string name,CancellationToken ct,object?[]? args)=>InvokeAsync<T>(name,args);
    }
    private sealed class Module(Player player,bool live):IJSObjectReference
    {
        public ValueTask<T> InvokeAsync<T>(string name,object?[]? args)=>ValueTask.FromResult(name=="capabilities"
            ?live?(T)(object)false:JsonSerializer.Deserialize<T>("{\"Recognition\":true,\"Speech\":true}")!:(T)(object)player);
        public ValueTask<T> InvokeAsync<T>(string name,CancellationToken ct,object?[]? args)=>InvokeAsync<T>(name,args);
        public ValueTask DisposeAsync()=>ValueTask.CompletedTask;
    }
    private sealed class Player:IJSObjectReference
    {
        public List<string> Greetings {get;}=[];
        public ValueTask<T> InvokeAsync<T>(string name,object?[]? args)
        {
            if(name=="playGreeting")Greetings.Add((string)args![0]!);
            return ValueTask.FromResult(typeof(T)==typeof(bool)?(T)(object)true:default!);
        }
        public ValueTask<T> InvokeAsync<T>(string name,CancellationToken ct,object?[]? args)=>InvokeAsync<T>(name,args);
        public ValueTask DisposeAsync()=>ValueTask.CompletedTask;
    }
}

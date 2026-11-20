using System.Net;
using System.Reflection;
using System.Text.Json;
using ASCOM.Alpaca;
using ASCOM.Alpaca.Razor;
using ASCOM.Common.DeviceInterfaces;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace GreenSwamp.Alpaca.Server.Tests;

public class PulseGuideApiTests
{
    [Fact]
    public async Task PulseGuide_ValidatesDirectionsThroughTheAlpacaProtocol()
    {
        var telescope = DispatchProxy.Create<ITelescopeV4, TelescopeProxy>();
        var proxy = (TelescopeProxy)telescope;
        DeviceManager.LoadConfiguration(DispatchProxy.Create<IAlpacaConfiguration, FalseBooleanProxy>());
        DeviceManager.LoadTelescope(97, telescope, "Pulse guide test", "pulse-guide-test");

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        StartupHelpers.ConfigureAlpacaAPIBehavoir(builder.Services);
        builder.Services.AddControllers().AddApplicationPart(typeof(TelescopeController).Assembly);
        builder.Services.AddSingleton(DispatchProxy.Create<IUserService, FalseBooleanProxy>());
        builder.Services.AddScoped<AuthorizationFilter>();
        await using var app = builder.Build();
        app.MapControllers();

        try
        {
            await app.StartAsync();
            using var client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
            foreach (var direction in new[] { -1, 4, 9, 99 })
            {
                using var response = await SendPulse(client, direction.ToString());
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                Assert.Equal(0x401, json.RootElement.GetProperty("ErrorNumber").GetInt32());
                Assert.Equal(42, json.RootElement.GetProperty("ClientTransactionID").GetInt32());
            }
            Assert.Empty(proxy.Directions);

            foreach (var direction in new[] { 0, 1, 2, 3 })
            {
                using var response = await SendPulse(client, direction.ToString());
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                Assert.Equal(0, json.RootElement.GetProperty("ErrorNumber").GetInt32());
            }
            Assert.Equal(new[] { 0, 1, 2, 3 }, proxy.Directions.Select(direction => (int)direction));

            proxy.RejectPulse = true;
            using var rejected = await SendPulse(client, "0");
            Assert.Equal(HttpStatusCode.OK, rejected.StatusCode);
            using var rejectedJson = JsonDocument.Parse(await rejected.Content.ReadAsStringAsync());
            Assert.Equal(0x40B, rejectedJson.RootElement.GetProperty("ErrorNumber").GetInt32());

            using var malformed = await SendPulse(client, "not-a-number");
            Assert.Equal(HttpStatusCode.BadRequest, malformed.StatusCode);
            using var missing = await SendPulse(client, null);
            Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        }
        finally
        {
            await app.StopAsync();
            DeviceManager.UnloadTelescope(97);
        }
    }

    private static Task<HttpResponseMessage> SendPulse(HttpClient client, string? direction)
    {
        var fields = new Dictionary<string, string> { ["Duration"] = "100", ["ClientTransactionID"] = "42" };
        if (direction != null) fields["Direction"] = direction;
        return client.PutAsync("api/v1/telescope/97/pulseguide", new FormUrlEncodedContent(fields));
    }

    public class TelescopeProxy : DispatchProxy
    {
        public List<GuideDirection> Directions { get; } = [];
        public bool RejectPulse { get; set; }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.Name == "get_Connected") return true;
            if (targetMethod?.Name == "PulseGuide")
            {
                if (RejectPulse) throw new ASCOM.InvalidOperationException("PulseGuide unavailable");
                Directions.Add((GuideDirection)args![0]!);
                return null;
            }
            throw new NotSupportedException($"Unexpected telescope call: {targetMethod?.Name}");
        }
    }

    public class FalseBooleanProxy : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            targetMethod?.ReturnType == typeof(bool) ? false : throw new NotSupportedException($"Unexpected call: {targetMethod?.Name}");
    }
}

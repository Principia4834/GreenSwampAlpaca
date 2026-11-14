using System.Globalization;
using System.Text.Json;
using GreenSwamp.Alpaca.Server.Helpers;
using Microsoft.AspNetCore.Http;

namespace GreenSwamp.Alpaca.Server.Tests
{
    [Collection(CultureCollection.Name)]
    public class InvariantGuardTests
    {
        [Fact]
        public void ApplyInvariantDefaults_MakesNewThreadsInvariant()
        {
            var previousCulture = CultureInfo.DefaultThreadCurrentCulture;
            var previousUi = CultureInfo.DefaultThreadCurrentUICulture;
            try
            {
                using (new CultureScope("de-DE"))
                {
                    CultureInfo.DefaultThreadCurrentCulture = CultureInfo.GetCultureInfo("de-DE");
                    BackendCulture.ApplyInvariantDefaults();

                    string? name = null;
                    string? uiName = null;
                    string? text = null;
                    var thread = new Thread(() =>
                    {
                        name = CultureInfo.CurrentCulture.Name;
                        uiName = CultureInfo.CurrentUICulture.Name;
                        text = 1.5.ToString(CultureInfo.CurrentCulture);
                    });
                    thread.Start();
                    thread.Join();

                    Assert.Equal(string.Empty, name);
                    Assert.Equal(string.Empty, uiName);
                    Assert.Equal("1.5", text);
                    Assert.Equal("1.5", 1.5.ToString(CultureInfo.CurrentCulture));
                }
            }
            finally
            {
                CultureInfo.DefaultThreadCurrentCulture = previousCulture;
                CultureInfo.DefaultThreadCurrentUICulture = previousUi;
            }
        }

        [Theory]
        [InlineData("/api/v1/telescope/0/rightascension", true)]
        [InlineData("/API/v1/telescope/0/declination", true)]
        [InlineData("/management/apiversions", true)]
        [InlineData("/swagger/index.html", true)]
        [InlineData("/charthub/negotiate", true)]
        [InlineData("/telescopestatehub/negotiate", true)]
        [InlineData("/", false)]
        [InlineData("/mountstatus", false)]
        [InlineData("/_blazor", false)]
        [InlineData("/culture/set", false)]
        [InlineData("/apiary", false)]
        public void RestAndHubRoutes_IgnoreBrowserCulture(string path, bool expected)
        {
            Assert.Equal(expected, BackendCulture.IsInvariantPath(new PathString(path)));
        }

        [Fact]
        public void JsonUsesDotAndIsoDates_WhateverTheRegionalFormat()
        {
            using var _ = new CultureScope("de-DE");
            var value = new { Ra = 12.5, When = new DateTime(2025, 3, 4, 5, 6, 7, DateTimeKind.Utc) };

            var json = JsonSerializer.Serialize(value);

            Assert.Equal("{\"Ra\":12.5,\"When\":\"2025-03-04T05:06:07Z\"}", json);
        }
    }
}

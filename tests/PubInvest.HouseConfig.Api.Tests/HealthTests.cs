using System.Net;

namespace PubInvest.HouseConfig.Api.Tests;

[Collection("api")]
public class HealthTests(HouseConfigApiFactory factory)
{
    [Fact]
    public async Task Health_endpoint_reports_healthy()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Healthy", await response.Content.ReadAsStringAsync());
    }
}

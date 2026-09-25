using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using SBQR.Modules.InstitutionTrust.Infrastructure.TrustStore;
using Xunit;

namespace SBQR.Modules.InstitutionTrust.Tests;

public sealed class HttpTrustStoreClientTests
{
    [Fact]
    public async Task FetchAllAsync_maps_the_trust_store_list_response()
    {
        var payload = new[]
        {
            new
            {
                institutionId = "031008",
                instituteType = "03",
                institutionName = "Example Bank",
                status = "ACTIVE",
                activeKey = new
                {
                    keyVersion = 3,
                    publicKeyPem = "-----BEGIN PUBLIC KEY-----\nAAAA\n-----END PUBLIC KEY-----",
                    sha256 = "deadbeef",
                    status = "ACTIVE",
                },
            },
        };
        var handler = new StubHttpMessageHandler(request =>
        {
            request.RequestUri!.AbsolutePath.Should().Be("/trust-store/institutions");
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(payload),
            };
        });
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://mock-trust-store/") };
        var client = new HttpTrustStoreClient(httpClient);

        var result = await client.FetchAllAsync(CancellationToken.None);

        result.Should().HaveCount(1);
        result[0].InstitutionId.Should().Be("031008");
        result[0].InstituteType.Should().Be("03");
        result[0].ActiveKey.KeyVersion.Should().Be(3);
        result[0].ActiveKey.PublicKeyPem.Should().Contain("BEGIN PUBLIC KEY");
    }

    [Fact]
    public async Task FetchAllAsync_returns_empty_list_when_the_store_has_no_institutions()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(Array.Empty<object>()),
        });
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://mock-trust-store/") };
        var client = new HttpTrustStoreClient(httpClient);

        var result = await client.FetchAllAsync(CancellationToken.None);

        result.Should().BeEmpty();
    }

    /// <summary>
    /// BB's real contract is unpublished, so a record with no key object is
    /// plausible. That record is skipped — it must not take the whole batch
    /// down with an NRE and strand every other institution in it.
    /// </summary>
    [Fact]
    public async Task FetchAllAsync_skips_a_record_with_no_key_instead_of_failing_the_batch()
    {
        var payload = new object[]
        {
            new
            {
                institutionId = "031008",
                institutionName = "Keyless Bank",
                status = "ACTIVE",
                activeKey = (object?)null,
            },
            new
            {
                institutionId = "031009",
                institutionName = "Good Bank",
                status = "ACTIVE",
                activeKey = new
                {
                    keyVersion = 1,
                    publicKeyPem = "-----BEGIN PUBLIC KEY-----\nAAAA\n-----END PUBLIC KEY-----",
                    sha256 = "deadbeef",
                    status = "ACTIVE",
                },
            },
        };
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(payload),
        });
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://mock-trust-store/") };
        var client = new HttpTrustStoreClient(httpClient);

        var result = await client.FetchAllAsync(CancellationToken.None);

        result.Should().ContainSingle();
        result[0].InstitutionId.Should().Be("031009");
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;

        public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> respond)
        {
            _respond = respond;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(_respond(request));
    }
}

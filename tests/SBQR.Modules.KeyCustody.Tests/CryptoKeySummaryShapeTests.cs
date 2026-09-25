using System.Globalization;
using System.Reflection;
using FluentAssertions;
using SBQR.Modules.KeyCustody.Application.Contracts;
using Xunit;

namespace SBQR.Modules.KeyCustody.Tests;

/// <summary>
/// Contract tests that pin the <see cref="CryptoKeySummary"/> response
/// shape. The HTTP surface for <c>POST /v1/crypto-keys</c> is
/// confidentiality-clean by construction (no <c>custody_key_reference</c>,
/// no <c>PrivateKeyPem</c>, no <c>CustodyHandle</c>, no plaintext bytes)
/// — these tests guard against accidental drift.
/// </summary>
public sealed class CryptoKeySummaryShapeTests
{
    private static readonly System.Text.Json.JsonSerializerOptions WebJsonOptions =
        new(System.Text.Json.JsonSerializerDefaults.Web);

    /// <summary>
    /// Pinned property list. If anyone adds <c>CustodyKeyReference</c>,
    /// <c>CustodyHandle</c>, <c>PrivateKeyPem</c>, <c>PrivateKeyBytes</c>,
    /// or <c>Plaintext</c> to <see cref="CryptoKeySummary"/>, this test
    /// fails the build.
    /// </summary>
    [Fact]
    public void CryptoKeySummary_does_not_expose_any_custody_or_private_field()
    {
        var propertyNames = typeof(CryptoKeySummary)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name)
            .ToHashSet(StringComparer.Ordinal);

        propertyNames.Should().NotContain("CustodyKeyReference");
        propertyNames.Should().NotContain("CustodyHandle");
        propertyNames.Should().NotContain("PrivateKeyPem");
        propertyNames.Should().NotContain("PrivateKeyBytes");
        propertyNames.Should().NotContain("Plaintext");
        propertyNames.Should().NotContain("PrivateKey");
    }

    /// <summary>
    /// Pinned JSON-serialization shape (the contract the controller
    /// writes over the wire). Locks the field names and the casing style
    /// the public-metadata projection uses — any future change to either
    /// has to land explicitly via this test. ASP.NET Core uses
    /// <see cref="JsonSerializerDefaults.Web"/> by default (camelCase
    /// property names), so this test mirrors that.
    /// </summary>
    [Fact]
    public void CryptoKeySummary_json_shape_is_pinned()
    {
        var summary = new CryptoKeySummary(
            CryptoKeyId: Guid.Parse("11111111-1111-1111-1111-111111111111"),
            TenantId: Guid.Parse("22222222-2222-2222-2222-222222222222"),
            KeyId: "sbqr-signing",
            KeyVersion: 1,
            PublicKeyPem: "-----BEGIN PUBLIC KEY-----\nAAAA\n-----END PUBLIC KEY-----",
            Status: "ACTIVE",
            IsActive: true,
            CreatedAt: DateTimeOffset.Parse("2026-09-06T00:00:00+00:00", CultureInfo.InvariantCulture));

        var json = System.Text.Json.JsonSerializer.Serialize(summary, WebJsonOptions);

        // Pin every public field's JSON name. Any future rename breaks this
        // test on purpose.
        json.Should().Contain("\"cryptoKeyId\"");
        json.Should().Contain("\"tenantId\"");
        json.Should().Contain("\"keyId\":\"sbqr-signing\"");
        json.Should().Contain("\"keyVersion\":1");
        json.Should().Contain("\"publicKeyPem\"");
        json.Should().Contain("\"status\":\"ACTIVE\"");
        json.Should().Contain("\"isActive\":true");
        json.Should().Contain("\"createdAt\"");
    }

    /// <summary>
    /// No property on the response shape accepts <c>byte[]</c> (private
    /// bytes) — defensive pin in case anyone re-introduces a custody or
    /// private-bytes field accidentally in a future refactor.
    /// </summary>
    [Fact]
    public void CryptoKeySummary_does_not_carry_any_byte_array_property()
    {
        var propertyTypes = typeof(CryptoKeySummary)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.PropertyType)
            .ToArray();

        propertyTypes.Should().NotContain(typeof(byte[]));
    }

    /// <summary>
    /// Sanity check that <see cref="CryptoKeySummaryBuilder.Build"/> is the
    /// single projection entry point and its return type is the response
    /// DTO. If anyone changes the projection to return a wider type
    /// (e.g. an internal aggregate), this test fails.
    /// </summary>
    [Fact]
    public void CryptoKeySummaryBuilder_Build_returns_CryptoKeySummary()
    {
        var build = typeof(CryptoKeySummaryBuilder)
            .GetMethod("Build", BindingFlags.Public | BindingFlags.Static);

        build.Should().NotBeNull("CryptoKeySummaryBuilder.Build must remain the projection entry point");
        build!.ReturnType.Should().Be<CryptoKeySummary>();
    }
}

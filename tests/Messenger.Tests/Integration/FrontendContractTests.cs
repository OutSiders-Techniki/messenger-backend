using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Messenger.Application.DTOs;
using Messenger.Application.Services;

namespace Messenger.Tests.Integration;

/// <summary>Фиксированный пакет создан текущим frontend encryptMessage, без изменений его исходников.</summary>
public sealed class FrontendContractTests
{
    private const string SenderPublicId = "ABCD2345EFGH";
    private static readonly Guid DialogId = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private const string PacketJson = """
        {"v":1,"id":"33333333-3333-4333-8333-333333333333","sentAt":1794916800000,"iv":"AAECAwQFBgcICQoL","ct":"l50HmxVdEqld9EYJncmo0VNuVrTRdUfCY53UyhPr-cMiyLhz-Q"}
        """;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public void ActualFrontendPacket_RoundTripsThroughBackend_AndDecryptsWithPublicSenderAad()
    {
        var request = JsonSerializer.Deserialize<SendMessageRequest>(PacketJson, JsonOptions)!;
        MessageService.Validate(request);
        Assert.Equal(1794916800000, request.SentAt.ToUnixTimeMilliseconds());
        var response = new MessageResponse(DialogId, SenderPublicId, request.Version, request.Id,
            request.SentAt, request.Iv!, request.Ciphertext!);
        var serialized = JsonSerializer.Serialize(response, JsonOptions);
        using var json = JsonDocument.Parse(serialized);
        Assert.Equal(SenderPublicId, json.RootElement.GetProperty("senderId").GetString());
        Assert.Equal(1794916800000, json.RootElement.GetProperty("sentAt").GetInt64());
        using var input = JsonDocument.Parse(PacketJson);
        foreach (var property in new[] { "v", "id", "sentAt", "iv", "ct" })
            Assert.Equal(input.RootElement.GetProperty(property).GetRawText(), json.RootElement.GetProperty(property).GetRawText());

        var decoded = JsonSerializer.Deserialize<MessageResponse>(serialized, JsonOptions)!;
        var aad = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new object[]
        {
            1, decoded.DialogId.ToString("D"), decoded.SenderId, decoded.Id.ToString("D"), decoded.SentAt.ToUnixTimeMilliseconds(),
        }));
        var plaintext = new byte[decoded.Ciphertext.Length - 16];
        using var aes = new AesGcm(Enumerable.Range(0, 32).Select(i => (byte)i).ToArray(), 16);
        aes.Decrypt(decoded.Iv, decoded.Ciphertext.AsSpan(0, plaintext.Length), decoded.Ciphertext.AsSpan(plaintext.Length), plaintext, aad);
        Assert.Equal("Привет, мир!", Encoding.UTF8.GetString(plaintext));

        var wrongSenderAad = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new object[]
        {
            1, decoded.DialogId.ToString("D"), Guid.NewGuid().ToString("D"), decoded.Id.ToString("D"), decoded.SentAt.ToUnixTimeMilliseconds(),
        }));
        Assert.Throws<AuthenticationTagMismatchException>(() => aes.Decrypt(decoded.Iv,
            decoded.Ciphertext.AsSpan(0, plaintext.Length), decoded.Ciphertext.AsSpan(plaintext.Length), plaintext, wrongSenderAad));
    }

    [Theory]
    [InlineData("\"2026-11-17T12:00:00.000Z\"")]
    [InlineData("\"1794916800000\"")]
    [InlineData("-1")]
    [InlineData("1.5")]
    [InlineData("253402300800000")]
    [InlineData("9007199254740992")]
    [InlineData("null")]
    public void InvalidFrontendTimestamps_AreRejected(string timestamp)
    {
        var json = PacketJson.Replace("1794916800000", timestamp, StringComparison.Ordinal);
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<SendMessageRequest>(json, JsonOptions));
    }

    [Theory]
    [InlineData("_w")]
    [InlineData("-___")]
    [InlineData("AQIDBA")]
    public void Base64UrlBytes_RoundTripCanonically(string encoded)
    {
        var options = new JsonSerializerOptions();
        options.Converters.Add(new Base64UrlBytesJsonConverter());
        var bytes = JsonSerializer.Deserialize<byte[]>(JsonSerializer.Serialize(encoded), options)!;
        Assert.Equal(encoded, JsonSerializer.Deserialize<string>(JsonSerializer.Serialize(bytes, options)));
    }

    [Theory]
    [InlineData("_x")]
    [InlineData("AQ ID")]
    [InlineData("AQ===")]
    [InlineData("*")]
    [InlineData("a")]
    public void NonCanonicalBase64_IsRejected(string encoded)
    {
        var options = new JsonSerializerOptions();
        options.Converters.Add(new Base64UrlBytesJsonConverter());
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<byte[]>(JsonSerializer.Serialize(encoded), options));
    }

    [Fact]
    public void StandardBase64Input_IsAccepted_ButAlwaysEmitsBase64Url()
    {
        var options = new JsonSerializerOptions();
        options.Converters.Add(new Base64UrlBytesJsonConverter());
        var bytes = JsonSerializer.Deserialize<byte[]>("\"+/8=\"", options)!;
        Assert.Equal("\"-_8\"", JsonSerializer.Serialize(bytes, options));
    }
}

using System.Buffers.Binary;
using System.Formats.Cbor;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;

namespace LummaSimulator.Web.Tests;

// Test-only software authenticator. The production app never generates or receives these private keys.
internal sealed class SoftwareAuthenticator : IDisposable
{
    private readonly ECDsa _key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    private readonly byte[] _credentialId = RandomNumberGenerator.GetBytes(32);
    private uint _counter;
    public byte[] UserHandle { get; private set; } = [];
    private static string B64(byte[] value) => WebEncoders.Base64UrlEncode(value);
    private static byte[] ClientData(string type, string challenge, string origin) =>
        JsonSerializer.SerializeToUtf8Bytes(new { type, challenge, origin, crossOrigin = false });

    public JsonElement Register(string optionsJson, string origin = "https://localhost:7243", bool verified = true, string rpId = "localhost", string? challenge = null)
    {
        using var options = JsonDocument.Parse(optionsJson);
        var root = options.RootElement;
        UserHandle = WebEncoders.Base64UrlDecode(root.GetProperty("user").GetProperty("id").GetString()!);
        var client = ClientData("webauthn.create", challenge ?? root.GetProperty("challenge").GetString()!, origin);
        var publicKey = _key.ExportParameters(false);
        var cose = new CborWriter();
        cose.WriteStartMap(5);
        cose.WriteInt32(1); cose.WriteInt32(2);
        cose.WriteInt32(3); cose.WriteInt32(-7);
        cose.WriteInt32(-1); cose.WriteInt32(1);
        cose.WriteInt32(-2); cose.WriteByteString(publicKey.Q.X!);
        cose.WriteInt32(-3); cose.WriteByteString(publicKey.Q.Y!);
        cose.WriteEndMap();
        using var authData = new MemoryStream();
        authData.Write(SHA256.HashData(Encoding.UTF8.GetBytes(rpId)));
        authData.WriteByte((byte)(verified ? 0x45 : 0x41));
        authData.Write(new byte[4]);
        authData.Write(new byte[16]);
        var length = new byte[2]; BinaryPrimitives.WriteUInt16BigEndian(length, (ushort)_credentialId.Length);
        authData.Write(length); authData.Write(_credentialId); authData.Write(cose.Encode());
        var attestation = new CborWriter();
        attestation.WriteStartMap(3);
        attestation.WriteTextString("fmt"); attestation.WriteTextString("none");
        attestation.WriteTextString("attStmt"); attestation.WriteStartMap(0); attestation.WriteEndMap();
        attestation.WriteTextString("authData"); attestation.WriteByteString(authData.ToArray());
        attestation.WriteEndMap();
        return JsonSerializer.SerializeToElement(new
        {
            id = B64(_credentialId), rawId = B64(_credentialId), type = "public-key", extensions = new { },
            response = new { attestationObject = B64(attestation.Encode()), clientDataJSON = B64(client), transports = new[] { "internal" } }
        });
    }

    public JsonElement Authenticate(string optionsJson, string origin = "https://localhost:7243", bool verified = true,
        string rpId = "localhost", bool badSignature = false, string? challenge = null, byte[]? userHandle = null, bool backupEligible = false, uint? counter = null)
    {
        using var options = JsonDocument.Parse(optionsJson);
        var client = ClientData("webauthn.get", challenge ?? options.RootElement.GetProperty("challenge").GetString()!, origin);
        var authData = new byte[37];
        SHA256.HashData(Encoding.UTF8.GetBytes(rpId)).CopyTo(authData, 0);
        authData[32] = (byte)((verified ? 0x05 : 0x01) | (backupEligible ? 0x08 : 0));
        BinaryPrimitives.WriteUInt32BigEndian(authData.AsSpan(33), counter ?? ++_counter);
        var signature = _key.SignData(authData.Concat(SHA256.HashData(client)).ToArray(), HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);
        if (badSignature) signature[^1] ^= 1;
        return JsonSerializer.SerializeToElement(new
        {
            id = B64(_credentialId), rawId = B64(_credentialId), type = "public-key", extensions = new { },
            response = new { authenticatorData = B64(authData), signature = B64(signature), clientDataJSON = B64(client), userHandle = B64(userHandle ?? UserHandle) }
        });
    }
    public void Dispose() => _key.Dispose();
}

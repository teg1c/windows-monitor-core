using System.Security.Cryptography;
using System.Text;

namespace WindowsMonitor.Infrastructure.Licensing;

public static class LicenseCipher
{
    private const string Prefix = "WML1.";
    private const int NonceSize = 12;
    private const int TagSize = 16;

    public static string EncryptJson(string json, string keyBase64)
    {
        var key = DecodeKey(keyBase64);
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var plaintext = Encoding.UTF8.GetBytes(json);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[TagSize];

        using var aes = new AesGcm(key, TagSize);
        aes.Encrypt(nonce, plaintext, ciphertext, tag);

        var payload = new byte[nonce.Length + tag.Length + ciphertext.Length];
        Buffer.BlockCopy(nonce, 0, payload, 0, nonce.Length);
        Buffer.BlockCopy(tag, 0, payload, nonce.Length, tag.Length);
        Buffer.BlockCopy(ciphertext, 0, payload, nonce.Length + tag.Length, ciphertext.Length);
        return Prefix + Base64UrlEncode(payload);
    }

    public static string DecryptJson(string encryptedText, string keyBase64)
    {
        var text = encryptedText.Trim();
        if (!text.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("授权码格式无效。");
        }

        var payload = Base64UrlDecode(text[Prefix.Length..]);
        if (payload.Length <= NonceSize + TagSize)
        {
            throw new InvalidOperationException("授权码内容无效。");
        }

        var key = DecodeKey(keyBase64);
        var nonce = payload[..NonceSize];
        var tag = payload[NonceSize..(NonceSize + TagSize)];
        var ciphertext = payload[(NonceSize + TagSize)..];
        var plaintext = new byte[ciphertext.Length];

        using var aes = new AesGcm(key, TagSize);
        aes.Decrypt(nonce, ciphertext, tag, plaintext);
        return Encoding.UTF8.GetString(plaintext);
    }

    private static byte[] DecodeKey(string keyBase64)
    {
        var key = Convert.FromBase64String(keyBase64);
        if (key.Length is not (16 or 24 or 32))
        {
            throw new InvalidOperationException("授权加密密钥必须是 16、24 或 32 字节。");
        }

        return key;
    }

    private static string Base64UrlEncode(byte[] bytes)
    {
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private static byte[] Base64UrlDecode(string text)
    {
        var padded = text.Replace('-', '+').Replace('_', '/');
        padded = padded.PadRight(padded.Length + (4 - padded.Length % 4) % 4, '=');
        return Convert.FromBase64String(padded);
    }
}

using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Konscious.Security.Cryptography;

namespace ThunderbirdAccounts.Core;

public sealed record AccountArchive(int Schema, DateTimeOffset CreatedUtc, string ProfileName, string ThunderbirdVersion,
    Dictionary<string, JsonElement> Preferences, Dictionary<string, byte[]> Files);

public static class Archive
{
    public const int MaxBytes = 48 * 1024 * 1024;
    private static readonly byte[] Magic = "TBACCT01"u8.ToArray();
    // v1: magic(8), salt(16), nonce(12), plaintext length LE(4), tag(16), ciphertext.
    private const int HeaderSize = 40;
    private static byte[] Key(string password, byte[] salt)
    {
        if (password.Length < 1 || password.Length > 1024) throw new ArgumentException(TextCatalog.Get("TheBackupPasswordMustBeCharacters"));
        byte[] bytes = Encoding.UTF8.GetBytes(password);
        try
        {
            using var argon = new Argon2id(bytes) { Salt = salt, MemorySize = 65536, Iterations = 3, DegreeOfParallelism = 4 };
            return argon.GetBytes(32);
        }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }
    public static byte[] Encrypt(AccountArchive archive, string password, bool allowShortPassword = false)
    {
        int minimum = allowShortPassword ? 1 : 12;
        if (password.Length < minimum || password.Length > 1024)
            throw new ArgumentException(TextCatalog.Get("TheBackupPasswordMustBeCharactersShorterPasswordsRequire", minimum));
        byte[] plain = JsonSerializer.SerializeToUtf8Bytes(archive);
        if (plain.Length > MaxBytes) { CryptographicOperations.ZeroMemory(plain); throw new InvalidDataException(TextCatalog.Get("TheBackupIsTooLarge")); }
        byte[] header = new byte[HeaderSize]; Magic.CopyTo(header, 0);
        RandomNumberGenerator.Fill(header.AsSpan(8, 16)); RandomNumberGenerator.Fill(header.AsSpan(24, 12));
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(36), plain.Length);
        byte[] key = Key(password, header[8..24]);
        try
        {
            byte[] output = new byte[HeaderSize + 16 + plain.Length]; header.CopyTo(output, 0);
            using var aes = new AesGcm(key, 16);
            aes.Encrypt(header.AsSpan(24, 12), plain, output.AsSpan(56), output.AsSpan(40, 16), header);
            return output;
        }
        finally { CryptographicOperations.ZeroMemory(key); CryptographicOperations.ZeroMemory(plain); }
    }
    public static AccountArchive Decrypt(byte[] data, string password)
    {
        if (data.Length < 56 || data.Length > MaxBytes + 56 || !data.AsSpan(0, 8).SequenceEqual(Magic))
            throw new InvalidDataException(TextCatalog.Get("TheFileIsNotAVersionTbaccountsBackup"));
        int length = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(36, 4));
        if (length < 1 || length > MaxBytes || length != data.Length - 56) throw new InvalidDataException(TextCatalog.Get("InvalidBackupLength"));
        byte[] key = Key(password, data[8..24]); byte[] plain = new byte[length];
        try
        {
            using var aes = new AesGcm(key, 16);
            try { aes.Decrypt(data.AsSpan(24, 12), data.AsSpan(56), data.AsSpan(40, 16), plain, data.AsSpan(0, 40)); }
            catch (CryptographicException) { throw new InvalidDataException(TextCatalog.Get("IncorrectBackupPasswordOrCorruptTamperedBackupNothingHas")); }
            var archive = JsonSerializer.Deserialize<AccountArchive>(plain) ?? throw new InvalidDataException(TextCatalog.Get("EmptyBackup"));
            BackupService.Validate(archive); return archive;
        }
        finally { CryptographicOperations.ZeroMemory(key); CryptographicOperations.ZeroMemory(plain); }
    }
    public static AccountArchive Read(string file, string password)
    {
        if (new FileInfo(file).Length > MaxBytes + 56) throw new InvalidDataException(TextCatalog.Get("TheBackupFileIsTooLarge"));
        return Decrypt(File.ReadAllBytes(file), password);
    }
    internal static void AtomicCreate(string path, byte[] bytes)
    {
        string full = Path.GetFullPath(path);
        string parent = Path.GetDirectoryName(full)!;
        SafeFiles.NoLinks(parent);
        string temp = Path.Combine(parent, ".tbaccounts-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { stream.Write(bytes); stream.Flush(true); }
            File.Move(temp, full, false);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}

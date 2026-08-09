using System.Text;
using NutManager.Core.Configuration;

namespace NutManager.Infrastructure.Configuration;

internal static class NutConfigurationTextCodec
{
    private static readonly Encoding Utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
    private static readonly Encoding Utf8Bom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true, throwOnInvalidBytes: true);
    private static readonly Encoding Utf16LittleEndian = new UnicodeEncoding(bigEndian: false, byteOrderMark: true, throwOnInvalidBytes: true);
    private static readonly Encoding Utf16BigEndian = new UnicodeEncoding(bigEndian: true, byteOrderMark: true, throwOnInvalidBytes: true);

    public static (NutConfigurationTextEncoding Encoding, string Text) Decode(ReadOnlySpan<byte> bytes)
    {
        var (encoding, bomLength) = DetectEncoding(bytes);
        return (encoding, GetEncoding(encoding).GetString(bytes[bomLength..]));
    }

    public static byte[] Encode(string text, NutConfigurationTextEncoding encoding)
    {
        var textEncoding = GetEncoding(encoding);
        var content = textEncoding.GetBytes(text);
        var bom = encoding switch
        {
            NutConfigurationTextEncoding.Utf8Bom or NutConfigurationTextEncoding.Utf16LittleEndian or NutConfigurationTextEncoding.Utf16BigEndian => textEncoding.GetPreamble(),
            _ => Array.Empty<byte>()
        };

        if (bom.Length == 0)
        {
            return content;
        }

        var bytes = new byte[bom.Length + content.Length];
        Buffer.BlockCopy(bom, 0, bytes, 0, bom.Length);
        Buffer.BlockCopy(content, 0, bytes, bom.Length, content.Length);
        return bytes;
    }

    private static (NutConfigurationTextEncoding Encoding, int BomLength) DetectEncoding(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length >= 3 && bytes[..3].SequenceEqual(new byte[] { 0xEF, 0xBB, 0xBF }))
        {
            return (NutConfigurationTextEncoding.Utf8Bom, 3);
        }

        if (bytes.Length >= 2 && bytes[..2].SequenceEqual(new byte[] { 0xFF, 0xFE }))
        {
            return (NutConfigurationTextEncoding.Utf16LittleEndian, 2);
        }

        if (bytes.Length >= 2 && bytes[..2].SequenceEqual(new byte[] { 0xFE, 0xFF }))
        {
            return (NutConfigurationTextEncoding.Utf16BigEndian, 2);
        }

        return (NutConfigurationTextEncoding.Utf8, 0);
    }

    private static Encoding GetEncoding(NutConfigurationTextEncoding encoding) => encoding switch
    {
        NutConfigurationTextEncoding.Utf8 => Utf8,
        NutConfigurationTextEncoding.Utf8Bom => Utf8Bom,
        NutConfigurationTextEncoding.Utf16LittleEndian => Utf16LittleEndian,
        NutConfigurationTextEncoding.Utf16BigEndian => Utf16BigEndian,
        _ => throw new ArgumentOutOfRangeException(nameof(encoding), encoding, "Unsupported configuration encoding.")
    };
}

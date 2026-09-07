using System.Buffers.Binary;
using System.Text;

namespace SwLicenseWatcher.Setup.Core;

public static class AttachedPayload
{
    public const string MagicText = "SWLWPAY1";
    public const int FooterLength = 16;
    public const long MaxZipLength = 2L * 1024 * 1024 * 1024;

    private static ReadOnlySpan<byte> MagicBytes => "SWLWPAY1"u8;

    public static void Attach(string hostExePath, Stream zip, string outputExePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(hostExePath);
        ArgumentNullException.ThrowIfNull(zip);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputExePath);

        if (!File.Exists(hostExePath))
        {
            throw new FileNotFoundException("Launcher stub was not found.", hostExePath);
        }

        if (zip.CanSeek)
        {
            zip.Seek(0, SeekOrigin.Begin);
        }

        var outputDirectory = Path.GetDirectoryName(outputExePath);
        if (!string.IsNullOrWhiteSpace(outputDirectory))
        {
            Directory.CreateDirectory(outputDirectory);
        }

        var tempPath = outputExePath + ".tmp";
        try
        {
            using (var output = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                using (var host = File.OpenRead(hostExePath))
                {
                    host.CopyTo(output);
                }

                var zipStart = output.Position;
                zip.CopyTo(output);
                var zipLength = output.Position - zipStart;
                if (zipLength <= 0)
                {
                    throw new InvalidDataException("The payload zip is empty.");
                }

                if (zipLength > MaxZipLength)
                {
                    throw new InvalidDataException($"The payload zip exceeds {MaxZipLength} bytes.");
                }

                Span<byte> footer = stackalloc byte[FooterLength];
                BinaryPrimitives.WriteInt64LittleEndian(footer, zipLength);
                MagicBytes.CopyTo(footer[8..]);
                output.Write(footer);
            }

            File.Copy(tempPath, outputExePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    public static void Attach(string hostExePath, byte[] zip, string outputExePath)
    {
        ArgumentNullException.ThrowIfNull(zip);
        using var stream = new MemoryStream(zip, writable: false);
        Attach(hostExePath, stream, outputExePath);
    }

    public static bool TryReadZip(string exePath, out byte[] zip, out string error)
    {
        zip = [];
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(exePath) || !File.Exists(exePath))
        {
            error = "The setup executable was not found.";
            return false;
        }

        using var stream = File.OpenRead(exePath);
        if (stream.Length < FooterLength)
        {
            error = "The file is too small to contain an attached payload.";
            return false;
        }

        stream.Seek(-FooterLength, SeekOrigin.End);
        Span<byte> footer = stackalloc byte[FooterLength];
        stream.ReadExactly(footer);

        if (!footer[8..].SequenceEqual(MagicBytes))
        {
            error = "The file does not contain an SWLWPAY1 payload.";
            return false;
        }

        var zipLength = BinaryPrimitives.ReadInt64LittleEndian(footer);
        if (zipLength <= 0 || zipLength > MaxZipLength)
        {
            error = "The attached payload length is invalid.";
            return false;
        }

        var zipStart = stream.Length - FooterLength - zipLength;
        if (zipStart < 0)
        {
            error = "The attached payload is truncated.";
            return false;
        }

        stream.Seek(zipStart, SeekOrigin.Begin);
        zip = new byte[zipLength];
        var read = stream.Read(zip, 0, zip.Length);
        if (read != zip.Length)
        {
            error = "The attached payload is truncated.";
            zip = [];
            return false;
        }

        return true;
    }

    public static byte[] ReadZip(string exePath)
    {
        if (!TryReadZip(exePath, out var zip, out var error))
        {
            throw new InvalidDataException(error);
        }

        return zip;
    }

    public static bool HasPayload(string exePath)
    {
        return TryReadZip(exePath, out _, out _);
    }

    public static string MagicAscii => Encoding.ASCII.GetString(MagicBytes);
}

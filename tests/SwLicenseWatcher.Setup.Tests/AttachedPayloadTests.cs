using System.IO.Compression;
using System.Text;
using SwLicenseWatcher.Setup.Core;

namespace SwLicenseWatcher.Setup.Tests;

public class AttachedPayloadTests
{
    [Fact]
    public void Attach_then_read_restores_the_zip_bytes()
    {
        using var dir = new TempDirectory();
        var host = Path.Combine(dir.Path, "host.exe");
        var output = Path.Combine(dir.Path, "setup.exe");
        File.WriteAllBytes(host, [0x4D, 0x5A, 0x00, 0x01, 0x02, 0x03]);
        var zip = CreateZip("hello.txt", "payload-body");

        AttachedPayload.Attach(host, zip, output);

        Assert.True(AttachedPayload.HasPayload(output));
        var restored = AttachedPayload.ReadZip(output);
        Assert.Equal(zip, restored);
        Assert.True(File.ReadAllBytes(output).Length > File.ReadAllBytes(host).Length);
    }

    [Fact]
    public void TryReadZip_rejects_a_host_without_magic()
    {
        using var dir = new TempDirectory();
        var host = Path.Combine(dir.Path, "host.exe");
        File.WriteAllBytes(host, [0x4D, 0x5A, 0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08, 0x09, 0x0A, 0x0B, 0x0C, 0x0D, 0x0E, 0x0F]);

        Assert.False(AttachedPayload.TryReadZip(host, out var zip, out var error));
        Assert.Empty(zip);
        Assert.Contains("SWLWPAY1", error, StringComparison.Ordinal);
    }

    [Fact]
    public void TryReadZip_rejects_a_truncated_footer()
    {
        using var dir = new TempDirectory();
        var host = Path.Combine(dir.Path, "tiny.exe");
        File.WriteAllBytes(host, "SWLWPAY"u8.ToArray());

        Assert.False(AttachedPayload.TryReadZip(host, out _, out var error));
        Assert.Contains("too small", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryReadZip_rejects_a_length_that_overruns_the_file()
    {
        using var dir = new TempDirectory();
        var host = Path.Combine(dir.Path, "broken.exe");
        using (var stream = File.Create(host))
        {
            stream.Write("MZ"u8);
            stream.Write(BitConverter.GetBytes(1_000_000L));
            stream.Write("SWLWPAY1"u8);
        }

        Assert.False(AttachedPayload.TryReadZip(host, out _, out var error));
        Assert.Contains("truncated", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Attach_then_extract_validates_a_full_payload()
    {
        using var dir = new TempDirectory();
        var host = Path.Combine(dir.Path, "host.exe");
        File.WriteAllBytes(host, [0x4D, 0x5A, 0x90, 0x00]);
        var ui = Path.Combine(dir.Path, "ui", PayloadLayout.SetupUiFileName);
        var worker = Path.Combine(dir.Path, "worker");
        var watchdog = Path.Combine(dir.Path, "watchdog");
        Directory.CreateDirectory(Path.GetDirectoryName(ui)!);
        Directory.CreateDirectory(worker);
        Directory.CreateDirectory(watchdog);
        File.WriteAllText(ui, "ui");
        File.WriteAllText(Path.Combine(worker, PayloadLayout.WorkerExe), "worker");
        File.WriteAllText(Path.Combine(watchdog, PayloadLayout.WatchdogExe), "watchdog");
        var zip = PayloadZipBuilder.Build(
            new CompanySettings
            {
                ServerBaseUrl = "https://ok.example",
                AgentToken = new string('c', 32),
                Version = "9.9.9"
            },
            ui,
            worker,
            watchdog);
        var setup = Path.Combine(dir.Path, "company-setup.exe");
        AttachedPayload.Attach(host, zip, setup);

        var extracted = PayloadExtractor.ExtractFromExecutable(setup, Path.Combine(dir.Path, "run"));
        PayloadZipBuilder.ValidateLayout(extracted);
        Assert.Equal("9.9.9", CompanySettingsStore.Load(PayloadLayout.GetCompanyJsonPath(extracted)).Version);
    }

    [Fact]
    public void Extract_rejects_zip_slip_entries()
    {
        using var dir = new TempDirectory();
        var zip = CreateZip("../escape.txt", "nope");

        var thrown = Assert.Throws<InvalidDataException>(() =>
            ZipExtractor.ExtractToDirectory(zip, Path.Combine(dir.Path, "out")));
        Assert.Contains("escapes", thrown.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static byte[] CreateZip(string entryName, string content)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = archive.CreateEntry(entryName);
            using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
            writer.Write(content);
        }

        return stream.ToArray();
    }
}

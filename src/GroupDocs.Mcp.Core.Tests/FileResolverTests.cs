using GroupDocs.Mcp.Core;
using GroupDocs.Mcp.Core.Entities;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace GroupDocs.Mcp.Core.Tests;

public class FileResolverTests
{
    private readonly Mock<IFileStorage> _storageMock = new();

    private FileResolver CreateResolver(McpConfig? config = null)
    {
        var options = Options.Create(config ?? new McpConfig());
        return new FileResolver(_storageMock.Object, options);
    }

    [Fact]
    public async Task ResolveAsync_WithFileContent_ReturnsMemoryStream()
    {
        var resolver = CreateResolver();
        var bytes = "hello"u8.ToArray();
        var base64 = Convert.ToBase64String(bytes);

        var result = await resolver.ResolveAsync(new FileInput
        {
            FileContent = base64,
            FileName = "test.txt"
        });

        Assert.Equal("test.txt", result.FileName);
        Assert.IsType<MemoryStream>(result.Stream);
    }

    [Fact]
    public async Task ResolveAsync_WithFileContent_MissingFileName_Throws()
    {
        var resolver = CreateResolver();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            resolver.ResolveAsync(new FileInput
            {
                FileContent = Convert.ToBase64String("data"u8.ToArray())
            }));
    }

    [Fact]
    public async Task ResolveAsync_WithFilePath_ReturnsStorageStream()
    {
        var stream = new MemoryStream("content"u8.ToArray());
        _storageMock
            .Setup(s => s.ReadFileStreamAsync("report.pdf", It.IsAny<CancellationToken>()))
            .ReturnsAsync(stream);

        var resolver = CreateResolver();

        var result = await resolver.ResolveAsync(new FileInput { FilePath = "report.pdf" });

        Assert.Equal("report.pdf", result.FileName);
    }

    [Fact]
    public async Task ResolveAsync_NoInput_Throws()
    {
        var resolver = CreateResolver();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            resolver.ResolveAsync(new FileInput()));
    }

    [Fact]
    public async Task ResolveAsync_FileNotFound_IncludesAvailableFiles()
    {
        _storageMock
            .Setup(s => s.ReadFileStreamAsync("missing.pdf", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new FileNotFoundException());

        _storageMock
            .Setup(s => s.ListDirsAndFilesAsync("", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                FileSystemEntry.File("existing.pdf", "existing.pdf", 1024)
            });

        var resolver = CreateResolver();

        var ex = await Assert.ThrowsAsync<FileNotFoundException>(() =>
            resolver.ResolveAsync(new FileInput { FilePath = "missing.pdf" }));

        Assert.Contains("existing.pdf", ex.Message);
    }

    // ---- fileName-alone resolution (the form the tool descriptions recommend) ----

    [Fact]
    public async Task ResolveAsync_WithFileNameOnly_ResolvesFromStorage()
    {
        var stream = new MemoryStream("content"u8.ToArray());
        _storageMock
            .Setup(s => s.ReadFileStreamAsync("report.pdf", It.IsAny<CancellationToken>()))
            .ReturnsAsync(stream);

        var resolver = CreateResolver();

        // This used to throw ArgumentException, which the client saw as the opaque
        // "An error occurred invoking '<tool>'" - while the descriptions told callers
        // to pass exactly this.
        var result = await resolver.ResolveAsync(new FileInput { FileName = "report.pdf" });

        Assert.Equal("report.pdf", result.FileName);
        Assert.Same(stream, result.Stream);
    }

    [Fact]
    public async Task ResolveAsync_WithFilePathAndFileName_PrefersFilePath()
    {
        var stream = new MemoryStream("content"u8.ToArray());
        _storageMock
            .Setup(s => s.ReadFileStreamAsync("actual.pdf", It.IsAny<CancellationToken>()))
            .ReturnsAsync(stream);

        var resolver = CreateResolver();

        var result = await resolver.ResolveAsync(
            new FileInput { FilePath = "actual.pdf", FileName = "ignored.pdf" });

        Assert.Equal("actual.pdf", result.FileName);
    }

    [Fact]
    public async Task ResolveAsync_WithNothingProvided_ThrowsWithActionableMessage()
    {
        var resolver = CreateResolver();

        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            resolver.ResolveAsync(new FileInput()));

        Assert.Contains("filePath", ex.Message);
        Assert.Contains("fileContent", ex.Message);
    }

    // ---- the available-files listing ----

    [Fact]
    public async Task ResolveAsync_MissingFile_ListsAvailableFiles()
    {
        _storageMock
            .Setup(s => s.ReadFileStreamAsync("nope.pdf", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new FileNotFoundException());
        _storageMock
            .Setup(s => s.ListDirsAndFilesAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                FileSystemEntry.File("report.pdf", "report.pdf", 2048),
                FileSystemEntry.File("notes.docx", "notes.docx", 512)
            });

        var resolver = CreateResolver();

        var ex = await Assert.ThrowsAsync<FileNotFoundException>(() =>
            resolver.ResolveAsync(new FileInput { FilePath = "nope.pdf" }));

        Assert.Contains("Available files:", ex.Message);
        Assert.Contains("report.pdf", ex.Message);
        Assert.Contains("notes.docx", ex.Message);
    }

    [Fact]
    public async Task ResolveAsync_MissingFile_MarksTruncationInsteadOfSilentlyDropping()
    {
        var many = Enumerable.Range(0, 36)
            .Select(i => FileSystemEntry.File($"file{i:D2}.pdf", $"file{i:D2}.pdf", 100))
            .ToArray();

        _storageMock
            .Setup(s => s.ReadFileStreamAsync("nope.pdf", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new FileNotFoundException());
        _storageMock
            .Setup(s => s.ListDirsAndFilesAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(many);

        var resolver = CreateResolver(new McpConfig { MaxListedFiles = 20 });

        var ex = await Assert.ThrowsAsync<FileNotFoundException>(() =>
            resolver.ResolveAsync(new FileInput { FilePath = "nope.pdf" }));

        // An unmarked cut-off reads as "the file really is not there" and sends the
        // caller looking in the wrong place.
        Assert.Contains("...and 16 more", ex.Message);
    }
}

using System.ComponentModel;

namespace GroupDocs.Mcp.Core;

/// <summary>
/// How a tool receives a document: by name in storage, or inline as base64.
/// </summary>
/// <remarks>
/// Two valid shapes:
/// <list type="bullet">
/// <item><c>filePath</c> (or <c>fileName</c>) — a file already in the configured storage;</item>
/// <item><c>fileContent</c> + <c>fileName</c> — the bytes inline, base64-encoded.</item>
/// </list>
/// <c>fileName</c> on its own resolves from storage exactly like <c>filePath</c>. It is accepted
/// because the tool descriptions tell callers to "just pass the filename the user provided", and
/// that form used to throw.
/// </remarks>
public class FileInput
{
    [Description(
        "Name or path of a file in the configured storage, e.g. 'report.pdf' or 'output/report.pdf'. " +
        "Use this for files the server can already see.")]
    public string? FilePath { get; set; }

    [Description(
        "Base64-encoded file content, for passing a file that is not in storage. " +
        "Must be accompanied by fileName so the format can be determined.")]
    public string? FileContent { get; set; }

    [Description(
        "Filename with extension, e.g. 'report.pdf'. Required alongside fileContent. " +
        "May also be used on its own as an alias for filePath.")]
    public string? FileName { get; set; }
}

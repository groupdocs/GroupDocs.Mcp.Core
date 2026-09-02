using System.Text;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace GroupDocs.Mcp.Core.Diagnostics;

/// <summary>
/// Turns tool exceptions into the errors-as-text contract, with <c>isError: true</c>.
/// </summary>
/// <remarks>
/// Registered once by <c>AddGroupDocsMcp()</c>, so every tool in every server is covered without
/// per-tool code.
///
/// Without it, three things went wrong at the protocol boundary:
/// <list type="bullet">
/// <item>tools resolve their file <i>outside</i> the try block, so resolver failures escaped and
/// the SDK replaced them with <c>"An error occurred invoking '&lt;tool&gt;'"</c> — discarding the
/// available-files listing the tool descriptions promise;</item>
/// <item>a missing required parameter was equally opaque, so a caller could not self-correct;</item>
/// <item>tools return <c>string</c>, so engine failures came back success-shaped and
/// <c>isError</c> was unreachable — the flag meant "we crashed", not "the operation failed".</item>
/// </list>
/// With the filter, every failure carries both a readable message and <c>isError: true</c>, so a
/// client needs one check rather than string-matching on error prose.
/// </remarks>
public static class ToolErrorFilter
{
    /// <summary>Maximum inner-exception depth included in the message.</summary>
    private const int MaxInnerDepth = 5;

    public static McpRequestFilter<CallToolRequestParams, CallToolResult> Create(ILogger logger)
        => next => async (context, cancellationToken) =>
        {
            try
            {
                return await next(context, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Cancellation is not a tool failure — let it propagate so the SDK can
                // answer the cancelled request properly.
                throw;
            }
            catch (FileNotFoundException ex)
            {
                // The message is already the full recovery text built by FileResolver.
                return Failure(logger, context, ex, ex.Message);
            }
            catch (DirectoryNotFoundException ex)
            {
                return Failure(logger, context, ex, ex.Message);
            }
            catch (ArgumentException ex)
            {
                // Covers both our own input validation and the SDK's
                // "missing a value for the required parameter 'x'".
                return Failure(logger, context, ex, ex.Message);
            }
            catch (Exception ex)
            {
                // Engine failures. Include the type and the inner chain — these are the
                // diagnostics that previously only reached stderr.
                return Failure(logger, context, ex, Describe(ex));
            }
        };

    private static CallToolResult Failure(
        ILogger logger, RequestContext<CallToolRequestParams> context, Exception ex, string text)
    {
        var toolName = context.Params?.Name ?? "<unknown>";
        logger.LogWarning(ex, "Tool {ToolName} failed.", toolName);

        return new CallToolResult
        {
            IsError = true,
            Content = [new TextContentBlock { Text = text }]
        };
    }

    private static string Describe(Exception ex)
    {
        var sb = new StringBuilder();
        sb.Append(ex.GetType().FullName).Append(": ").Append(ex.Message);

        var inner = ex.InnerException;
        for (var depth = 0; inner is not null && depth < MaxInnerDepth; depth++, inner = inner.InnerException)
        {
            sb.Append(" | inner(").Append(depth).Append("): ")
              .Append(inner.GetType().FullName).Append(": ").Append(inner.Message);
        }

        return sb.ToString();
    }
}

# Backlog & Known Issues

Running list of ideas, planned work, and known limitations for **`GroupDocs.Mcp.Core`** — the
shared framework behind all 12 GroupDocs MCP servers. Grouped by topic. Terse on purpose — each
line is a ticket, not an essay. `[ ]` = open, `[x]` = shipped (kept for context).

**Current surface (26.4.1):** `GroupDocs.Mcp.Core` (bootstrap, `McpConfig`, `FileInput`,
`FileResolver`, `OutputHelper`, licensing, diagnostics) + `GroupDocs.Mcp.Local.Storage`,
`GroupDocs.Mcp.AwsS3.Storage`, `GroupDocs.Mcp.AzureBlob.Storage`.

> **All 12 products are pinned to Core 26.4.1** — one uniform baseline, so the next bump lands
> everywhere from the same starting point with no staggered migration.

---

## Confirmed defects — external audit, 2026-08-16

Source: black-box test round across all 12 published MCP servers. **Three defects live here and
appear identically on every product**, so one change fixes all twelve. 46 defects reported, all 46
independently reproduced; a later validation round confirmed each of these three **in this
repository's own source** and found zero false positives.

- [ ] **S1** Passing `fileName` crashes any tool — **High**.
      `FileResolver.ResolveAsync` (`FileResolver.cs:17-27`) accepts `fileContent` or `filePath`
      and throws `ArgumentException` otherwise. `fileName` alone is the form the tool descriptions
      *recommend* ("just pass the filename the user provided") and the schema marks all three
      fields optional with no `oneOf`, so it is the natural, schema-legal choice — and the one
      guaranteed to fail. The client sees only `An error occurred invoking '<tool>'`.
      *Fix:* treat `fileName`-alone as `filePath`. Order becomes
      `fileContent`+`fileName` → `filePath` → `fileName` → throw. **P1**
- [ ] **S2** Missing files (and missing parameters) return an opaque error — **High**.
      `BuildFileNotFoundMessage` (`FileResolver.cs:78-104`) produces genuinely excellent text —
      every file with sizes, plus recovery advice — and it **never leaves the process**, because
      tools call the resolver *outside* their `try` block. Every tool description promises this
      listing. On Total, the same path swallows *missing required parameter* messages, so a client
      can never self-correct. **P1**
- [ ] **S2b** The available-files listing is silently capped at 20 entries — **Low**.
      `Take(20)` at `FileResolver.cs:86`, no truncation marker; storage held 36 during testing and
      it actively misled testers. *Fix:* raise the cap and append `…and N more`. **P1** (ship with
      S2)
- [ ] **S3** `isError` is set on crashes but not on real failures — **Med**.
      Engine failures come back success-shaped with `isError` unset, while resolver crashes *do*
      set it — so the flag means "we crashed", not "the operation failed", and a client cannot
      detect failure programmatically. Unreachable by construction today: tools return
      `Task<string>`. **P1**

### The fix is one file, not sixty — verify this first

First, why the per-tool route is worse than it looks: the `catch` in every tool uses
`resolved.FileName`, so the resolve **cannot simply move inside the `try`** — `resolved` would be
unassigned on that path. Each tool becomes a nullable + `try/finally` restructure that loses
`using var`: 6–10 lines × ~60 tools, each an independent chance to drop a `Dispose`.

The alternative is a first-class extension point, **signature verified** by reflecting
`ModelContextProtocol.Core` 1.1.0 (already pinned in every product):

```csharp
public delegate McpRequestHandler<TParams,TResult> McpRequestFilter<TParams,TResult>(
    McpRequestHandler<TParams,TResult> next);          // middleware-factory shape

McpServerOptions.Filters.Request.CallToolFilters
    : IList<McpRequestFilter<CallToolRequestParams, CallToolResult>>
```

Builder sugar already exists — `McpRequestFilterBuilderExtensions.AddCallToolFilter`, reached via
`McpServerBuilderExtensions.WithRequestFilters`:

```csharp
.WithRequestFilters(f => f.AddCallToolFilter(next => async (ctx, ct) =>
{
    try { return await next(ctx, ct); }
    catch (FileNotFoundException ex) { return ErrorsAsText(ex.Message); }   // S2
    catch (ArgumentException ex)     { return ErrorsAsText(ex.Message); }   // S1
}))
```

`ErrorsAsText` returns `CallToolResult { IsError = true, … }`, closing S3 in the same stroke and
letting the 12 product repos **delete** `ToolError.cs` and their per-tool `catch` blocks.

### Metered verified against real keys — 2026-09-01 ✅

Run end to end on GroupDocs.Comparison 26.9.0 with a live dev metered key pair:

- `get_license_status` reports `mode: metered`, `licensed: true`, `source: metered-keys`.
- Output carries **no evaluation markers**, so the licence reaches the engine — not merely the API.
- The private key appears nowhere in tool output or logs (asserted, not assumed).
- 4/4 metered tests green; 25/25 free tests green.

Both previously open questions are now answered:

- **`GetConsumptionQuantity` does not need a separate network wait** and reflects usage
  immediately — no batching delay, so the consumption assertion can be enforced rather than
  merely observed.
- **`quantity` is account-wide, not process-local.** A freshly started server reported ~1.58e9
  units; a process-local counter would start near zero. Treat `quantity` as a cumulative account
  total and `credit` as the remaining balance. Worth reflecting in the tool description if a
  customer might read `quantity` as "what this server used".
- The per-operation charge is **not constant** (0.00180 and 0.00360 observed for the same
  comparison), so no test should pin a magnitude — only the direction.

### Spike RESOLVED — 2026-08-31 ✅

Run against a live stdio MCP server whose tool is shaped exactly like the real ones
(`Task<string>`, throws from outside any `try`, discovered by `WithToolsFromAssembly`).

**Exceptions do reach the filter** — the concern that the SDK catches lower down is disproven.
Client-visible JSON, captured verbatim:

```jsonc
{"result":{"content":[{"type":"text","text":"Provide either filePath (name in storage) or fileContent (base64) + fileName."}],"isError":true}}
{"result":{"content":[{"type":"text","text":"File 'x.pdf' not found in storage.\n\nAvailable files:\n- a.pdf (1.0 KB)"}],"isError":true}}
{"result":{"content":[{"type":"text","text":"The arguments dictionary is missing a value for the required parameter 'mode'."}],"isError":true}}
{"result":{"content":[{"type":"text","text":"ok result"}]}}     // success unaffected
```

**S1, S2, S2b, S2c and S3 all close here with zero per-tool edits.**

**And products need no registration line.** Registering from `AddGroupDocsMcp()` via
`services.Configure<McpServerOptions>(…)` — which runs *before* `AddMcpServer` and touches only
`IServiceCollection` — works for both:

```csharp
services.Configure<McpServerOptions>(o =>
{
    o.Filters.Request.CallToolFilters.Add(next => async (ctx, ct) =>
    {
        try { return await next(ctx, ct); }
        catch (FileNotFoundException ex) { return ErrorsAsText(ex.Message); }
        catch (ArgumentException ex)     { return ErrorsAsText(ex.Message); }
    });

    // A Core-shipped tool appears in tools/list next to the product's own
    // WithToolsFromAssembly tools, and is callable. Verified.
    o.ToolCollection ??= new();
    o.ToolCollection.Add(McpServerTool.Create(…, new() { Name = "get_license_status" }));
});
```

No product `Program.cs` change is required for either the filter or `get_license_status`.
Reproduction harness kept at `scratchpad/spike/`.

Tracked in Redmine as **SIG-MCP-1** (420600) under epic
[SIGNATURENET-5892](https://issue.saltov.dynabic.com/issues/SIGNATURENET-5892) — but its scope is
**all 12 products, not just Signature**.

---

## Known issues & limitations

- `LicenseManager` sets `_initialized = true` *before* attempting to apply the licence
  (`LicenseManager.cs:28`), so a failed apply is never retried within the process lifetime.
  Deliberate — but it means a transient failure means evaluation mode until restart.
- `SetLicense()` is called at the top of every tool rather than once at startup. The
  `_initialized` guard makes this cheap and correct, but the startup log says nothing about the
  licensing mode until the first tool call.
- The three storage packages version in lockstep with Core (`$(GroupDocsMcpCore)`).
- Core deliberately references **no GroupDocs product package** — only `Microsoft.Extensions.*`.
  Product-specific behaviour arrives through abstract members. Keep it that way.

---

## Tools & functionality

- [ ] **S1/S2/S2b/S3** — see above. **P1**
- [ ] `FileInput` schema — keep the per-field descriptions, add `oneOf`
      (`filePath` XOR `fileContent`+`fileName`) so the failing shape stops being schema-legal.
      **P1**
- [ ] **Metered licensing** — `MeteredPublicKey` / `MeteredPrivateKey` on `McpConfig`, read from
      `GROUPDOCS_METERED_PUBLIC_KEY` / `_PRIVATE_KEY`; new
      `protected abstract void SetMeteredKeyCore(string, string)` on `LicenseManager`.
      Resolution order: metered → licence file → evaluation. Warn when only one key is set, and
      when both metered keys and a licence file are configured. **Never log the private key**;
      mask the public key to a prefix. **P1**
- [ ] **`LicenseMode { Evaluation, Licensed, Metered }`** on `ILicenseManager`, plus
      `MeteredConsumption? GetConsumption()` returning **null** (not zero) outside metered mode,
      backed by one further abstract member `ReadConsumptionCore()`. **P1**
- [ ] **`get_license_status` tool shipped in Core** and registered by `AddGroupDocsMcp()`, so it is
      written once rather than twelve times. Closes an audit gap: there is currently **no
      programmatic way for a client to detect evaluation mode**. **P1**
- [ ] Adopt one output-naming policy family-wide (`' (N)'` dedup everywhere) and expose it from
      Core so products stop diverging — Merger/Watermark/Signature/Total dedup, Redaction/Markdown
      silently overwrite. **P2**
- [ ] `*_FILE` secret-file variants for the metered keys — deferred. Revisit if macOS-GUI clients
      (launchd does not inherit shell exports) or Docker secret mounts prove painful in practice.
      **P2**

## Testing & CI

- [ ] `FileResolverTests` — cover every branch of the new resolution order, `fileName`-alone, the
      filter's error shape, the truncation marker, and that **no log line ever contains the private
      key**. **P1**
- [ ] The current suite is mock-only (family-wide: 294/294 product unit tests pass without opening
      a single document). Core's tests should at minimum exercise the real `LocalFileStorage`
      against a temp directory. **P1**
- [ ] Regression guard: adding an abstract member to `LicenseManager` is a **breaking change** for
      all 12 products. Announce it in the changelog and confirm each product compiles before
      release. **P1**

## Documentation & discoverability

- [ ] Document the licensing resolution order and the env-var contract in the README — this is the
      single source other platforms (Java/Python/Node cores) will port from. **P1**
- [ ] Document the errors-as-text + `isError` contract once S3 lands, so products stop
      hand-rolling it. **P1**
- [ ] Note the outbound-egress requirement of metered mode (usage is reported to GroupDocs
      servers) — matters for air-gapped deployments; the licence file remains the offline option.
      **P1**

## Platform & infra (longer-term)

- [ ] Streamable-HTTP transport — lands here once and benefits all products. **P2**
- [ ] Additional storage providers (Google Cloud Storage, generic S3-compatible). **P2**
- [ ] This package is the reference design for `groupdocs-mcp-core` on Java / Python / Node. Keep
      the abstractions portable and resist product-specific leakage. **P2**

---

*Evidence: `TEMP_ThirdPartyAnalysis/README.md` (the three shared defects in full),
`ALL-PRODUCTS-REPORT.md` §4 (scope confirmed on 12/12 products), `VALIDATION-REPORT.md`
(source-level confirmation). Conventions: any behaviour change ships with a `changelog/NNN-*.md`
entry and a CalVer bump; a Core bump requires a coordinated pass across all 12 product repos.*

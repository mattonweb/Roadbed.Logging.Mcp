# CLAUDE.md — Roadbed.Logging.Mcp

Orientation for Claude Code sessions in this repo. The README describes the server (a read-only MCP server over the
`Roadbed.Logging` schema) and its tool catalog; `docs/` holds the design. This file exists to hold what the shared
skills require a repo to state for itself. It is not a working agreement: none has been written for this repo.

## Skills: reached by junction, owned by Matt

`.claude/skills/` holds 2 Windows junctions, never copies, each gitignored (`REPO-SETUP.md` in the Signalbox repo):

| Skill | Home | What it governs |
|---|---|---|
| `code-csharp-standards` | `C:\Source\Signalbox\skills\` | House C# rules: file layout, type taxonomy, method shape, the 500-line ceiling, allowed language features (C# 14). Its enforceable half is `src/.editorconfig` plus the analyzers |
| `code-roadbed-csharp` | `C:\Source\Roadbed\skills\` | Code that consumes the vendored `Roadbed.*` libraries |

⛔ **Matt owns every skill. Never edit a file under `.claude/skills/`**, however wrong it looks; report the
defect in your hand-back. Each skill there is a Windows junction to its home repo, never a copy, and gitignored
(`REPO-SETUP.md` in the Signalbox repo), so an edit through it lands in another repo's working tree.

**The 500-line ceiling (`code-csharp-standards` section 8): the named-exception list for this repo is EMPTY.** 1 production files exceed it today (measured 2026-10-15, counting every line, excluding test
projects, generated and designer files and migrations): `LoggingRepository.cs` 1,291. **They are migration debt, not
exceptions**: do not add to them, and do not refactor them unless a build order says to. A file that needs to
exceed the ceiling gets its entry here, with its line count and a reason, inside the commit that creates it.
Re-derive the list before relying on it:

```bash
find src -name '*.cs' -not -path '*/bin/*' -not -path '*/obj/*' -not -name '*.g.cs' -not -name '*.Designer.cs' -not -ipath '*/migrations/*' | grep -v -i -E '/[^/]*\.Tests?(\.[^/]*)?/' | xargs wc -l | awk '$1>500 && $2!="total"'
```

The house `.editorconfig` (`src/.editorconfig`) and `.gitattributes` are verbatim copies of Signalbox's root files,
applied 2026-10-15. A copy that differs from Signalbox is drift: it is fixed in Signalbox first, never here
(`code-csharp-standards` rule 19).

# Contributing to clici

Thanks for looking. clici is a small, deliberately narrow tool, and the bar for
changing what it does to someone's clipboard is high. This document explains
that bar so a pull request does not come as a surprise.

## The one rule that matters

**clici rewrites the clipboard silently.** There is no confirmation step and no
undo. A wrong decision does not annoy the user — it destroys the thing they
copied, usually without them noticing until the paste is already somewhere
else.

So the project is biased hard toward *refusing*. When a copy cannot be
classified with confidence, the correct behavior is to leave it alone and let
it fall through unchanged. A false refusal costs a user nothing; a false
rewrite costs them their data.

Concretely, that means a change is usually judged on what it *stops* doing:

- Ambiguous input is left unchanged rather than handled heuristically.
- A new signal must show *positive evidence*, not merely the absence of
  contrary evidence.
- "Probably fine" is a rejection, not an approval.

If you find yourself reaching for a ratio, a threshold vote, or a "most lines
look like X" rule, that is the shape the project has already rejected once. See
the conflict-based classifier in `MarginNormalizer` and the seam rules in
`WrappedLineJoiner` for what the alternative looks like.

## Scope

clici corrects a common left margin on multiline text copied from terminals,
and rejoins a line the terminal wrapped. That is the whole product.

**Current limitations** and **Planned next steps** in the README describe what
is known-missing and what is intended. Before proposing something outside that,
please open an issue first — a large PR that widens the scope is likely to be
declined regardless of how good the code is.

## Getting set up

Requirements: Windows 10 or later, and the .NET 10 SDK.

```powershell
dotnet restore clici.sln
dotnet build clici.sln --configuration Release --no-restore
dotnet test clici.sln --configuration Release --no-build
```

`clici.Core` targets plain `net10.0` and has no Windows UI dependency, so the
normalization and joining logic is testable anywhere. `clici.App` targets
`net10.0-windows` and needs Windows.

To run it:

```powershell
dotnet run --project src/clici.App/clici.App.csproj --configuration Release
```

## Architecture, briefly

The split exists to keep the decisions testable:

- **`clici.Core`** — pure text decisions. `MarginNormalizer` and
  `WrappedLineJoiner` take a string and return a result, with no clipboard, no
  Windows, and no I/O. New classification logic belongs here, with unit tests.
- **`clici.App`** — the tray app, the clipboard listener, and the Win32 edges.
  `ClipboardNormalizationCoordinator` is the ordered decision pipeline that
  gates every rewrite; the numbered comments in it are the specified order and
  the order matters. Windows-bound dependencies sit behind interfaces
  (`IClipboardService`, `IForegroundProcessProvider`, `IStartupRegistryStore`)
  so the pipeline can be tested without a clipboard.

Requirements are numbered in `docs/introductory-slice-spec.md` (NORM, JOIN,
PROC, CLIP, CONF). If you change specified behavior, update the requirement in
the same PR and reference its ID in the commit message.

## Tests

Behavior changes need tests. Two things we care about more than coverage:

- **Test the refusal, not just the success.** Most bugs in this codebase have
  been "it rewrote something it should have left alone", so the interesting
  test is usually the one asserting `NotEligible` and an unchanged string.
- **Use realistic fixtures.** A fixture built from `new string('a', 80)` can
  encode input a terminal cannot actually produce, and a test built on one can
  end up asserting a bug. Prefer real wrapped commands and real copied output.

## Commits and pull requests

Commit messages use [Conventional Commits][cc] (`fix:`, `feat:`, `docs:`,
`ci:`, `build:`, `chore:`), with a body explaining *why*. Reference a
requirement ID when one applies.

[cc]: https://www.conventionalcommits.org/

The pull request template covers verification and the documentation sync that
`docs/release-checklist.md` requires. Please fill in what you actually ran — if
you could not test something, say so plainly rather than leaving it implied.
That is more useful than a checked box.

CI runs the build, the tests, and a whitespace check on `windows-latest`.

## Reporting bugs

Use the issue templates. Please do not paste clipboard content you would not
want published — a redacted or synthetic reproduction is always fine.

For security issues, see [SECURITY.md](SECURITY.md) and report privately.

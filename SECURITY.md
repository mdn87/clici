# Security policy

## Reporting a vulnerability

Please report security issues **privately**, not as a public issue, using
[GitHub's private vulnerability reporting][report] on this repository.

[report]: https://github.com/mdn87/clici/security/advisories/new

Include what you did, what happened, and the clici version. Please do not
include real clipboard contents — a synthetic reproduction is always fine.

clici is maintained by one person as a side project. Expect an initial reply
within about two weeks. There is no bounty.

## Supported versions

Only the latest release is supported. clici has not reached 1.0, and fixes go
onto `main` rather than onto older tags.

## What is in scope

clici reads and rewrites the Windows clipboard, so the interesting failures are
about content and privacy rather than remote attack:

- a rewrite that **adds** `CanIncludeInClipboardHistory` or
  `CanUploadToCloudClipboard` when the source did not set them, or that drops a
  privacy format the source did set;
- processing an item marked `ExcludeClipboardContentFromMonitorProcessing`;
- clipboard contents or copied fragments appearing in the diagnostic log, which
  is specified to record only timestamps, process names, decision types,
  exception types, and aggregate line counts;
- normalizing or joining a copy from a process outside the configured
  allowlist;
- a crash, hang, or unbounded memory growth reachable from clipboard content.

## What is out of scope

- **The self-signed installer.** Releases are signed with a self-signed
  certificate, so Windows SmartScreen warns on first run. This is a known
  property of the distribution, not a vulnerability. Verify the download
  against the checksum on the release.
- **Third-party clipboard managers** recording both the source copy and clici's
  rewrite. clici cannot prevent another process from reading the clipboard.
- **Source attribution being imperfect.** The clipboard owner process is not a
  security boundary; clipboard brokers and ownerless states exist, and
  integrated-terminal hosts share one process across editor and terminal. See
  **Current limitations** in the README.
- Anything requiring an attacker who already runs code as your Windows user. At
  that point the clipboard is theirs regardless of clici.

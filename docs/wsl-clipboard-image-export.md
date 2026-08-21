# WSL clipboard image export

clici can copy the current Windows clipboard image to a stable PNG file without
registering or replacing the Windows screenshot shortcut. `Win+Shift+S` remains
owned by Snipping Tool. After Windows places the completed snip on the
clipboard, clici observes the normal clipboard notification and writes a PNG
copy to the configured destination.

This is intended for contained WSL environments where Windows may deliberately
place a selected artifact into the Linux filesystem, but WSL cannot mount or
execute anything from Windows.

## Configure the destination

Open `%LOCALAPPDATA%\clici\config.json` and set
`clipboardImageExportPath` to a fully qualified local Windows path or a local
WSL UNC path. JSON requires each backslash to be escaped.

```json
{
  "clipboardImageExportPath": "\\\\wsl.localhost\\Ubuntu\\home\\your-linux-user\\agent-sandbox\\drop\\clipboard.png"
}
```

Restart clici after editing the file.

The example is visible inside WSL as:

```text
/home/your-linux-user/agent-sandbox/drop/clipboard.png
```

The parent directory is created when needed. Each new clipboard image replaces
the stable destination through a same-directory temporary file, so a WSL agent
should not observe a partially written PNG.

Set the field to an empty string to disable image export:

```json
{
  "clipboardImageExportPath": ""
}
```

## Archived copies

The stable destination is overwritten by every new image, so a second snip
taken before an agent reads the first would otherwise destroy it. Each export
therefore also writes a timestamped copy beside the stable file:

```text
drop/
  clipboard.png                        newest image, stable path
  clipboard-20260821-161622-500.png
  clipboard-20260821-162345-250.png    same bytes as clipboard.png
```

The archive is written before the stable destination, so an image is never
replaced by one that has no archived copy of its own. Names carry the export
time to the millisecond and sort chronologically as plain text, so the newest
archive is the last one an ordinary `ls` prints.

`clipboardImageExportHistory` sets how many archives to keep (default `20`,
maximum `1000`). The oldest beyond that count are deleted after each export.
Set it to `0` to keep only the stable destination and restore plain overwrite
behavior:

```json
{
  "clipboardImageExportHistory": 0
}
```

Pruning is best effort: an archive that is locked when its turn comes is left
in place and retried on the next export rather than failing the export.

## Behavior and boundaries

- Any clipboard image is exported, not only images created by Snipping Tool.
- Clipboard contents are read only. clici does not replace the image or remove
  any formats, so normal paste and clipboard history behavior remain unchanged.
- The global `enabled` setting controls image export. `Pause normalization`
  pauses text normalization only and leaves image export active.
- Clipboard items marked with
  `ExcludeClipboardContentFromMonitorProcessing` are not exported. An unreadable
  privacy policy also fails closed.
- The destination must end in `.png`.
- Local drive paths, `\\wsl.localhost\...`, and the legacy `\\wsl$\...`
  form are accepted. Other UNC network shares are rejected so enabling this
  feature does not create a general clipboard-to-network bridge.
- The configured file is a deliberate Windows-to-WSL artifact handoff. It does
  not grant the WSL process access to the Windows clipboard, Windows files, or
  Windows executable interop.

"""Install or disable automatic renderer navigation for this Windows user.

Python 3.11+; no packages, registry changes, system PATH edits, or file deletions.
Profiles load the verified, pinned bundle when opening Windows Terminal shells.
"""
import argparse
import hashlib
import json
import os
from pathlib import Path, PureWindowsPath
import re
import shlex
import shutil
import tomllib
import uuid

UPSTREAM = "092d3acd6bec3e3a14bdc7e7a2810ab628ab759d"
START = "# >>> Clici renderer navigation >>>"
END = "# <<< Clici renderer navigation <<<"
CONFIG_MARKER = "# Clici renderer navigation: classic scrollback"


def digest(data):
    return hashlib.sha256(data).hexdigest()


def read(path):
    return path.read_bytes() if path.exists() else b""


def write(path, data):
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary = path.with_name(path.name + ".clici-" + uuid.uuid4().hex + ".tmp")
    temporary.write_bytes(data)
    temporary.replace(path)


def verify_bundle(directory):
    directory = directory.resolve()
    raw = (directory / "renderer-bundle.json").read_bytes()
    manifest = json.loads(raw)
    if manifest.get("upstreamCommit") != UPSTREAM or manifest.get("version") != "0.162.1":
        raise ValueError("Expected the pinned Codex 0.162.1 renderer bundle.")
    names = set()
    for item in manifest["files"]:
        name = item["path"]
        relative = PureWindowsPath(name)
        if relative.is_absolute() or relative.drive or ".." in relative.parts or ":" in name:
            raise ValueError("Unsafe bundle path.")
        candidate = (directory / name).resolve()
        if not candidate.is_relative_to(directory) or name.lower() in names:
            raise ValueError("Ambiguous bundle path.")
        names.add(name.lower())
        if digest(candidate.read_bytes()).lower() != item["sha256"].lower():
            raise ValueError("Renderer bundle integrity check failed: " + name)
    required = {"bin/codex.exe", "helper/promptnavigation.probe.exe",
                "helper/promptnavigation.probe.dll", "helper/clici.core.dll",
                "helper/promptnavigation.probe.runtimeconfig.json", "enable-renderernavigation.ps1"}
    if not required.issubset(names):
        raise ValueError("Renderer bundle is incomplete.")
    return manifest, raw


def profile_blocks(bundle):
    quoted = "'" + str(bundle).replace("'", "''") + "'"
    activation = "'" + str(bundle / "Enable-RendererNavigation.ps1").replace("'", "''") + "'"
    ps = f"""
{START}
if ($PSVersionTable.PSVersion.Major -ge 7 -and $env:WT_SESSION -and
    -not $env:CODEX_THREAD_ID -and -not (Test-Path Env:NO_COLOR) -and
    -not (Get-Variable CliciRendererPreviousEnvironment -Scope Global -ErrorAction SilentlyContinue)) {{
    try {{ . {activation} -BundleDirectory {quoted} | Out-Null }}
    catch {{ Write-Warning ("Clici navigation was not loaded: " + $_.Exception.Message) }}
}}
{END}
"""
    helper = shlex.quote((bundle / "helper/PromptNavigation.Probe.exe").as_posix())
    binary = shlex.quote((bundle / "bin").as_posix())
    bash = f"""
{START}
if [[ $- == *i* && -n ${{WT_SESSION-}} && -z ${{NO_COLOR+x}} && -z ${{CODEX_THREAD_ID-}} ]]; then
    export CLICI_RENDERER_BRIDGE={helper}
    export CLICI_RENDERER_KEYS=1 CLICI_RENDERER_TERMINALS=1
    unset CLICI_NATIVE_KEY_PROBE
    CLICI_RENDERER_BIN="$(cygpath -u {binary})"
    case ":${{PATH}}:" in
        *":${{CLICI_RENDERER_BIN}}:"*) ;;
        *) export PATH="${{CLICI_RENDERER_BIN}}:${{PATH}}" ;;
    esac
    unset CLICI_RENDERER_BIN
fi
{END}
"""
    return ps, bash


def scrollback_config(raw):
    text = raw.decode("utf-8")
    parsed = tomllib.loads(text.lstrip("\ufeff"))
    newline = "\r\n" if "\r\n" in text else "\n"
    block = CONFIG_MARKER + newline + 'alternate_screen = "never"' + newline
    header = re.search(r"(?m)^[ \t]*\[tui\][ \t]*(?:#[^\r\n]*)?(?:\r?\n|$)", text)
    if header is None:
        if "tui" in parsed:
            raise ValueError("Use a [tui] table before installing; inline/dotted TUI settings are preserved.")
        updated = text + newline + "[tui]" + newline + block
        original = ""
    else:
        rest = text[header.end():]
        boundary = re.search(r"(?m)^[ \t]*\[", rest)
        body = rest[:boundary.start()] if boundary else rest
        key = re.search(r"(?m)^[ \t]*alternate_screen[ \t]*=[^\r\n]*(?:\r?\n|$)", body)
        if key is None and "alternate_screen" in parsed.get("tui", {}):
            raise ValueError("Unsupported alternate_screen spelling; configuration was not changed.")
        original = key.group() if key else ""
        position = header.end() + (key.start() if key else 0)
        if position and not text[:position].endswith(("\n", "\r")):
            block = newline + block
        updated = text[:position] + block + text[position + len(original):]
    checked = tomllib.loads(updated.lstrip("\ufeff"))
    expected = dict(parsed)
    expected["tui"] = dict(parsed.get("tui", {}), alternate_screen="never")
    if checked != expected:
        raise ValueError("Configuration edit changed an unrelated setting.")
    return updated.encode("utf-8"), original, block


def install(bundle, root, powershell, bash, config):
    bundle, root, powershell, bash, config = (p.resolve() for p in (bundle, root, powershell, bash, config))
    if len({powershell, bash, config}) != 3:
        raise ValueError("Profiles and config must be different files.")
    manifest, raw_manifest = verify_bundle(bundle)
    release = root / "releases" / ("codex-0.162.1-" + digest(raw_manifest)[:16])
    state_path = root / "installation.json"
    old = json.loads(state_path.read_text(encoding="utf-8")) if state_path.exists() else {}
    if old.get("enabled"):
        if old["profiles"] != [str(powershell), str(bash)] or old["config"] != str(config):
            raise ValueError("Disable the existing installation before changing target files.")
        if old["bundle"] == str(release):
            verify_bundle(release)
            if all(block.encode("utf-8") in read(path) for path, block in
                   zip((powershell, bash, config), old["blocks"] + [old["config_block"]])):
                return summary(old)
        raise ValueError("Disable the current installation before upgrading or repairing modified startup blocks.")

    profile_bytes = [read(powershell), read(bash)]
    if any(START.encode() in content for content in profile_bytes):
        raise ValueError("An untracked Clici startup block already exists; no settings changed.")
    config_before = read(config)
    config_after, original, config_block = scrollback_config(config_before)
    blocks = profile_blocks(release)
    updates = [(powershell, profile_bytes[0] + blocks[0].encode()),
               (bash, profile_bytes[1] + blocks[1].encode()), (config, config_after)]
    if not release.exists():
        for item in manifest["files"]:
            target = release / item["path"]
            target.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(bundle / item["path"], target)
        write(release / "renderer-bundle.json", raw_manifest)
    verify_bundle(release)

    backup = root / "backups" / uuid.uuid4().hex
    backup.mkdir(parents=True)
    originals = profile_bytes + [config_before]
    for index, content in enumerate(originals):
        write(backup / str(index), content)
    state = dict(enabled=True, version="0.162.1", bundle=str(release),
                 profiles=[str(powershell), str(bash)], blocks=list(blocks),
                 config=str(config), config_original=original, config_block=config_block,
                 config_installed_digest=digest(config_after), backup=str(backup))
    try:
        for path, content in updates:
            write(path, content)
        if Path(__file__).resolve() != root / "install_renderer.py":
            shutil.copy2(Path(__file__), root / "install_renderer.py")
        write(state_path, json.dumps(state, indent=2).encode())
    except Exception:
        for (path, _), content in zip(updates, originals):
            write(path, content)
        raise
    return summary(state)


def summary(state):
    return {key: state[key] for key in ("enabled", "version", "bundle", "profiles", "config")}


def disable(root):
    state_path = root.resolve() / "installation.json"
    state = json.loads(state_path.read_text(encoding="utf-8"))
    if not state["enabled"]:
        return dict(summary(state), config_changed_by_user=False)
    updates = []
    for path, block in zip(state["profiles"], state["blocks"]):
        path = Path(path)
        raw = read(path)
        if raw.count(block.encode()) != 1:
            raise ValueError("A startup block was edited; leave it intact for manual review.")
        updates.append((path, raw.replace(block.encode(), b"", 1)))
    config = Path(state["config"])
    raw = read(config)
    changed = False
    if digest(raw) == state["config_installed_digest"]:
        updates.append((config, (Path(state["backup"]) / "2").read_bytes()))
    elif state["config_block"].encode() in raw:
        restored = raw.replace(state["config_block"].encode(), state["config_original"].encode(), 1)
        tomllib.loads(restored.decode("utf-8-sig"))
        updates.append((config, restored))
    else:
        changed = True  # Preserve a setting the user changed after deployment.
    originals = [(path, read(path)) for path, _ in updates]
    try:
        for path, content in updates:
            write(path, content)
        state["enabled"] = False
        write(state_path, json.dumps(state, indent=2).encode())
    except Exception:
        for path, content in originals:
            write(path, content)
        raise
    return dict(summary(state), config_changed_by_user=changed)


def main():
    user_home = Path.home()
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--bundle", type=Path)
    parser.add_argument("--root", type=Path, default=Path(os.environ.get("LOCALAPPDATA", user_home / "AppData/Local")) / "Clici/renderer-navigation")
    parser.add_argument("--powershell-profile", type=Path, default=user_home / "Documents/PowerShell/Microsoft.PowerShell_profile.ps1")
    parser.add_argument("--bash-profile", type=Path, default=user_home / ".bashrc")
    parser.add_argument("--config", type=Path, default=Path(os.environ.get("CODEX_HOME", user_home / ".codex")) / "config.toml")
    parser.add_argument("--disable", action="store_true")
    args = parser.parse_args()
    if os.name != "nt":
        parser.error("Renderer navigation deployment requires Windows.")
    if args.disable:
        result = disable(args.root)
    elif args.bundle:
        result = install(args.bundle, args.root, args.powershell_profile, args.bash_profile, args.config)
    else:
        parser.error("Choose --bundle or --disable.")
    print(json.dumps(result, indent=2))


if __name__ == "__main__":
    main()

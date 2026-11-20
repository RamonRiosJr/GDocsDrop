#!/usr/bin/env python3
"""
Coqui Cloud Zero-Knowledge Pre-Commit Secret Blocker
- Contains ZERO plaintext secrets: matches irreversible SHA-256 digests + live local vault digests + token regexes.
- Aborts git commit (exit 1) BEFORE any raw password, token, or private key can enter Git history.
"""
import hashlib
import os
import re
import subprocess
import sys
from pathlib import Path

# Irreversible SHA-256 digests of known Coqui/Ramon vault passphrases & tokens (zero plaintext stored)
BLOCKED_SHA256 = {
    "12670d54b149171ab68f90b9f7a2b2059e6273e2c42d797abd06bc830b263b3c",
    "1e3ab6b4e850b63a74e665b90e9e546c347562f6383c3fc602468882b36a71b9",
    "34b61604bdb0c76bf0992120cd95459406088bd08e1f76b99670770cfb856a5a",
    "466db7a53f5f977859dcf69a22d2fd47eaa117213fbd308346ca2fedc55fbf72",
    "597200f647122e52bd8d3f6fb11cb0b8abe4ba1ef921e627af3e202641962bc6",
    "7711b5acaec2dc371f1f3fe4e5087989f95faf5448c35f46826d8691cf5d83ae",
    "83b266f449287e45e4cbdf8dedfc70482eb848929fb105aa66a0ee025cdfb425",
    "912b55c0ea226721c2d20b3001544115fedbe1cfd617c8aa8bd1d98fbf590581",
    "977343afbb71de8f3da707444e94dd2e6ad6a9f656bde45328b5225c5c69769c",
    "9d511b557e93a69255860c717dff96dbf43b3bcb0f6ab4c081a457f9e4958deb",
    "b54d9942edad172f4e7b0e300e24262ea4bbba3da6092470e54ab9afff86eb4f",
    "b5cd7b39465783367130ecb3a3fa1f651654fdd744e6c3f40da693708ecb12fb",
    "c93b115b36cdf424445944e9d377e6adf49c18f6fc1a1543481abafc60678d8b",
    "d781d7659f0a9c311e606a1cc4dc922c1e35728b7adc9795de272433e5a74746",
    "fa7f5504ceeac108b4a83b47a06adfe1fb8bef270793dd59999243af3ce655ad",
}

BLOCKED_FILE_PATTERNS = [
    re.compile(r"(^|/)(\.env(\..+)?)$", re.I),
    re.compile(r"(^|/).*(_token|master_credentials|id_rsa|id_ed25519|antigravity_key).*", re.I),
]

ALLOWED_FILE_SUFFIXES = (".env.example", ".env.sample", ".env.template")

SECRET_REGEXES = [
    ("Cloudflare API Token", re.compile(r"\bcfut_[A-Za-z0-9_]{20,}\b")),
    ("Google OAuth Client Secret", re.compile(r"\bGOCSPX-[A-Za-z0-9_-]{15,}\b")),
    ("Google / Gemini API Key", re.compile(r"\bAIza[0-9A-Za-z_-]{35}\b")),
    ("GitHub Personal Access Token", re.compile(r"\b(?:ghp_|github_pat_)[A-Za-z0-9_]{25,}\b")),
    ("OpenAI / Anthropic Secret Key", re.compile(r"\bsk-(?:proj-|ant-)?[A-Za-z0-9_-]{20,}\b")),
    ("Slack Token", re.compile(r"\bxox[baprs]-[A-Za-z0-9-]{15,}\b")),
    ("Private Cryptographic Key Block", re.compile(r"-----BEGIN [A-Z0-9 ]*PRIVATE KEY(?: BLOCK)?-----")),
]


def load_dynamic_vault_secrets() -> tuple[set[str], list[str]]:
    digests = set(BLOCKED_SHA256)
    literals = []
    vault_path = Path(os.path.expanduser("~")) / ".gemini" / "config" / "MASTER_CREDENTIALS.md"
    if vault_path.exists():
        try:
            txt = vault_path.read_text(encoding="utf-8", errors="ignore")
            for m in re.finditer(r"(?:Password|Passphrase|Token|Secret|Pass)[^:\n]*:\s*`?([^\s`\"'\n]{8,})`?", txt, re.I):
                val = m.group(1).strip()
                if val and not val.startswith("http") and "@" not in val and val.lower() not in ("antigravity-coqui", "antigravity-coqui-master"):
                    digests.add(hashlib.sha256(val.encode("utf-8")).hexdigest())
                    literals.append(val)
        except Exception:
            pass
    return digests, literals


def scan_diff_and_files(staged_files: list[str], diff_text: str, digests: set[str], live_literals: list[str]) -> list[str]:
    violations = []
    for fpath in staged_files:
        if fpath.lower().endswith(ALLOWED_FILE_SUFFIXES):
            continue
        for pat in BLOCKED_FILE_PATTERNS:
            if pat.search(fpath):
                violations.append(f"Blocked secret filename: {fpath}")

    current_file = "<diff>"
    for line in diff_text.splitlines():
        if line.startswith("+++ b/"):
            current_file = line[6:].strip()
            continue
        if not line.startswith("+") or line.startswith("+++"):
            continue
        if current_file.lower().endswith(ALLOWED_FILE_SUFFIXES) or current_file.endswith(".githooks/secret_blocker.py"):
            continue
        added = line[1:]

        # Regex checks
        for label, rx in SECRET_REGEXES:
            if rx.search(added):
                violations.append(f"[{current_file}] Detected {label}")

        # Live substring check (if local MASTER_CREDENTIALS.md is present on machine)
        for lit in live_literals:
            if lit in added:
                violations.append(f"[{current_file}] Detected known vault credential from MASTER_CREDENTIALS.md")
                break

        # Zero-knowledge SHA-256 token check (works everywhere including GitHub Actions CI without MASTER_CREDENTIALS.md)
        for tok in re.split(r"[\s`'\"=,:;()\[\]{}<>]+", added):
            if len(tok) >= 7:
                h = hashlib.sha256(tok.encode("utf-8")).hexdigest()
                if h in digests:
                    violations.append(f"[{current_file}] Detected blocked credential (SHA-256 digest match {h[:10]}...)")
    return violations


def main() -> int:
    ci_mode = "--ci" in sys.argv
    digests, live_literals = load_dynamic_vault_secrets()

    if ci_mode:
        # In CI mode, scan tracked filenames + latest commit diff (HEAD~1..HEAD)
        res_files = subprocess.run(
            ["git", "ls-files"],
            capture_output=True, text=True, encoding="utf-8", errors="ignore"
        )
        tracked_files = [f.strip().replace("\\", "/") for f in res_files.stdout.splitlines() if f.strip()]
        res_diff = subprocess.run(
            ["git", "diff", "HEAD~1..HEAD", "-U0", "--diff-filter=ACMR"],
            capture_output=True, text=True, encoding="utf-8", errors="ignore"
        )
        violations = scan_diff_and_files(tracked_files, res_diff.stdout, digests, live_literals)
    else:
        # Pre-commit mode: scan staged files and staged diff
        res_files = subprocess.run(
            ["git", "diff", "--cached", "--name-only", "--diff-filter=ACMR"],
            capture_output=True, text=True, encoding="utf-8", errors="ignore"
        )
        staged_files = [f.strip().replace("\\", "/") for f in res_files.stdout.splitlines() if f.strip()]
        if not staged_files:
            return 0
        res_diff = subprocess.run(
            ["git", "diff", "--cached", "-U0", "--diff-filter=ACMR"],
            capture_output=True, text=True, encoding="utf-8", errors="ignore"
        )
        violations = scan_diff_and_files(staged_files, res_diff.stdout, digests, live_literals)

    if violations:
        print("\n" + "=" * 74, file=sys.stderr)
        print("🚨 BLOCKED BY COQUI CLOUD SECRET GUARD (.githooks/secret_blocker.py) 🚨", file=sys.stderr)
        print("=" * 74, file=sys.stderr)
        for v in dict.fromkeys(violations):
            print(f"  ❌ {v}", file=sys.stderr)
        print("\nRemove the secret and store it in passbolt.coqui.cloud or MASTER_CREDENTIALS.md.", file=sys.stderr)
        print("=" * 74 + "\n", file=sys.stderr)
        return 1

    return 0


if __name__ == "__main__":
    sys.exit(main())

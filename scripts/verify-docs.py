#!/usr/bin/env python3
"""Docs checker: relative links resolve, every docs/ markdown file is reachable from docs/README.md,
archive-tag permalinks resolve, docs/superpowers holds no transient files on main, and the bilingual set has an
English `X.md` and a German `X.de.md` that link each other.

Usage: python scripts/verify-docs.py [--repo DIR] [--archive-tag TAG] [--fail-on-transient] [--bilingual]
Exit code 1 when any error is found. Standard library only.
"""
import argparse
import os
import re
import subprocess
import sys

REPO_URL = "https://github.com/Czarnak/tia-portal-mcp/"
TRANSIENT_DIRS = ("docs/superpowers/plans/", "docs/superpowers/specs/", "docs/superpowers/evidence/")
INLINE = re.compile(r"\[(?:[^\[\]]|\[[^\]]*\])*\]\(\s*(<[^>]*>|[^()\s]*(?:\([^()\s]*\)[^()\s]*)*)")
CODE_SPAN = re.compile(r"`[^`\n]*`")
FENCE = re.compile(r"^\s{0,3}(```+|~~~+)")
INDEX = "docs/README.md"
# Documents that must exist in English (X.md) and German (X.de.md): these files and every *.md directly in these directories.
BILINGUAL_ROOT = ("README.md", "CONTRIBUTING.md", "SECURITY.md", "ROADMAP.md", "THIRD_PARTY_NOTICES.md",
                  "docs/README.md", "docs/FORK_FEATURES.md")
BILINGUAL_DIRS = ("docs/guides", "docs/SupportedOperations")


def listed(repo, *pathspec):
    """Tracked and untracked-but-not-ignored files, so new files are checked before `git add`."""
    out = subprocess.run(["git", "-C", repo, "ls-files", "-co", "--exclude-standard", "--", *pathspec],
                         capture_output=True, text=True, check=True).stdout
    return sorted({p for p in out.splitlines() if p and os.path.exists(os.path.join(repo, p))})


def links(repo, rel):
    """Yield (line number, raw target) for inline links outside fenced code blocks."""
    fence = None
    with open(os.path.join(repo, rel), encoding="utf-8") as handle:
        for number, line in enumerate(handle, 1):
            marker = FENCE.match(line)
            if marker:
                fence = None if fence == marker.group(1)[0] else (fence or marker.group(1)[0])
                continue
            if fence:
                continue
            for match in INLINE.finditer(CODE_SPAN.sub("", line)):
                yield number, match.group(1).strip("<>")


def resolve(source, target):
    """Return a repo-relative path for a local target, or None when the target is external or an anchor."""
    target = target.split("#", 1)[0].split("?", 1)[0]
    if not target or re.match(r"^[a-z][a-z0-9+.-]*:", target, re.I):
        return None
    base = os.path.dirname(source)
    return os.path.normpath(os.path.join(base, target)).replace(os.sep, "/")


def tag_has(repo, tag, path):
    return subprocess.run(["git", "-C", repo, "cat-file", "-e", f"{tag}:{path}"], capture_output=True).returncode == 0


def check(repo, archive_tag, fail_on_transient, bilingual_set=False):
    errors, warnings = [], []
    docs = listed(repo, "*.md")
    graph = {}
    for rel in docs:
        graph[rel] = set()
        for number, target in links(repo, rel):
            if target.startswith(REPO_URL):
                kind, _, tail = target[len(REPO_URL):].split("#", 1)[0].partition("/")
                ref, _, path = tail.partition("/")
                if kind not in ("blob", "tree"):
                    continue
                path = path.rstrip("/")
                if ref == archive_tag and archive_tag:
                    if not tag_has(repo, archive_tag, path):
                        errors.append(f"{rel}:{number}: {path} is not in tag {archive_tag}")
                elif ref == "main" and not os.path.exists(os.path.join(repo, path)):
                    errors.append(f"{rel}:{number}: absolute main link target is missing: {path}")
                continue
            resolved = resolve(rel, target)
            if resolved is None:
                continue
            full = os.path.join(repo, resolved)
            if not os.path.exists(full):
                errors.append(f"{rel}:{number}: broken link {target}")
                continue
            if os.path.isdir(full):
                resolved = resolved.rstrip("/") + "/README.md"
            graph[rel].add(resolved)
    reachable, queue = {INDEX}, [INDEX]
    while queue:
        for nxt in graph.get(queue.pop(), ()):
            if nxt not in reachable:
                reachable.add(nxt)
                queue.append(nxt)
    for rel in listed(repo, *TRANSIENT_DIRS):
        (errors if fail_on_transient else warnings).append(f"{rel}: transient working file present")
    for rel in docs:
        if rel.startswith("docs/") and not rel.startswith(TRANSIENT_DIRS) and rel not in reachable:
            errors.append(f"{rel}: not reachable from {INDEX}")
    errors.extend(bilingual(repo, docs, graph, bilingual_set))
    return errors, warnings


def bilingual(repo, docs, graph, require_set):
    """Every X.de.md has its English X.md and the two link each other; with require_set, every document of the
    bilingual set has both."""
    errors, present = [], set(docs)

    def links_to(source, target):
        if target in graph.get(source, ()):
            return True
        # README.md is also the NuGet readme and links absolutely.
        with open(os.path.join(repo, source), encoding="utf-8") as handle:
            return REPO_URL.replace("Czarnak/tia-portal-mcp", "Birger90/tia-portal-mcp-server") + "blob/main/" + target in handle.read()

    for rel in docs:
        if rel.endswith(".de.md"):
            english = rel[:-len(".de.md")] + ".md"
            if english not in present:
                errors.append(f"{rel}: German version without its English {english}")
            else:
                for source, target in ((rel, english), (english, rel)):
                    if not links_to(source, target):
                        errors.append(f"{source}: no language switch link to {target}")
        elif require_set and (os.path.dirname(rel) in BILINGUAL_DIRS or rel in BILINGUAL_ROOT):
            if rel[:-len(".md")] + ".de.md" not in present:
                errors.append(f"{rel}: German version {rel[:-3]}.de.md is missing (bilingual set)")
    return errors


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--repo", default=os.getcwd())
    parser.add_argument("--archive-tag", default="")
    parser.add_argument("--fail-on-transient", action="store_true")
    parser.add_argument("--bilingual", action="store_true", help="require the German version of every bilingual-set document")
    args = parser.parse_args()
    errors, warnings = check(args.repo, args.archive_tag, args.fail_on_transient, args.bilingual)
    for message in warnings:
        print("warning:", message)
    for message in errors:
        print("error:", message)
    print(f"{len(errors)} error(s), {len(warnings)} warning(s)")
    return 1 if errors else 0


if __name__ == "__main__":
    sys.exit(main())

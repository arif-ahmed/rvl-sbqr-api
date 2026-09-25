---
name: vapt-audit
description: Use when the user asks for a VAPT audit, VAPT compliance check, security audit, OWASP API Top 10 or ASVS assessment of this codebase, or a pass/fail security report against the public (or internal-admin) API endpoints. Runs static code analysis plus safe dynamic probes, then writes a dated compliance report to docs/security/ with per-rule pass/fail verdicts, evidence, and rule references.
---

# VAPT Audit (opencode entry point)

This skill shares one implementation with the Claude Code skill in this
repo. The canonical workflow, rule catalogs, probe script, and report
template live under `.claude/skills/vapt-audit/` — do not duplicate them,
they are agent-readable markdown/scripts and work identically here.

## Run it like this

1. **Read the canonical skill first**:
   `.claude/skills/vapt-audit/SKILL.md` — follow its workflow exactly
   (scope rules, static pass, dynamic pass, verdict rubric, report writing,
   safety rules).
2. **Rule catalogs** (read both in full during the static pass):
   - `.claude/skills/vapt-audit/rules/owasp-api-top10.md`
   - `.claude/skills/vapt-audit/rules/asvs-checks.md`
3. **Dynamic probes** (static+dynamic mode, localhost only):
   ```
   python .claude/skills/vapt-audit/scripts/vapt_probe.py --base-url http://localhost:5001 --output <temp>/vapt-probe.json
   ```
4. **Report**: fill `.claude/skills/vapt-audit/templates/report-template.md`
   and write to `docs/security/vapt-report-YYYY-MM-DD.md`.

## opencode-specific notes

- Where the canonical skill says "AskUserQuestion", use the `question` tool.
- Shell is pwsh on this machine — quote paths with backslashes.
- Everything else (verdicts, charter-deferred annotations, safety rules,
  localhost-only probing, report-only stance) is identical.

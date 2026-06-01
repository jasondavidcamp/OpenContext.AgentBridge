# PowerShell

Use this skill when working on PowerShell scripts, modules, build scripts, automation helpers, or Windows-first infrastructure tooling.

## Operating Principles

- Treat PowerShell changes as automation changes: inspect first, edit narrowly, and validate with commands that exercise the changed path.
- Prefer `replace_text` for small exact substitutions after reading the file. Use `apply_patch` for broader multi-line edits. Use `write_file` only for new files or when replacing a tiny fixture is clearer than patching.
- Keep scripts readable for infrastructure engineers who may need to troubleshoot them under pressure.
- Preserve existing parameter names, pipeline behavior, output shape, and exit-code behavior unless the user explicitly asks to change them.
- Do not introduce network calls, credential prompts, destructive filesystem operations, service changes, registry edits, or remote execution unless the user specifically asked for that behavior.
- Never hard-code secrets, hostnames, internal URLs, tokens, usernames, passwords, certificate thumbprints, or environment-specific paths. Use parameters, environment variables, or clearly named placeholders.

## Inspection Checklist

Before editing, inspect:

- The target `.ps1`, `.psm1`, or `.psd1` file.
- Any nearby README or usage examples.
- Existing function names, parameter attributes, output conventions, and error handling.
- Existing validation commands or tests, such as Pester tests.

When the repository has no tests, create the smallest safe validation command that exercises the behavior without requiring internal systems.

## Style Guidance

- Use approved verbs for functions when practical.
- Prefer `[CmdletBinding()]` and explicit `param(...)` blocks for reusable scripts and functions.
- Use meaningful parameter names and avoid terse aliases in committed scripts.
- Prefer object output for automation-friendly functions. Use `Write-Verbose` for diagnostic detail instead of `Write-Host`, unless the script is intentionally interactive.
- Use `Join-Path` for path construction.
- Use `Set-StrictMode -Version Latest` in new standalone scripts when it will not conflict with existing style.
- Keep error messages actionable and include the relevant path or parameter name.

## Validation

Good validation commands include:

```powershell
pwsh -NoProfile -File .\script.ps1
pwsh -NoProfile -Command "Import-Module .\Module.psm1 -Force; Get-Command -Module ModuleName"
pwsh -NoProfile -Command "Invoke-ScriptAnalyzer -Path ."
pwsh -NoProfile -Command "Invoke-Pester"
```

If `pwsh` is unavailable but Windows PowerShell is expected, use:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\script.ps1
```

Report exactly what validation ran and whether it passed. If a validation command is unavailable, say so and choose the next safest local check.

## Security And Safety

- For code that touches files, protect against accidental broad paths. Resolve and validate paths before deletion, movement, or recursive operations.
- Avoid `Invoke-Expression`.
- Avoid command strings when native commands or splatting are safer.
- For destructive commands, require explicit user intent and prefer `-WhatIf` support.
- For remote systems such as Azure DevOps Server, Splunk, Oracle, or Windows services, create interfaces or placeholders first unless credentials and endpoints are explicitly configured in the workspace.

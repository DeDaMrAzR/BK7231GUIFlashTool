# OpenIOT flasher Agent Guidance

This repository contains a .NET Framework 4.7.2 WinForms application that can read,
erase, and write firmware on multiple embedded platforms. Treat hardware-facing paths
as potentially destructive even when a command or button is labeled as a test.

## Start here

1. Confirm the repository path, branch, revision, and working-tree state.
2. Read [the memory index](.agents/memory/INDEX.md), then load only memory relevant
   to the current task.
3. Classify the request as investigation, application change, platform/flasher work,
   protocol analysis, log/report review, verification, guidance evolution, or memory
   maintenance.
4. Read the routed rule and skill before acting.
5. Separate source-confirmed facts, supplied observations, inferences, hypotheses, and
   unperformed proposals.

## Authorization boundaries

Without explicit approval for the specific action, do not:

- build the solution, project, Docker image, installer, package, or release;
- run any test or script, including files under `tests/`;
- launch the GUI or a built executable;
- open a serial port, connect to a device, scan a network, or send a device command;
- read, erase, or write device flash, eFuse, OTP, ROM, or configuration;
- download firmware, dependencies, loaders, packages, or other remote artifacts;
- stage, commit, push, tag, publish, release, or rewrite Git history;
- update repository guidance or persistent memory without explicit approval.

Read-only source inspection and read-only Git provenance commands are allowed when
they are relevant. Internet research does not authorize downloading project artifacts.

## Routing

| Task | Read first | Use skill |
|---|---|---|
| Diagnose a bug or regression | [investigation rule](.agents/rules/core/evidence-and-provenance.md) | [investigate-issue](.agents/skills/investigate-issue/SKILL.md) |
| Locate ownership or call flow | [application architecture](.agents/rules/architecture/application-boundaries.md) | [trace-source-path](.agents/skills/trace-source-path/SKILL.md) |
| Change C# or WinForms UI | [WinForms rule](.agents/rules/architecture/winforms-and-generated-files.md) | [change-winforms](.agents/skills/change-winforms/SKILL.md) |
| Add or modify a chip/platform | [platform integration rule](.agents/rules/flashers/platform-integration.md) | [develop-platform-flasher](.agents/skills/develop-platform-flasher/SKILL.md) |
| Decode a protocol or firmware format | [protocol rule](.agents/rules/protocols/reverse-engineering.md) | [develop-platform-flasher](.agents/skills/develop-platform-flasher/SKILL.md) |
| Review supplied logs or test reports | [log/report rule](.agents/rules/evidence/log-and-report-review.md) | [analyze-log-report](.agents/skills/analyze-log-report/SKILL.md) |
| Review a change or plan validation | [validation rule](.agents/rules/verification/approval-gated-validation.md) | [verify-change](.agents/skills/verify-change/SKILL.md) |
| User says `update skill`, `adapt skill`, or equivalent | [guidance policy](.agents/rules/core/guidance-and-memory.md) | [evolve-project-guidance](.agents/skills/evolve-project-guidance/SKILL.md) |
| User says `remember this`, `update memory`, or equivalent | [guidance policy](.agents/rules/core/guidance-and-memory.md) | [maintain-project-memory](.agents/skills/maintain-project-memory/SKILL.md) |

## Repository invariants

- Trace platform support across `BKType`, `FlashPlatformCatalog`,
  `FlasherFactory`, GUI routing, command-line routing, boot-mode guidance, ROM-read
  catalogs, embedded loaders, and `platforms.md`; one list is not authoritative alone.
- Keep backup/readback distinct from erase/write/test operations. A read path can still
  expose credentials and must not be treated as harmless device access.
- Do not infer protocol meaning from names or neighboring chip families. Preserve exact
  bytes, offsets, sizes, directions, timing, and observed responses.
- Treat `.Designer.cs`, `.resx`, `Resources.Designer.cs`, and `Settings.Designer.cs` as
  generated or tool-owned surfaces. Do not hand-edit them casually.
- Preserve cancellation, port cleanup, bounds checking, verification, and useful error
  classification when changing a flasher.
- The user runs the GUI and performs all hardware tests; ask for a bounded manual test
  and analyze the returned evidence.

## Changes and handoff

Keep edits surgical and do not alter unrelated user changes. Report what source was
inspected, what changed, what was not executed, and what manual evidence is still
needed. Do not claim build-, GUI-, or device-tested status unless the corresponding
authorized action actually ran and its provenance is recorded.

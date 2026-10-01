# User Instruction Memory

This file records user instructions, preferences, and teachings for reference in future interactions.

## Format

### User Instruction Entry
User instruction entries should follow this format:

[User Instruction Summary]
- Date: [YYYY-MM-DD]
- Context: [Mentioned scenario or time]
- Instructions:
  - [Content of user teaching or instruction, described line by line]

### Project Knowledge Entry
Entries discovered by the Agent during task execution should follow this format:

[Project Knowledge Summary]
- Date: [YYYY-MM-DD]
- Context: Discovered by Agent while performing [specific task description]
- Category: [Operations & Deployment|Build Methods|Testing Methods|Troubleshooting & Debugging|Workflow & Collaboration|Environment Configuration]
- Instructions:
  - [Specific knowledge points, described line by line]

## Deduplication Strategy
- Before adding a new entry, check for similar or identical instructions.
- If a duplicate is found, skip the new entry or merge it with the existing one.
- When merging, update the context or date information.
- This helps avoid redundant entries and keeps the memory file tidy.

## Entries

### User Instruction Entry
[User Instruction Summary]
- Date: 2026-09-30
- Context: User requested implementation of a 94-feature CNC machining simulation system under Unity3D with C#.
- Instructions:
  - Use Unity3D as the main engine; the user writes the UI layout themselves.
  - The assistant must only implement the functionality/feature code, not the UI.
  - Output the highest-quality code only: think before writing, check carefully, avoid compile errors and bugs.
  - Support both Chinese and English (zh-CN default, en-US).
  - Coordinate convention: right-handed, Z axis up, unit is mm.
  - The Core layer must not depend on UnityEngine.

[Project Knowledge Summary]
- Date: 2026-09-30
- Context: Discovered by Agent while building and testing the CNC simulation Core library.
- Category: Build Methods / Environment Configuration
- Instructions:
  - .NET SDK 8.0 is installed at `/root/dotnet`; builds require `export DOTNET_ROOT=/root/.dotnet PATH=/root/.dotnet:$PATH`.
  - `libicu72` is required, otherwise dotnet fails with "Couldn't find a valid ICU package".
  - Core-only compile/test harness lives at `/tmp/opencode/corecheck/` (`corecheck.csproj` includes `/workspace/Assets/CncSim/Scripts/Core/**/*.cs` plus `Program.cs` tests).
  - Build command: `cd /tmp/opencode/corecheck && dotnet build corecheck.csproj -v q --nologo`.
  - Run tests: `cd /tmp/opencode/corecheck && dotnet run -v q --nologo` (prints ALL TESTS PASSED on success).
  - Core namespaces are flat under `CncSim.Core` (e.g. `MeshData`, `MeshPrimitives`, `ColorRgba` are in `CncSim.Core`, not sub-namespaces).
  - A Unity folder named `Editor/` has special meaning; code editor sources live in `CodeEditor/` with namespace `CncSim.Core.CodeEditor`.

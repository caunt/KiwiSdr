# Repository Guidelines

## Coding conventions

Follow the existing C# style rules:

- Indentation uses four spaces, with tab width set to four.
- Private fields prefixed with an underscore and method parameters use camelCase.
- Braces sit on their own lines with a blank line before loops and conditional statements.
- Method signatures remain on a single line.
- Identifier names are fully descriptive and never abbreviated.
- Async methods carry an `Async` suffix.
- Pattern matching is preferred over explicit casts.
- `nameof` is used for parameter checks, logging, and exceptions.
- Never use the null-forgiving `!` operator to silence possible null reference warnings.

## Development

- Commit only hunks with actual code changes. All code should use CRLF line endings and existing whitespace should be preserved; never reformat untouched lines.
- Run `dotnet test` to ensure all tests pass whenever C# source files (`*.cs`) are modified. Skip this step if no C# files change.
- Be patient with long-running tests and avoid aborting them early; some may take several minutes to complete.
- After changing source code, run `dotnet build` from the repository root.
- Conventional Commits are required for commit messages.
- Commit messages must include a scope after the type, e.g., `docs(readme): ...`.
- Use only the following Conventional Commit types:
  - `feat` — Features
  - `fix` — Bug Fixes
  - `perf` — Performance Improvements
  - `deps` — Dependencies
  - `revert` — Reverts
  - `docs` — Documentation
  - `style` — Styles
  - `chore` — Miscellaneous Chores
  - `refactor` — Code Refactoring
  - `test` — Tests
  - `build` — Build System
  - `ci` — Continuous Integration
- Commit bodies are required and must include a brief note about any observable behavior change.
- Use the `fix` or `feat` type only when your changes modify the proxy code in `./src`. For documentation, CI, or other unrelated updates, choose a more appropriate type such as `docs` or `chore`.
- Append a [gitmoji](https://gitmoji.dev/specification) after the commit scope, e.g., `feat(api): ✨ add new endpoint`.
- Pull request titles should follow the same Conventional Commits format.
- Pull request descriptions must use the following template:

  ```
  ## Summary
  One-sentence problem and outcome.

  ## Rationale
  Why this is needed; alternatives considered briefly.

  ## Changes
  Concise, high-signal description of what changed.

  ## Verification
  How it was tested; include commands or steps.

  ## Performance
  Before/after numbers if relevant; memory/alloc notes.

  ## Risks & Rollback
  Known risks, how to revert safely.

  ## Breaking/Migration
  Required actions for users, if any.

  ## Links
  Issues, discussions, specs.
  ```

- Never modify `CHANGELOG.md`; it is generated automatically by `release-please` from commit history.

## Documentation

- When adding links to documentation, make the link text bold. For example: `[**link text**](https://example.com)`.
- Never include inline code or backticks inside a link caption; keep code formatting outside the link text.

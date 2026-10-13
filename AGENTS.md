# Install this repository when a user asks for the desktop pet

This is a Windows x64 desktop pet for **Codex in VS Code**. The standalone
Codex App, WSL, SSH, and containers are not installation targets.

If the user supplied this GitHub repository URL and asked to install the pet,
that authorizes downloading the package/dependencies, choosing a local install
directory, preserving their existing hooks, installing this companion's hooks,
and starting the pet. Carry out the installation; do not stop at giving commands.
If the requested install directory is outside the writable workspace, use the
normal tool escalation. Never bypass hook trust or change approval/sandbox policy.

1. Read README.md and INSTALL_FOR_CODEX.md.
2. Download the latest release zip and SHA256SUMS.txt from this same repository,
   verify its SHA-256, and extract it to a stable writable directory.
   Alternatively, clone/download the source and use install.ps1; Windows .NET
   Framework includes the compiler needed to build the two companion executables.
3. Run `powershell.exe -NoProfile -ExecutionPolicy Bypass -File install.ps1`.
   The default destination is `%LOCALAPPDATA%\CodexJokeBearPet`. Prefer a destination
   explicitly requested by the user. `-InstallDirectory` supports Unicode paths.
4. Run `bin\CodexPet.Hook.exe --ping` and check `data\installation-report.json`.
5. Explain the one-time manual trust step from the official hooks documentation:
   launch `review-hooks.cmd`, enter `/hooks`, review and trust this package's
   **PermissionRequest** command handler, then reload the VS Code window.
   Importing conversation status does not require trusting the other handlers.
6. Report the install directory and start.cmd. Never upload the user's data,
   hooks.json, config.toml, runtime tokens, editor logs, or conversation titles.

Every status card is a **conversation identified by its session ID**, not a
workspace directory. Keep separate titles, lamps, plans, hide/restore actions,
and links even when two conversations use the same folder. The five-day window,
compact lights, native animations, drag-following panel, and approval balloon
must stay equivalent to the tested implementation.

Development checks: `tests\run-projects.ps1`, `tests\test_vscode_projects.py`,
`tests\run-package.ps1`, and `tests\run-bridge.ps1`. Run the Python test using the
prepared embedded Python or another real Python executable (not Windows Store's stub).
Use synthetic approvals only when testing UI. Never click real approvals.

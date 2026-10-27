# Security policy

## Reporting a vulnerability

Please report security issues **privately**, through GitHub's
**Security → Report a vulnerability** on this repository, not in a public
issue. Include what you found, how to reproduce it, and what an attacker could
do with it. You should hear back within a week.

Only the latest release is supported; there are no maintained release
branches.

## What is in scope

This is a local app with no network service, so the interesting boundaries
are local ones:

- **Read-only access to the drives.** The app, the collector and the command
  line may send a drive only Identify and Get Log Page requests, through
  `IOCTL_STORAGE_QUERY_PROPERTY`. Any way to make them send anything else (a
  Set Features, a format, a firmware download, any pass-through) is in scope.
- **The optional Intel RST reader.** `NandWatchRstReader.exe` runs as SYSTEM
  from `%ProgramFiles%\NAND Watch\`, started by the "NAND Watch RST Reader"
  task, and sends each drive one command, Get Log Page for the health log,
  through RST's miniport pass-through. Any way for a standard user to make it
  send a different command, run code of their choosing, or write outside
  `%ProgramData%\NAND Watch\rst\` is in scope.
- **The collector task and the app**, which run as the signed-in user. Anything
  that lets another local account read or alter the history in the data
  folder, or the RST reader's report, beyond what that folder's permissions
  allow, is in scope.
- **The installer and uninstaller** (`Install.ps1`, `Uninstall.ps1`), which
  register the tasks, and install the RST reader behind one UAC prompt.

Out of scope: anything requiring an already-elevated attacker, physical access
to an unlocked machine, or a data folder the user deliberately placed where
other accounts can write.

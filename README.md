# SFS Shared Parts

Shared source for Spaceflight Simulator part modules (`SFS.Parts.Modules`).
Consumed as a git submodule by both the private game and the public Modding Toolkit,
mounted at `Assets/Scripts/Parts/` in each.

**This repository is PUBLIC.** It must NEVER contain anti-hack / security code.
Anti-hack lives only in the game repo as private `*.Security.cs` partial classes that
are not part of this submodule. The pre-commit hook rejects security markers.

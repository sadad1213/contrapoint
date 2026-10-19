# Ladder behaviour references

Reviewed 2026-10-18. Implementation is in `player/SourceMovementLadder.cs`.

- [Valve Source SDK 2013 gamemovement.cpp](https://github.com/ValveSoftware/source-sdk-2013/blob/master/src/game/shared/gamemovement.cpp): `LadderMove`, `FullLadderMove`, `TryPlayerMove`, CSTRIKE ladder damping.
- [CS:S gamemovement.cpp](https://github.com/nillerusr/source-engine/blob/master/game/shared/gamemovement.cpp): cross-check of the common ladder formulas and the same-tick switch to `FullWalkMove` on jump.
- [cs_gamemovement.cpp](https://github.com/nillerusr/source-engine/blob/master/game/shared/cstrike/cs_gamemovement.cpp): `LadderDistance`, `ClimbSpeed`, `OnLadder`, `CheckForLadders`. `LadderLateralMultiplier` is declared here but is not used by the common `LadderMove`; do not invent an extra 0.5 strafe multiplier.
- [cs_shareddefs.cpp](https://github.com/nillerusr/source-engine/blob/master/game/shared/cstrike/cs_shareddefs.cpp): crouched climb modifier 0.34.
- [cs_player_shared.cpp](https://github.com/nillerusr/source-engine/blob/master/game/shared/cstrike/cs_player_shared.cpp): `SurpressLadderChecks`, `CanGrabLadder`, `UpdateStepSound`.
- [baseplayer_shared.cpp](https://github.com/nillerusr/source-engine/blob/master/game/shared/baseplayer_shared.cpp): ladder step gain 0.5 and cadence 350 + 100 ms.

The nillerusr repository is an unofficial source mirror, not an official Valve SDK release. See `player/README.md` for the existing movement provenance and limits.

Audio deliberately uses full 3D ladder speed (60 u/s threshold), rather than the CS-specific 150 u/s horizontal gate: the requested ordinary vertical climbing must be audible. Crouch climbing stays silent.

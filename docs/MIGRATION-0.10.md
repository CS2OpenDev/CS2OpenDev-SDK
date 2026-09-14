# Migrating to CS2OpenDev.Sdk 0.10

**Eight members and one `SchemaNames` class leave the public surface.** Two unrelated upstream
changes landed in the same build: a base class interposed under `CCSPlayerCamera`, and four
`ECstrike15UserMessages` members renamed. A ninth member stops being declared on
`CCSPlayerCamera` and is inherited instead, which compiles unchanged and needs nothing from you.

Measured against CS2 build 25218825 (from 25175329). Regenerate the inventory with:

```
dotnet run --configuration Release --project src/CS2OpenDev.Sdk.Exporter
```

## 1. `CCSPlayerCamera` gains an interposed base

`CCSPlayerCamera` derived from `C_BaseEntity` on the client and `CBaseEntity` on the server and
declared three fields of its own. In 25218825 it derives from a new `CCSCustomPlayerCamera` and
declares none: its `field_count` went from 3 to 0, and the new base carries eight fields. Both
platforms and both modules made the same change.

One of the three fields moved up into the new base. The other two are gone, replaced by a
different model of the same state.

| Native field | Was | Now | Consumer impact |
|---|---|---|---|
| `m_hPawn` | declared on `CCSPlayerCamera` | declared on `CCSCustomPlayerCamera` | none, inherited |
| `m_bEnabled` | declared on `CCSPlayerCamera` | gone | breaking |
| `m_bIsControllingAngles` | declared on `CCSPlayerCamera` | gone | breaking |

`CCSPlayerCamera` is the only subclass of `CCSCustomPlayerCamera` in either module, so the new
base reads as a generalisation ahead of siblings that do not exist yet.

### `Pawn`: nothing to do

`CCSPlayerCamera : CCSCustomPlayerCamera`, and `Pawn` is declared on the base with the same
`CHandle<C_CSPlayerPawnBase>` type it had before. `camera.Pawn` resolves exactly as it did.

The surface gate reports this as a hoist rather than a removal, which is why it does not count
toward the eight above. See `reachable_members` in `scripts/sdk_surface.py`.

### `Enabled`: use `CameraMode`

`CCSCustomPlayerCamera` adds `m_nCameraMode`, emitted as `CameraMode` typed by a new enum
`CustomCameraMode` (`CustomCameraMode_t` upstream):

| Member | Value |
|---|---|
| `CustomCameraModeDisabled` | 0 |
| `CustomCameraModeControlled` | 1 |
| `CustomCameraModeControlledPosition` | 2 |
| `CustomCameraModeFollowPosition` | 3 |

Disabled is 0, so the old boolean is the disabled check inverted:

```csharp
// was
if (camera.Enabled) { ... }

// now
if (camera.CameraMode != CustomCameraMode.CustomCameraModeDisabled) { ... }
```

### `IsControllingAngles`: no confirmed replacement

The enum splits controlled cameras into `Controlled` and `ControlledPosition`, and the obvious
reading is that the first controls angles and the second does not. That reading is taken off the
member names alone. Nothing in the schema states it and it has not been checked against the game,
so confirm it before depending on it. If you need the old semantics exactly, no field carries them
any more.

### `SchemaNames.CCSPlayerCamera`: renamed

With no fields left on the class the generator emits no constants holder for it, so the nested
class is gone. The constants live on `SchemaNames.CCSCustomPlayerCamera`, which carries `Pawn` and
the new fields but not `Enabled` or `IsControllingAngles`.

### What else arrived on the new base

Beyond the hoisted `Pawn` and the new `CameraMode`, `CCSCustomPlayerCamera` declares six
properties with no predecessor on `CCSPlayerCamera`: `FollowEntity` (`CHandle<C_BaseEntity>`),
`FollowEyes` (`bool`), `FollowOffset` (`Vector`), `CameraOffset` (`Vector`), `ClipCameraOffset`
(`bool`) and `CameraOffsetReturnStrength` (`float`).

## 2. Four `ECstrike15UserMessages` members renamed

Unrelated to the camera change and in the same build. Four members of
`CS2OpenSchema.Server.ECstrike15UserMessages` gained a `_CSGOLegacy` suffix upstream. **The
numeric values are unchanged**, so this is a compile break with an exact mechanical fix and no
behavioural change.

| Was | Now | Value |
|---|---|---|
| `CS_UM_SayText` | `CS_UM_SayText_CSGOLegacy` | 305 |
| `CS_UM_SayText2` | `CS_UM_SayText2_CSGOLegacy` | 306 |
| `CS_UM_TextMsg` | `CS_UM_TextMsg_CSGOLegacy` | 307 |
| `CS_UM_UpdateTeamMoney` | `CS_UM_UpdateTeamMoney_CSGOLegacy` | 328 |

The old spellings are gone, not deprecated. Anything switching on these needs the new names.

## Everything else in this build

Additions, which never block:

- `CCSCustomHudLayout.Observable` on client, server and `SchemaNames`.
- `SVCMessages.Svc_EncryptedData`.
- The four types behind section 1: `Client.CCSCustomPlayerCamera`,
  `Server.CCSCustomPlayerCamera`, `Server.CustomCameraMode` and
  `SchemaNames.CCSCustomPlayerCamera`.

That is the complete accounting: 1 type and 8 members removed, 4 types and 8 members added, 2
members hoisted, 2 type declarations changed. Nothing else in the emitted surface moved.

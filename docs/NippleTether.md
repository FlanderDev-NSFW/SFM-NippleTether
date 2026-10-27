# NippleTether

> Developer notes: how it works, settings in detail, diagnostics and the test history. The player readme (shipped in the
> release zip) is [`mods/NippleTether/README.md`](../../mods/NippleTether/README.md).
>
> **1.0.0 (2026-10-27): first release** (F3M). Plugin GUID `FlanderDev.SFM.NippleTether` (was `com.local.nippletether`), so the
> config file is `BepInEx/config/FlanderDev.SFM.NippleTether.cfg`. The release zips no longer include RestraintHub (it is its own download).
>
> It does not need NipplePiercings: that mod's chain code is compiled in and its model embedded. L3 2026-10-27: with
> NipplePiercings removed from the plugins folder it loaded, put on with 400 cm chains and drew its own rings (screenshot).

Two chains, one from each nipple, to one point in the air. A restraint on **RestraintHub** (1.0.0 or newer; ring value 9500): put on from the
ring's restraint page, it appears in the restraint list. The point has no gravity and goes through anything; the chains hang
under gravity, lie on her body and the level, and when they are held taut they draw her breasts out and hold her back.

## Controls (Nipple Tether selected in the restraint list)

| Key | Does |
|---|---|
| tap **F** | hand the point to the player / give her back control. While the player holds the point she stands still and **WASD** move it (camera-relative), **Space** / **C** raise and lower it, Shift is 2.5x faster |
| hold **F** | struggle free (`Unlock.AttemptSeconds`, `SuccessChance`) |
| **+** / **-** | let the chains out / take them in: a press is `Chain.StepCm`, held they run on smoothly at `Chain.LengthSpeed` (the links run out of and into the point's hoop). Stored in `Chain.LengthCm` on release |

F belongs to the game inside interaction areas (closet, PC ...): step out of them first. It holds her place (`BodyParts.Spot`),
so it is refused together with the Wall Restraint unless overlap is on (`V`).

## How it works

- **Ends:** at each nipple a ring through it (NipplePiercings' "ring", sized to the nipple and placed from the nipple's tip on
  her body bake, as that mod does), swung towards its chain; the chain hangs from its bottom. With NipplePiercings installed and
  a piercing worn on that side (read from its config by reflection, `PiercingsLink`) no ring is drawn and the chain starts at
  the pierced point. At the point a hoop (NipplePiercings' "hoop") hangs upright, facing her.
- **Chains** (`Logic/TetherChain.cs`, plain C#): NipplePiercings' chain link scaled to `Chain.LinkCm`, as joints held at both
  ends; Verlet under gravity and damping; the links' lengths are kept by "fast projection" (all at once, a tridiagonal solve per
  step) - one-by-one sweeps left a 67-link chain 4.6 % long while slack, which read as a pull, and let a taut one sag 19
  degrees. A chain held further apart than it is long is solved stretched evenly. Her body (torso, head, arms, legs as capsules
  from her bone lengths, a sphere behind each nipple) is kept out every few sweeps; the level once per frame
  (`EnvironmentProbe`: `Physics.OverlapSphereNonAlloc` + `ComputePenetration`, her own colliders, NPCs, UI and invisible player
  blockers skipped), with friction so a chain lying on something stays put. Length changes add or remove links at the point's end.
- **Pull:** `Stretch` = how much longer a chain is held than it is (path or straight distance; also when it is caught around
  something). The breast is drawn towards its chain until the chain is just taut, up to `Breasts.MaxStretchCm`: its root bone
  (`BreastRotateL / R`, in LateUpdate, put back in the next Update) is stretched along its axis to the nipple
  (`Breasts.Elongation`, at most `MaxElongation`) and turned the rest of the way (at most `MaxAngle`); it springs back when the
  chain is slack. The game's own breast physics (DynamicBone, stiffness 0.93) is too stiff for a force to show.
- **Holding her back:** she is never pulled. Of her own step, the part that would take a nipple further from the point than
  the chain plus the breast stretch reach (at their height difference) is undone (`PointControl.Hold`,
  `CharacterController.Move`): she can walk freely within reach and along its edge, and back in. The point held by the player
  stops at that reach (it does not drag her), and `-` takes the chains in only as far as she can follow without being pulled.
- **Hub:** the F tap is RestraintHub's `OnTap` (holding F longer is its `StruggleHeld`), smooth + / - its `HeldStep`, menus its
  `MenuOpen`; save, load and a new game come through its `OnSave` / `OnLoad` / `OnNewGame`.
- **Save:** whether it is on (`NippleTether.Locked`, `.Chance`; the hub saves the shortcuts). After a load, a teleport or a map
  change the point is put `Point.StartDistance` in front of her at the nipples' height.

## Config (`BepInEx/config/FlanderDev.SFM.NippleTether.cfg`)

| Key | Default | Meaning |
|---|---|---|
| General.RingLabel | Nipple Tether | name in the ring and the list |
| Chain.LengthCm | 100 | length of each chain (cm; + / - change and store it) |
| Chain.StepCm / LengthSpeed | 2 / 0.4 | cm per press of + / -; m/s while held (and to follow a new length) |
| Chain.MinLengthCm / MaxLengthCm | 15 / 300 | what + / - reach |
| Chain.LinkCm | 1.5 | link size (cm, joint to joint; applies when put on) |
| Chain.Damping / Gravity / Friction | 1 / 1 / 0.6 | swing damping, gravity, sliding speed lost per frame on the level |
| Chain.BodyCollision / LevelCollision | true / true | chains stay out of her body / the level |
| Point.Speed | 0.8 | m/s while the player holds it (Shift 2.5x) |
| Point.UpKey / DownKey | Space / C | raise / lower the point (Input System key names; their game actions are suppressed meanwhile) |
| Point.StartDistance | 0.6 | m in front of her where the point is put |
| Breasts.MaxStretchCm / MaxAngle | 4 / 30 | how far a nipple is drawn out (cm at height 1, scaled with her height) / a breast turned (deg) before she is held |
| Breasts.Elongation / MaxElongation | 1.5 / 0.5 | how much of the pull stretches the breast towards the chain (per pull relative to its length; 0 = only turned) / at most (0.5 = 1.5 x its length) |
| Nipple.NippleFit / DepthCm | 1.7 / 0.45 | ring size relative to the nipple; how far behind the tip it goes through |
| Unlock.AttemptSeconds / SuccessChance | 3 / 1 | the struggle |
| Debug.Disable | 0 | bit mask (live): 1 breasts not pulled, 2 she is not held back, 4 no body collisions, 8 no level collisions, 16 no nipple rings |

## Diagnostics

`game get NippleTether.TetherController::Status` (per chain: links, stretch, breast pull and turn, joints on the level; the
point's distance and height, length and target, how far she was moved, level probes). With `--allow-invoke`:
`game call NippleTether.TetherController PutOnNow | TogglePoint`, `... MovePointBy 0,-1,0`.

## Verification

- L1: `dotnet run --project tests/NippleTether.Tests` (43): slack chain reads untaut (stretch < 0.1 mm), sags, keeps its links;
  taut chain: stretch 0.2 m for ends 1.2 m apart on 1 m, pulls straight; a moving point and letting out / taking in move no
  joint more than 5 cm a frame, rests add up, 0.25 - 1.25 links last; on a floor: lies on it, stays put, control without the
  level hangs through; caught on a post: stretch; body sphere: outside; breast pull: settles, springs back, limited, rate
  limited; F tap vs hold; point moves camera-relative, clamped; `Hold` (only her outward step undone, never pulled in); the models.
- L3 0.1.0 (2026-10-25, Apartment, first save; your Breasts 15 cm / 80 deg config):
  - put on: 74 links of 1.5 cm per 1.1 m chain, from NipplePiercings' crystals (no own ring), draped to the hoop 60 cm ahead.
  - point lowered 1.25 m (onto the floor): 19 + 27 joints on the floor; breasts drawn down (19.7 cm, 37-44 deg) - front inset
    screenshot vs `Debug.Disable 1` (control): breasts at rest. She was moved 15 cm until within reach, then not at all (before
    `Follow`, pulling along the chain's direction slid her 2.4 m).
  - walking away: held at the chain's reach (point 120.6 cm away, reach 129.7 cm).
  - F tap -> point mode, tap -> back, hold 3.6 s -> "attempt: success", off. W 1 s moved the point 51 cm, Space raised it
    48 cm, she stayed put; with her coat open Space / C changed neither coat nor crouch (Space closed the coat before the fix).
  - + held: 110 -> 126 cm (74 -> 84 links), - back; 2.88 m chains (2 x 192 links): 21.4 vs 19.9 ms per frame without.
  - not checked: other maps, clothes, crawling, other restraints with overlap on.

- L3 0.2.0 (2026-10-25, on RestraintHub 0.3.0): F tapped twice -> "tap: NippleTether", point mode on / off; held 3.6 s ->
  struggled free. + held: 119.6 -> 317 cm in a smooth run at 0.4 m/s, one config write on release. Saved at home with it on:
  "save: locked=True"; restart + load: "load: locked=True", worn again.

- L3 0.3.0 (2026-10-26, user request: move her less, more breast pull; your config: 92 cm, 15 cm / 80 deg): walking away
  from the point she went 17 cm, then stopped at the reach (point 108 cm away) while still pressing S, and stayed put after
  releasing it (no sliding). Point moved far below her (debug call past the reach): she was not moved at all (0.1.0: pulled
  15 cm). Point held by the player, D for 5 s: it stopped at 111.8 cm (reach 111.7), she did not move. Breasts: 19.7 cm drawn
  out, 67 deg and stretched x1.50 - screenshot vs `Elongation` 0 (x1.00, control): visibly longer, nipples lower.

- 0.3.1 (2026-10-26): the nipple tips are searched at every bake with NipplePiercings 0.2.2's tighter search (with the
  unstretched nipple size while a breast is stretched).

- L3 0.3.2 (2026-10-26, user report: the chains go through the floor): the joints were only pushed out of the level once a
  frame; a joint lying on the floor's mesh slipped under it and stayed there (diagnostic `Floor`: lowest joint 1.9 cm below the
  floor, resting on a box under it; screenshot: chains into the floor at her feet). Now each joint is also swept
  (`Physics.SphereCastNonAlloc`) from where it was at the last check, started a link's thickness further back, since a sweep
  ignores the floor it starts touching (tests: a falling 4 m chain and a new layout that would sag through a thin floor; the
  layout test fails without the back-off). In game with your 400 cm chains: lowest joints 0.2 - 0.3 cm above the floor, also
  after walking with the point lowered to 16 cm; screenshot: the chains lie in a curve on the floor.

## Known limits

- When held back she walks on the spot (the game's walk animation plays against the chain).
- The chains are drawn over clothes and pass through them.
- Pull and collisions are approximations: the body is capsules and spheres; a chain caught around the level pulls the breasts,
  but she is held back by the straight distance to the point only.
- The point is not saved: after a load it is put in front of her.

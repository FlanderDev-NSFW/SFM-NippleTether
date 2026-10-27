# NippleTether 1.0.0

Two chains for **Secret Flasher Manaka**, one from each nipple to a point in the air. The chains hang, lie on her body and
the floor, and when they are pulled tight they stretch her breasts and keep her from walking further away.

## Requirements
- Secret Flasher Manaka (tested with v1.1.3)
- BepInEx 6 for IL2CPP (tested with build 738)
- **RestraintHub 1.0.0** or newer
- Optional: NipplePiercings. If she wears a piercing there, the chain hangs from it.

## Install
Extract the zip into the game folder (the folder with `SecretFlasherManaka.exe`). You get `BepInEx/plugins/NippleTether/`.

## How to use
Hold a ring key (e.g. `1`), page with `Z` / `X` to the restraint page and click **Nipple Tether**. The point appears in front
of her. With Nipple Tether selected in the worn list (`B` selects):

| Key | Does |
|---|---|
| tap `F` | switch between moving her and moving the point |
| `W` `A` `S` `D` | (moving the point) move it; `Space` / `C` raise and lower it; `Shift` is faster |
| `+` / `-` | let the chains out / take them in (hold for a smooth change) |
| hold `F` | struggle free |

The point floats and passes through walls. She can walk freely while the chains are slack; when they are tight she cannot
walk further away from the point.

## Settings
`BepInEx/config/FlanderDev.SFM.NippleTether.cfg` (created on the first start). Most useful:

| Setting | Default | |
|---|---|---|
| Chain.LengthCm | 100 | chain length (`+` / `-` change it) |
| Chain.MinLengthCm / MaxLengthCm | 15 / 300 | range of `+` / `-` |
| Breasts.MaxStretchCm | 4 | how far the breasts can be pulled before she is held back |
| Breasts.Elongation | 1.5 | how much a pull stretches the breasts (0 = they only turn towards the chain) |
| Unlock.SuccessChance | 1 | chance that struggling frees her (1 = first try) |

## Good to know
- The point's position is not saved: after loading it is placed in front of her again.
- The chains pass through clothes.
- When held back she walks on the spot.

Single-player / offline only. Code: MIT licence (`LICENSE.txt`). Models: "Assorted Piercings" by hellsquad2, CC BY-NC-ND 4.0 (smutba.se; details in `LICENSE-MODELS.txt`).

## Building from source
- .NET SDK 8, and a copy of the game with BepInEx 6 IL2CPP started once: copy `Directory.Build.local.props.example` to
  `Directory.Build.local.props` and set `GameDir`. Then `dotnet build src/NippleTether/NippleTether.csproj -c Release`.
- `git clone --recursive` (NipplePiercings' code and model come in as the `lib/NipplePiercings` submodule).
- It compiles against RestraintHub: the [SFM-RestraintHub](https://github.com/FlanderDev-NSFW/SFM-RestraintHub) repository next to this one, `lib/RestraintHub/RestraintHub.dll`, or the hub installed in the game.
- The source of the mod is `src/NippleTether` and its tests (`tests/`); `lib/SfmKit` is a copy of the shared [SfmKit](https://github.com/FlanderDev-NSFW/SfmKit)
  library; `docs/` holds the developer notes.
- Every push to `main` builds, tests and releases it (`.github/workflows/release.yml`, `build/ci.py`).

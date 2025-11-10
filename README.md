> [!IMPORTANT]  
> This plugin requires my fork of [PKHeX](https://github.com/hexbyt3/PKHeX/releases/tag/10.26.25) to work right.  

![GitHub Release](https://img.shields.io/github/v/release/hexbyt3/PLZA-Seed-Finder)
![GitHub Downloads (all assets, all releases)](https://img.shields.io/github/downloads/hexbyt3/PLZA-Seed-Finder/total?color=violet)


# PLZA Seed Finder Plugin for PKHeX

A specialized seed finding plugin for PKHeX that generates **valid, legal Pokémon** for Pokémon Legends: Z-A by finding proper origin seeds that satisfy PLZA's strict correlation requirements.

## The Problem This Solves

### Why You Need This Plugin

Pokémon Legends: Z-A uses a sophisticated seed correlation system where **every Pokémon must have a valid origin seed** that correlates correctly with its PID, EC, IVs, and other properties. Unlike previous generation games, you cannot simply generate a Pokémon with random values - the game validates that all properties were generated from the same seed using the **Xoroshiro128+ RNG algorithm**.

**Without proper seed correlation:**
- PKHeX will flag Pokémon as invalid
- Generated Pokémon fail legality checks
- You cannot create legal Shiny Alpha Pokémon
- Mystery Gift and Static encounters fail validation
- Pokémon will be rejected in online trades

### How This Plugin Helps

This plugin **searches through millions of valid seeds** to find ones that produce Pokémon matching your exact specifications. Instead of randomly generating values and hoping they correlate, it:

1. **Finds valid origin seeds** that produce your desired Pokémon
2. **Ensures proper correlation** between PID, EC, IVs, nature, and all other properties
3. **Generates legally valid Pokémon** that pass PKHeX validation
4. **Supports all encounter types** (Wild, Static, Gift, Trade)
5. **Handles special cases** like Shiny Alpha Pokémon that have complex PID+ correlation

**Author:** [@hexbyt3](https://github.com/hexbyt3)

## Screenshots
<img width="1528" height="830" alt="image" src="https://github.com/user-attachments/assets/8aedce04-8624-4edf-9ba7-f145ba4cc012" />

## Features

### Core Functionality
- **Valid Seed Generation**: Searches through millions of 64-bit seeds to find ones that produce legal Pokémon
- **Proper Correlation**: Ensures PID, EC, IVs, nature, ability, and gender all correlate from the same origin seed
- **All Encounter Types Supported**:
  - Wild encounters (standard field encounters)
  - Static encounters (legendaries, fixed spawns)
  - Gift Pokémon (starters, fossils, event gifts)
  - In-game trades (with proper fixed trainer data)

### Search Capabilities
- **Customizable IV Ranges**: Set min/max for each stat (HP, Atk, Def, SpA, SpD, Spe)
- **Nature Selection**: Find specific natures or leave random
- **Ability Control**: Target specific abilities including Hidden Abilities
- **Gender Filtering**: Male, Female, Genderless, or Random
- **Shiny Options**: Never, Always, Square-only, or Star-only shinies
- **Flawless IV Count**: See how many perfect IVs each encounter guarantees
- **Trainer ID Support**: Uses your save file's TID/SID for correct shiny determination

### Advanced Features
- **64-bit Seed Range Support**: Search specific hex ranges or entire seed space
- **Parallel Processing**: Multi-threaded search processes 50,000+ seeds per second
- **Real-time Results**: See matches as they're found, no waiting until search completes
- **Shiny Alpha Support**: Generates valid Shiny Alpha Pokémon (notoriously difficult due to PID+ correlation)
- **CSV Export**: Save results with seed, IVs, nature, PID, EC for later use
- **Direct PKHeX Loading**: Double-click any result to load into PKHeX editor
- **Visual Highlighting**: Shiny results color-coded (blue for square, gold for star)

### User Interface
- **Species Type-ahead Search**: Quickly filter through all available Pokémon
- **Live Encounter Filtering**: Shows which sources have your selected species
- **Progress Tracking**: Real-time seed count and percentage progress
- **Dark Theme Compatible**: Matches PKHeX's modern UI design

## Requirements

- PKHeX (latest version recommended)
- Windows 10 or 11
- .NET 9.0 runtime (usually already installed if you can run PKHeX)

## Installation

1. Download the latest `PLZASeedFinderPlugin.dll` from the releases page
2. Place it in PKHeX's `plugins` folder (create the folder if it doesn't exist)
3. Restart PKHeX
4. You'll find "PLZA Seed Finder" in the Tools menu

## Usage

### Basic Search
1. Open the plugin from Tools > PLZA Seed Finder
2. Search or select your target Pokémon species
3. Choose the form if applicable
4. Select which encounter sources to search (Wild, Static, Gift, Trade)
5. Set your search criteria
6. Click Search

### Understanding PLZA Seed Requirements

Each encounter type has different seed correlation patterns:

**Wild Encounters (EncounterSlot9a)**
- Variable levels within encounter range
- Can be Alpha or normal
- Seed determines all properties including Alpha status
- Shiny Alpha requires special PID+ correlation

**Static Encounters (EncounterStatic9a)**
- Fixed level and location
- Often legendary Pokémon
- May have minimum flawless IVs
- Some have fixed natures or abilities

**Gift Pokémon (EncounterGift9a)**
- Fixed trainer data (OT, TID, SID)
- Examples: starters, fossils, special event gifts
- Plugin handles proper trainer info automatically
- Seed still determines IVs, nature, shininess

**In-Game Trades (EncounterTrade9a)**
- Completely fixed properties
- Specific OT, nature, ability
- Seed variation is minimal
- Mainly for completion purposes

### Search Tips

**For Best Results:**
1. **Start with wide IV ranges** - Finding 6IV Pokémon requires searching billions of seeds
2. **Use species filter** - Type to quickly find your target Pokémon
3. **Check encounter sources** - Plugin shows which types are available
4. **Specify shiny early** - Shiny determination happens first in RNG chain
5. **Set realistic max results** - 100-500 is usually sufficient
6. **Use hex seed ranges** - Format: `0000000000000000` to `00000000FFFFFFFF`

**For Shiny Alphas:**
- These are the hardest to find due to PID+ correlation
- Expect longer search times (10-30 minutes for good IVs)
- Square vs Star is determined by XOR value
- Your TID/SID from save file affects shiny determination

**Performance Expectations:**
- Modern 8-core CPU: ~80,000-120,000 seeds/second
- Finding specific shiny: typically 1-5 million seeds
- Finding 5-6IV shiny: 10-100 million seeds
- Finding shiny alpha with good IVs: 50-500 million seeds

## Building from Source

If you want to build the plugin yourself:

1. Clone this repository
2. Open `PLZASeedFinderPlugin.sln` in Visual Studio 2022 or later
3. Ensure you have the PKHeX.Core NuGet package referenced
4. Build in Release mode
5. The compiled DLL will be in `bin/Release/net9.0-windows/`

## Technical Details

### PLZA Seed Correlation System

Pokémon Legends: Z-A uses a **Xoroshiro128+ RNG algorithm** (via LumioseRNG in PKHeX.Core) where every Pokémon property is generated sequentially from a single 64-bit origin seed. The correlation must be perfect:

```
Seed → EC → PID → IVs → Ability → Gender → Nature → Height/Weight → Moves
```

Each value advances the RNG state in a specific pattern. If any value doesn't match what the seed should produce, the Pokémon is **invalid**. This is why random generation fails - you can't just pick values you want; they must all come from the same seed.

### The PID+ Correlation Challenge

The most difficult case is **Shiny Alpha Pokémon**, which require PID+ correlation. The algorithm must:
1. Generate the initial PID from the seed
2. Check if it's shiny based on your TID/SID
3. If not shiny but needed, try again with RollCount=1
4. Validate the final PID matches the expected pattern

This involves up to **131,072 seed operations** per attempt. Without proper seed searching, generating valid Shiny Alphas is nearly impossible.

### How This Plugin Works

**Search Algorithm:**
1. Takes user-defined criteria (IVs, nature, shiny, etc.)
2. Iterates through seed ranges using parallel processing
3. For each seed:
   - Quick validates if seed could produce desired criteria
   - Generates full Pokémon using PKHeX.Core's LumioseRNG
   - Verifies all properties match requirements
   - Validates correlation is correct
4. Returns only seeds that produce valid, legal Pokémon

**Encounter Type Support:**
- **EncounterSlot9a**: Wild encounters with variable levels and Alpha status
- **EncounterStatic9a**: Fixed encounters like legendaries with set properties
- **EncounterGift9a**: Gift Pokémon with specific trainer data (handles fixed OT/TID)
- **EncounterTrade9a**: In-game trades with locked properties

**Performance Optimizations:**
- Multi-threaded parallel search across CPU cores
- Quick pre-validation before full generation
- Batch processing in 10,000 seed chunks
- Real-time result streaming
- Smart SearchShiny1 toggle when loading results (instant loading vs 5-30 second delay)

## Frequently Asked Questions

### Why can't I just use regular PKHeX generation?

Regular PKHeX generation doesn't search for seeds - it either:
1. Uses a random seed (which likely won't produce your desired values)
2. Tries to find a seed but may fail for complex requirements
3. Doesn't guarantee proper correlation for all PLZA encounter types

This plugin **brute-force searches millions of seeds** to find ones that actually produce what you want.

### What happens if I use a Pokémon without proper seed correlation?

- **PKHeX marks it invalid** with correlation errors
- **May be rejected in online trades** or competitions
- **Other players may report it** as hacked/illegal
- **You waste time** creating Pokémon that can't be used

### Why do shiny Pokémon show "PID Type: None" and "Origin Seed: 0" in PKHeX, but non-shinies show the correct values?

**This is completely normal and expected PKHeX behavior!** Here's why:

**For Non-Shiny Pokémon:**
- PKHeX can **quickly reverse-calculate** the origin seed from the EC and PID
- This is a fast mathematical operation (< 1ms)
- Shows: `PID Type: Xoroshiro`, `Origin Seed: [calculated value]`

**For Shiny Pokémon:**
- The game rolls the PID **multiple times** (up to 4 with Shiny Charm)
- When a shiny is found, the game **modifies the PID bits** to force shiny status
- PKHeX doesn't know which roll succeeded or what the original bits were before modification
- Recovering the seed requires **brute-forcing ~131,072 combinations**
- This is **disabled by default** to prevent freezing when validating boxes of Pokémon
- Shows: `PID Type: None`, `Origin Seed: 0`

**Your Pokémon is still perfectly legal!** PKHeX only marks Pokémon as invalid if seed validation explicitly fails. When it can't determine the seed, it returns "Ignore" status, meaning it won't mark the Pokémon as illegal.

The plugin shows you the seed in the alert message when you load a result - you can copy it from there if needed.

### Why do different species (like Bulbasaur and Weedle) yield the same seed results?

**This is actually correct behavior!** The Xoroshiro RNG is **species-agnostic** - a given seed produces the same sequence of random numbers regardless of which Pokémon you're generating.

**What stays the same across species:**
- ✅ EC (Encryption Constant)
- ✅ PID (determines if shiny)
- ✅ Base random number sequence
- ✅ Ability slot number
- ✅ Gender roll (but different species have different gender ratios, so the actual gender may differ)

**What changes between species for the same seed:**
- ❌ **Actual ability** (Weedle's ability #1 vs Bulbasaur's ability #1)
- ❌ **Final IVs** (Alphas get 3 perfect IVs, non-Alphas get 0)
- ❌ **Nature** (Alphas use different RNG correlation, consuming the RNG in a different order)
- ❌ **Height/Weight** (species-specific scaling)

**Why this is useful:**
If you find a good shiny seed, you can use it for **any species you want!** Just change the species filter and search that seed range - you'll get shinies for whatever Pokémon you pick. One good seed = infinite shiny Pokémon of any species!

### Why are Shiny Alphas so hard to generate?

Shiny Alphas require **PID+ correlation** where:
1. The seed must produce a shiny PID using RollCount=1
2. The PID must pass additional correlation checks
3. This requires trying up to 131,072 operations per seed
4. Only ~1 in 4,096 shinies are valid Shiny Alphas

Without seed searching, finding valid Shiny Alphas is nearly impossible.

### How do I know if my generated Pokémon is valid?

After loading a result into PKHeX:
1. Check the legality indicator (should be ✓ Valid)
2. Look for any red/orange flags in the legality analysis
3. Specifically check for "PID mismatch" or "correlation" errors
4. If you see "Invalid: Encounter Type PID mismatch" → bad seed correlation

All Pokémon generated by this plugin should pass validation.

### Why does my search take so long?

Search time depends on criteria difficulty:
- **Easy (5 seconds)**: Any shiny with random IVs
- **Medium (1-5 minutes)**: Specific nature shiny with 4-5IVs
- **Hard (10-30 minutes)**: Shiny Alpha with 5-6IVs
- **Very Hard (hours)**: Perfect 6IV Shiny Alpha with specific nature

Use wider IV ranges to find results faster.

### Can this plugin modify save files or inject Pokémon?

**No**. This plugin only:
- Searches for seeds
- Generates Pokémon in PKHeX's editor (not your save)
- Exports data to CSV

You must manually save the Pokémon to your save file using PKHeX's normal functions.

## Credits

This plugin wouldn't exist without the incredible work of the PKHeX team:
- **Kurt (@kwsch)** - For creating and maintaining PKHeX, implementing the LumioseRNG system, and providing the comprehensive PLZA encounter database
- **SciresM** - For reverse-engineering the Xoroshiro128+ RNG algorithm and PID+ correlation mechanics
- **All PKHeX Contributors** - For the robust legality analysis and validation framework

Special thanks to:
- The **RNG research community** for documenting PLZA's seed correlation requirements
- **PLZA dataminers** for uncovering encounter tables and correlation patterns
- **Community contributors** for testing and feedback

## License

This project is licensed under the MIT License. See the LICENSE file for details.

## Contributing

Issues and pull requests are welcome. If you find a bug or have a feature request, please check existing issues first.

## Disclaimer

This tool is designed for educational purposes and to help users understand PLZA's seed correlation system. All Pokémon generated using this plugin:

- Have valid origin seeds and proper correlation
- Pass PKHeX's legality analysis
- Are generated using the same RNG algorithm as the game
- Are indistinguishable from naturally encountered Pokémon

**Important Notes:**
- This plugin does not modify game files or memory
- All generation happens within PKHeX
- Users are responsible for how they use generated Pokémon
- Always follow game terms of service and community guidelines

Please use responsibly and respect the game and other players.

---

For more information about PKHeX, visit the [official repository](https://github.com/kwsch/PKHeX).

WarTech IIC (WIIC) is a mod for HBS's Battletech computer game. It's inspired by the likes of WarTech, GalaxyAtWar, and RogueTech's PersistantMap. The mod's homepage is https://github.com/BlueWinds/WarTechIIC, where you can always find the most up to date source code.

WIIC is distributed under the GNU General Public License v3.0 license. Special permission is granted to Battletech Advanced: 3062 and Roguetech to distribute this alongside Custom Bundle and other proprietary code. Please reach out to BlueWinds on github, or the BTA/RogueTech discords for more details.

# Flareups
WIIC adds Flareups to the map - conflicts between factions for control of entire planets. They play out similarly to Flashpoints, where the player travels to the system, accepts a contract to participate in the campaign, and is then offered a series of missions to help their side take (or retain) control of the planet. Here's the basic flow, and how it relates to the settings in `settings.json`.

## On game load
When a game is loaded or started, WIIC looks at `setActiveFactionsForAllSystems`. If `true`, it will override the active factions for every star system on the map. See [Active Factions](#setting-active-factions) for more details on how it determines which factions to set as active for each system. If `false`, nothing happens.

## Generating Flareups
Flareups come in two types - Attacks and Raids. In an attack, one faction attempts to wrest control of a system from the other. The winner will end up controlling the star system. In a raid, the attacker simply wants to do damage, steal resources, and otherwise harass the system owner. The victor will gain bonuses in future flareups, the loser penalties.

Every day that passes, there is a `dailyAttackChance` chance that a new Flareup will be added to the map, or `dailyRaidChance` for a Raid. There are no limits to the number of flareups that can be active at once, and they can occur from day 1 onwards. When a flareup or raid occurs...

### Deciding attacker and location
WIIC first decides who will be the attacker and where they'll attack by iterating through all star systems on the map.

1) If it's controlled by a faction in `cantBeAttacked` or `ignoreFactions`, it's also skipped.
2) Each faction that controls a neighboring system and isn't in `ignoreFactions` might attack, if they're either the owner's enemy or `limitTargetsToFactionEnemies` is `false`. The weight for that attacker on this star system is the following items multiplied together:
    1) The number of bordering systems the attacker controls (within one jump)
    2) The distance multipilier: `1 / sqrt(distanceFactor + distanceInLyFromPlayer)`. Systems near the player are more likely to be attacked than those far across the map.
    3) `aggression[attacker]`, read from the settings, defaulting to 1.
    4) `reputationMultiplier[attacker] + reputationMultiplier[defender]`.
    5) `hates[attacker][defender]`, defaulting to 1.
    6) Multipliers from any tags in `systemAggressionByTag` that the system has. A multiplier of 0 means this system can never be attacked or raided randomly.
3) For attacks, `factionInvasionTags` are always considered adjacent to the appropriate faction (that is to say, Jade Falcon can always attack planets with the falcon_invasion_corridor tag, if such is set in settings.json). For raids, `factionActivityTags` is used instead (eg, Pirates can always raid planet_other_pirate worlds in the default settings).
    * The weight uses the same rules as above. Each tag the planet has is equivalent to one 'border world'.

Factions will only attack or raid themselves if they are set to be their own enemy (regardless of `limitTargetsToFactionEnemies`).

With the weight for each target system and each faction which could attack it figured out, one is selected at random.

### Initial setup
A border world controlled by the defender and near the attacker is chosen at random. The flareup is now visible as a blip to the starmap, appearance controlled by the `attackMarker` or `raidMarker` as appropriate. See [ColourfulFlashpoints](https://github.com/wmtorode/ColourfulFlashPoints) for details on the settings.

The initial attacker and defender forces are calculated as follows.
1) The attacker begins with `defaultAttackStrength` points, overridden by their setting in `attackStrength` if they have one.
2) The defender begins with `defaultDefenseStrength` points, overridden by their setting in `defenseStrength` if they have one.
3) The `WIIC_{attacker}_attack_strength` and ``WIIC_{defender}_defense_strength` company stats are added to attack and defense strength respectively.
4) If the location has any `addStrengthTags`, this amount is added to both the attacker's and defender's strengths.
5) If it's a raid, both strengths are multiplied by `raidStrengthMultiplier`

## How Flareups proceed
When initially generated, flareups are in "countdown", a random number of days chosen based on `minCountdown` and `maxCountdown`. Nothing will happen until that many days pass - the attacker and defender are mustering their forces, preparing for the coming confrontation.

When the countdown reaches 0, the flareup begins ticking. Every `daysBetweenMissions` days, one side or the other - chosen with a coinflip - will lose between `combatForceLossMin` and `combatForceLossMax` points. When one side reaches 0, the *next time a mission would occur* the flareup ends.

### How Flareups end
When a raid concludes, the attacker gets + 1 to their `WIIC_{faction}_attack_strength` if they won, and -1 if they lost for the next `raidResultDuration` days. Similarly the defender with `WIIC_{faction}_defense_strength`.

When at attack concludes, the winner takes (or retains) control of the system, and the active factions are updated.

### Setting active factions
When a system flips control - or every system on game load, if `setActiveFactionsForAllSystems` is true - WIIC adjusts the planet's tags using the following steps:

The system's employers and targets are set with the following logic:
1) If the system has any tags included in `clearEmployersAndTargetsForSystemTags`, then employers and targets are emptied, and all other steps ignored.
2) The system owner and Locals are always included.
3) If the system has any tag from `factionActivityTags`, then the matching factions are added as active.
4) Any faction that shares a border with this world is added as active, unless they're in `ignoreFactions`.

## How the player gets involved
So that's cool and all, but what do players do?

Well, when they enter the star system where a flareup is occurring, each faction may generate a contract to hire the player ("Flareup: Attack Planet", "Flareup: Raid Planet", or one of the two variants of "Flareup: Defend Planet" for attacks/raids) if:
1) They are not listed in `wontHirePlayer`.
2) The players reputation with that faction is at least `minReputationToHelpAttack` / `minReputationToHelpRaid`.

If the player accepts the contract, the "countdown" is immediately set to 0, and the next mission is set to begin tomorrow. They get a task in the timeline telling them when the next mission will occur.

On the same interval as automatic force loss, every `daysBetweenMissions` days, the player will be offered a mission with nothing more than the mission name. If they accept, they *must* drop - no accepting the mission and then backing out! If they complete the mission, then the faction they fought against loses between `combatForceLossMin` and `combatForceLossMax` points, and the player gets a `attackBonusPerHalfSkull * (difficulty of mission)` cbill bonus (or `raidBonusPerHalfSkull`). Their non-priority salvage is increased by `attackBonusSalvage` / `raidBonusSalvage`.

If they don't accept the mission, fail it, or evac without completing it, their employer loses forces between `combatForceLossMin` and `combatForceLossMax` (equal to what the target would have taken if the player had succeeded).

While participating in a flareup, the player has to stay in the star system - if they attempt to leave, they will get a popup warning them of the consequences of breaking the contract. These aren't actually terribly severe, just reputation loss with the employer equal to one bad faith withdrawal from a mission.

### When the flareup ends
When a Flareup ends, if the player signed on with the winning faction and they dropped into combat at least once, they'll receive an extra reward. The itemCollection given to the player is determined by `defaultAttackReward` / `defaultRaidReward`, or overridden by an entry in `factionAttackRewards` / `factionRaidRewards`.

# Exporting / Importing map control
Every time a career saves, WIIC writes out `{savePath}/WIIC_systemControl.json`, which contains a list of all systems that have flipped control during the current career. If you copy this into the mod directory (`WarTechIIC/WIIC_systemControl.json`), then when you start a fresh career, WIIC will import the list. Bam, you can persist your map across careers!

# Simgame statistics and tags
WIIC reads and sets a variety of tags and statistics on companies and star systems. These can be used in conditions, set, updated or removed from events and flashpoints like any other stat or tag.

### Company Tags
When naming star systems, remember to use the ID and not the name. You want `starsystemdef_St.Ives`, not `St. Ives`. For factions, refer to them by factionID. You want `ClanCloudCobra`, not `Clan Cloud Cobra` or `faction_ClanCloudCobra`. This is slightly inconsistent, yes, but I work with what HBS gives me.

* `WIIC_helping_attacker` and `WIIC_helping_defender` - If present, the company is in the middle of a flareup, helping the attacker/defender take the current system. You can remove this from events, and nothing will break. Adding it from events will force player participation if there's already a flareup in their current system, otherwise it won't do anything.
* `WIIC_give_{system}_to_{newOwner}` (eg: WIIC_give_systemdef_Sol_to_Clan Wolf) - Setting this will pass control of the named star system to the new owner. The tag won't actually added to the company - WIIC 'eats' it.
* `WIIC_{faction}_attacks_{system}` (eg: WIIC_Clan Jade Falcon_attacks_systemdef_Sol) - Setting this will cause a new Attack to start in the given system, with the faction as the attacker, if one doesn't already exist. The tag won't actually added to the company - WIIC 'eats' it.
* `WIIC_{faction}_raids_{system}` (eg: WIIC_Clan Jade Falcon_raids_systemdef_Sol) - Setting this will cause a new Raid to start in the given system, with the faction as the raider, if one doesn't already exist. The tag won't actually added to the company - WIIC 'eats' it.
* `WIIC_set_{system}_{attacker|defender}_strength_{number}` (eg: WIIC_set_systemdef_Sol_defender_strength_10) - Setting this will adjust the attacker or defender's strength in that system's flareup, if there is one. The tag won't actually added to the company - WIIC 'eats' it.
* `WIIC_add_{tag}_to_{system} | WIIC_remove_{tag}_from_{system}` (eg: WIIC_add_planet_other_pirate_to_systemdef_Sol or WIIC_remove_planet_other_pirate_from_systemdef_Sol) - Setting this will add or remove the given tag from the given system. The tag won't actually added to the company - WIIC 'eats' it.

### Company Stats
For all company stats, `-1` is a magic value - "ignore this". If present, we'll read the value from settings.json rather than the stat.

* `WIIC_dailyAttackChance` (float) If present, this overrides the `dailyAttackChance` from settings.json. -1 will use the value from settings.json.
* `WIIC_dailyRaidChance` (float) If present, this overrides the `dailyRaidChance` from settings.json. -1 will use the value from settings.json.
* `WIIC_{attacker}_aggression` (float) If present, this overrides `aggression[attacker]` from settings.json. -1 will use the value from settings.json.
* `WIIC_{attacker}_hates_{defender}` (float) If present, this overrides `hatred[attacker][defender]` from settings.json. -1 will use the value from settings.json.
* `WIIC_{faction}_attack_strength` (int) Adds to the attack strength of any flareups or raids the faction engages in. Can be negative. Note that this is *additive* - it does not override that faction's default values. If you modify this from events, please add or subtract rather than set a value - Raids also adjust this, so setting a value would overwrite the raid history.
* `WIIC_{faction}_defense_strength` (int) Adds to the attack strength of any flareups or raids the faction engages in. Can be negative. Note that this is *additive* - it does not override that faction's values. If you modify this from events, please add or subtract rather than set a value - Raids also adjust this, so setting a value would overwrite the raid history.

# Misc functionality
WarTechIIC modifies several base-game features.
* Contracts in the current system refresh every time the month rolls over.
* Contracts in the command center are sorted by difficulty (with travel contracts at the bottom and priority contracts at the top).
* Contract descriptions can now use `{RES_OBJ}` for self-referencing descriptions ("mad libs"). For example, a contract can use its own name, `{RES_OBJ.Name}`, inside its `shortDescription`.

# Extended Contracts
In addition to existing Raids and Attacks, there are now "extended contracts", as defined by `ContractOverride` definitions. These are loaded via modtek, like other things that modtek can load. Though similar in some ways to flareups, extended contracts do not use / track combat forces, nor do they appear on the map of have lasting effects on the galactic stage.

## Extended Contract Types
Extended Contract Types are defined each in their own json files, loaded as any other ModTek resource. Any mod can include them by having an entry in its mod.json similar to

```
"Manifest": [
  { "Type": "ExtendedContractType", "Path": "extendedContracts" }
]
```

All the top-level properties explained below are required.

- The `name` is displayed to the player in various ways as the contract progresses.
- This type of extended contract can only spawn if `companyRequirements` is met. `StarSystem` scope requirements are checked against the location of the extended contract, not against the player's current location.
- When WIIC decides to spawn a extended contract, it does so based on their `weight`s. Higher weights are more likely to be selected.
- `employer` is a list of strings, with exact meaning determined by `spawnLocation`. See below.
- Each ExtendedContractType has a `spawnLocation`, which describes the *relationship between the owner of the star system and the* `employer`. It is one of:
  - `Any`: The employer doesn't need any specific relation to the system controller. Valid entries in `employer` are:
    - `Any` is not a valid `employer` when `spawnLocation` is also `Any`.
    - `OwnSystem`: The extended contract can spawn on any world, and the employer will be the system owner.
    - `Allied`: The extended contract can spawn on any world. The employer will be any faction faction the player is allied to.
    - Any faction ID (eg. `ClanJadeFalcon`): The extended contract can spawn on any world, and this faction will be the employer.
  - `OwnSystem`: The system owner and the employer are the same faction. Valid entries in `employer` are:
    - `Any`: Any system owner not in `ignoreFactions` / `wontHirePlayer` can hire the player.
    - `OwnSystem` is not a valid `employer` when `spawnLocation` is also `OwnSystem`.
    - `Allied`: The extended contract can spawn on any world whose controller is allied to the player.
    - Any faction ID (eg. `ClanJadeFalcon`): The extended contract can spawn on any world controlled by this faction.
  - `NearbyEnemy`: The employer must be an enemy of the system owner who controls a system nearby (within one jump of a system the employer controls). Valid entries in `employer` are:
    - `Any`: Any enemy of the system owner that controls a nearby system and isn't in `ignoreFactions` / `wontHirePlayer` can be the employer.
    - `OwnSystem` is not a valid `employer` when `spawnLocation` is `NearbyEnemy`.
    - `Allied`: Any enemy of the system owner that controls a nearby system and is allied to the player can be the employer.
    - Any faction ID (eg. `ClanJadeFalcon`): If the system owner is an enemy of the listed faction, and the listed faction controls a world within one jump, they can be the employer.
- And finally, there is `target` - this is determined after employer and system are chosen, and is an array of one or more of:
  - `Employer`: The OpFor will be the same as the employer.
  - `SystemOwner`: The OpFor will be the whoever owns the system.
  - `NearbyEnemy`: The OpFor will be randomly chosen enemies of your employer that control a system within one jump
  - Any faction ID (eg. `ClanJadeFalcon`). The target will be this faction, even if they don't control any systems in the area.
- `hireContract` is the ID of the travel contract WIIC will use to let the player get involved. This is always spawned as a travel contract, and no actual drop will occur.
- `availableFor` determines how long the travel contract will be available, min and max days. Once it expires, the extended contract disappears, never to be seen again.
- `schedule` is an array of strings, each one referencing an item in `entries` (see below). These occur each day in order, and when the player reaches the end, the extended contract is over.
- `entries` is an object of "day definitions", expanding on what each entry in the `schedule` means. These objects have a large number of options, described below in their own section.

### `entries`
Each entry is defined by an ID (the key, used in the extended contract type's `schedule` to say when this entry occurs), and a value with a large number of possible properties. All properties are optional - just leave out any that you don't need. An entry of `{}` is perfectly valid.

  - `triggerEvent` is an array of event IDs. Starting at the beginning, WIIC finds the first event with matching conditions and triggers it. If an event triggers, then everything else that could happen on the day is ignored - no contract or lootbox will be generated.
    - If none of the events trigger, other properties will be checked as normal.
  - If no event occurred, here is a `contractChance` chance that a contract will be offered to the player on this day. This defaults to 0 - if you want a contract to spawn, set it! The rest of the options control what sort of contract will be generated.
    - `contract`: A list of contracts to choose between at random, ignoring planetary difficulty but respecting each contract's `requirementList`. If empty or not present, then a contract will be chosen by vanilla logic (respecting `allowedContractTypes` below).
    - `allowedContractTypes`: A list of contract types the extended contract will select between. If the list is empty or not present, then any contract type (including those from vanilla and in WIIC's settings.json `customContractEnums`) is valid.
    - `contractPayoutMultiplier`: Pay for this contract is multiplied by this amount.
    - `contractBonusSalvage`: Added to the salvage this contract pays out. Can be negative. The final value will be clamped between 0 and 28 (so as not to break the UI).
    - `contractMessage`: A string to display in the popup offering the mission to the user. You do not need to explain the `declinePenalty` in here - WIIC will display that to the user separately. This can contain HBS madlib replacements, such as `{TEAM_EMP.FactionDef.ShortName}` and `{TGT_SYSTEM.Name}`. Notably, you can also access the contract itself under `{RES_OBJ.Name}` and `{RES_OBJ.ContractTypeValue.FriendlyName}` and similar.
    - If the player declines a generated contract, they're given the `declinePenalty`, which is one of:
      - No `declinePenalty` defined: No penalty for declining.
      - `BadFaith`: Reputation penalty as if they'd performed a bad-faith withdrawal.
      - `BreakContract`: Declining this mission terminates the extended contract as if the player had flown away.
  - Finally, if no event and no contract triggered, `rewardByDifficulty` gives a lootbox to the player, based on the half-skull rating of the planet - they will receive the highest value they met or exceeded. For example, if on an 8 difficulty world, WIIC will look for 8, then 7, 6, etc. until that key exists and give them that. If the player is given a lootbox, no contract will be generated.

## Generating Extended Contracts
Each day, after checking for flareups and raids if there are fewer than `maxAvailableExtendedContracts` available, WIIC decides if it should generate a new one. If there are currently no extended contracts available, it uses `dailyExtConChanceIfNoneAvailable` as the chance. If one or more already exist, it instead uses `dailyExtConChanceIfSomeAvailable`.

- If it decides to offer the player a new extended contract, it generates a list of all the extended contract types for which the player qualifies (based on their `companyRequirements`s), weighted by their `weight`s, and picks one.
- Builds a list of all valid employer and location pairs, as described by `spawnLocation` and the `employer` array. These are weighted by the distance multiplier, as Flareups: `1 / sqrt(distanceFactor + distanceInLyFromPlayer)`. Systems near the player are more likely to be chosen than those far across the map.
- Finally, it determines the target of the contract based on the `target` array.

## Extended Contract Ideas
  - Garrison Duty, which lasts 30 days and pays out at the end. It has a 20% chance of generating a contract every three days.
  - Covert Operations, which lasts 15 days (with no contracts) and then three contracts separated by one day each at the end.
  - Dueling Circuit, which lasts 35 days and spawns a new duel every 7 days.
  - Training Contract, which works like a normal Flareup except you only fight your employer, and your units are repaired after each contract. No salvage, reduced cash payout.

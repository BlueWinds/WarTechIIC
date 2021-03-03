WarTech IIC (WIIC) is a mod for HBS's Battletech computer game. It's inspired by the likes of WarTech, GalaxyAtWar, and RogueTech's PersistantMap.

# Flareups
WIIC adds Flareups to the map - conflicts between factions for control of entire planets. They play out similarly to Flashpoints, where the player travels to the system, accepts a contract to participate in the campaign, and is then offered a series of missions to help their side take (or retain) control of the planet. Here's the basic flow, and how it relates to the settings in `settings.json`.

## On game load
When a game is loaded or started, WIIC looks at `setActiveFactionsForAllSystems`. If `true`, it will override the active factions for every star system on the map. See [Active Factions](#setting-active-factions) for more details on how it determines which factions to set as active for each system. If `false`, nothing happens.

## Generating Flareups
Every day that passes, there is a `dailyFlareupChance` chance that a new flareup will be added to the map. There are no limits to the number of flareups that can be active at once, and they can occur from day 1 onwards. When a flareup occurs...

### Deciding attacker
WIIC first decides who will be the attacker in the new flareup.
1) Only factions that control at least one star system on the map are considered.
2) And factions in `ignoreFactions` are ignored.
3) A weight is assigned to each faction, the sum of several factors:
    1) `aggressionPerSystemOwned` is multiplied by the number of star systems the faction controls. This represents larger factions having more border worlds and more resources.
    2) The player's reputation with the faction is pulled from `aggressionByReputation` and added in. This gives extra weight to factions that the player is interested in, either by fighting against them frequently or by allying with them.
    3) The sum of the above two is multiplied by that faction's entry in `aggressionMultiplier`, if they have one. This means if you set their `aggressionMultiplier` to 0, they'll never attack people.

With the weight for each potential attacker figured out, one is selected at random.

### Deciding defender
With the attacker decided, WIIC looks to see where they will attack.
1) Factions in `cantBeAttacked`... can't be attacked.
2) If `limitTargetsToFactionEnemies` is `true`, then only a faction's enemies - as defined in their factionDef - are considered as possible targets. Otherwise, any faction they share a border with is fair game.
3) A weight is assigned to each defender, the sum of two factors.
    1) `targetChancePerBorderWorld` is multiplied by the number of border worlds between the attacker and this faction. A border world is considered any world you can reach in a single jump from one of the attacker's planets. If there are no border worlds, the faction is discarded as a potential defender.
    2) The `baseTargetChance` is added in.
4) Each defender's weight is multiplied by the value in `targetChoiceMultiplier[attacker][defender]`, if any. For example: `targetChoiceMultiplier: {ClanJadeFalcon: {ClanGhostBear: 10}}` makes the Falcons 10x as likely to attack the Bears as they would normally be.

With the weight for each potential defender figured out, one is selected at random.

### Initial setup
A border world controlled by the defender and near the attacker is chosen at random. The flareup is added to the map - this adds a blip to the starmap, appearance controlled by the `flareupMarker`. See [ColourfulFlashpoints](https://github.com/wmtorode/ColourfulFlashPoints) for details on the settings.

The initial attacker and defender forces are calculated as follows.
1) The attacker begins with `defaultAttackStrength` points, overridden by their setting in `attackStrength` if they have one.
2) The defender begins with `defaultDefenseStrength` points, overridden by their setting in `defenseStrength` if they have one.

Every few days, one side or other will lose strength, continuing until either the attacker is out of points and gives up, or the defender is out of points and the attacker takes the system. This process is detailed below.

## How Flareups proceed
When initially generated, flareups are in "countdown", a random number of days chosen based on `minCountdown` and `maxCountdown`. Nothing will happen until that many days pass - the attacker and defender are mustering their forces, preparing for the coming confrontation.

When the countdown reaches 0, the flareup begins ticking. Every `daysBetweenMissions` days, one side or the other - chosen with a coinflip - will lose between `combatForceLossMin` and `combatForceLossMax` points. When one side reaches 0, the *next time a mission would occur* the flareup ends. If the attacker wins, system control is flipped.

### Setting active factions
When a system flips control - or every system on game load, if `setActiveFactionsForAllSystems` is true - WIIC adjusts the planet's tags using the following steps:
1) All tags relating to system control (`planet_faction_*`) are removed.
2) A new tag for the new owner is applied (`planet_faction_{new owner}`).
3) A tag `WIIC_control_{new owner}` is added. WIIC uses this tag to persist system control when the game saves / loads.

The system's employers and targets are set with the following logic:
1) The system owner and Locals are always included.
2) If the system has any tag from `pirateTags`, then the Aurigan Pirates are added as active.
3) Any faction that shares a border with this world is added as active, unless they're in `ignoreFactions`.

## How the player gets involved
So that's cool and all, but what do players do?

Well, when they enter the star system where a flareup is occurring, each faction may generate a contract to hire the player ("Flareup: Attack Planet" or "Flareup: Defend Planet") if:
1) They are not listed in `wontHirePlayer`.
2) The players reputation with that faction is at least `minReputationToHelp`.

If the player accepts the contract, the "countdown" is immediately set to 0, and the next mission is set to begin tomorrow. They get a task in the timeline telling them when the next mission will occur.

On the same interval as automatic force loss, every `daysBetweenMissions` days, the player will be offered a mission with nothing more than the mission name. There's no penalty for passing, but if they accept, they *must* drop - no accepting the mission and then backing out! If they complete the mission, then the faction they fought against loses between `combatForceLossMin` and `combatForceLossMax` points, and the player gets a `flareupMissionBonusPerHalfSkull * (difficulty of mission)` cbill bonus.

While participating in a flareup, the player has to stay in the star system - if they attempt to leave, they will get a popup warning them of the consequences of breaking the contract. These aren't actually terribly severe, just reputation loss with the employer equal to one bad faith withdrawal from a mission.

# Simgame statistics and tags
WIIC reads and sets a variety of tags and statistics on companies and star systems. These can be used in conditions, set, updated or removed from events and flashpoints like any other stat or tag.

### Company Tags
* `WIIC_helping_attacker` and `WIIC_helping_defender` - If present, the company is in the middle of a flareup, helping the attacker/defender take the current system. You can remove this from events, and nothing will break. Adding it from events will force player participation if there's already a flareup in their current system, otherwise it won't do anything.
* `WIIC_give_{system}_to_{newOwner}` (eg: WIIC_give_Sol_to_ClanWolf) - Setting this will pass control of the named star system to the new owner. The tag won't actually added to the company - WIIC 'eats' it.
* `WIIC_{faction}_attacks_{system}` (eg: WIIC_ClanJadeFalcon_attacks_Sol) - Setting this will cause a new flareup to start in the given system, with the faction as the attacker, if one doesn't already exist. The tag won't actually added to the company - WIIC 'eats' it.
* `WIIC_set_{system}_{attacker|defender}_strength_10` (eg: WIIC_set_Sol_defender_strength_10) - Setting this will adjust the attacker or defender's strength in that system's flareup, if there is one. The tag won't actually added to the company - WIIC 'eats' it.
* `WIIC_{faction}_attacks_{defenderFaction}_x{count}` (eg: WIIC_ClanDiamondShark_attacks_ClanJadeFalcon_x3) - Setting this will spawn up to (count) new Flareups as the attacker goes after the defender all across their shared border. It may spawn fewer than that many, if WIIC can't find border worlds to create them on. The tag won't actually added to the company - WIIC 'eats' it.

### Company Stats
For all company stats, `-1` is a magic value - "ignore this". If present, we'll read the value from settings.json rather than the stat.

* `WIIC_dailyFlareupChance` (float) If present, this overrides the `dailyFlareupChance` from settings.json.
* `WIIC_{attacker}_aggressionMultiplier` (float) If present, this overrides `aggressionMultiplier[attacker]` from settings.json.
* `WIIC_{attacker}_hates_{defender}` (float) If present, this overrides `targetChoiceMultiplier[attacker][defender]` from settings.json.

# Custom Campaigns
While the Milestone and Flashpoint systems in the base game are extremely flexible and powerful, they are also cumbersome and verbose; custom campaigns end up spread across dozens of files totally thousands of lines of JSON. Ask me how I know. ^^;;

WIIC provides an alternative format for creating custom campaigns. Campaigns do not map exactly onto any one concept in the base game; they hook into many different places. They:
- Can trigger events, sim game conversations and play cutscenes
- Offer rewards (rewardes) and contracts
- Ask the player to fly to different star systems to continue the story
- Have their own simple language for flow control and conditions

The overall goal is to make creating campaigns a pleasant and simple experience. They are written in YAML, which is very human-readible and less prone to mistakes than JSON.

## How they work
Campaigns begin by adding a company tag: `WIIC_begin_campaign_{name}` (eg: `WIIC_begin_campaign_Sword of Restoration`). They will never spawn on their own; you'll need to write an event (or some other result) that gives the player a company tag.

Campaigns have only a few top-level fields:
 - `name` - The name of the campaign. This should match the filename, and will be displayed to the player.
 - `beginsAt` - A star system where the campaign begins.
 - `nodes` - A dictionary of named flow-control points. A campaign always begins with a node named `Start`.

Each node is a sequential list of Entries, representing something presented to the player, some action they must take, or a flow-control statement (`goto`) telling the campaign what happens next.

The final Entry in a node must *always* be `goto` without an `if` block. Flow control is never implicit, there is always a next step.

Example:
```
name: Sword of Restoration
beginsAt: starsystemdef_Coromodir
nodes:
  Start:
    - event:
        id: event_FP_Storytime0
    - goto: Exit
```

All entries can have a condition applied, using `if`:

```
  Start:
    - if:
        companyHasTag: <tagName>
        companyDoesNotHaveTag: <tagName>
```

The entry will only happen if all the given conditions are true; if any are false, the entry is skipped. Usually you'll only use one condition in an `if` block.

The various types of entries are explained below.

### `goto`
The basic flow-control statement of a Campaign. It moves the campaign from the current node to a different one. The first Entry in the given node triggers immediately.

Example:
```
  Start:
    - if:
        companyHasTag: some_tag
      goto: NextNode
    - goto: Exit
  NextNode:
    - <...etc...>
```

The value of `goto` is either the name of another node, of the special value `Exit` - the campaign is over!

### `event`
Triggers an event. Events (or conversations) are usually how players make decisions; use the `Options` in an event to add company tags, then follow up the `event` entry with forking `goto` based on their choice. The next Entry triggers once the event is concluded.

Example:
```
  Start:
    - event:
        id: event_FP_Storytime0
    - if:
        companyHasTag: accepted_storytime_campaign
      goto: Corronation
    - goto: Exit
```

`id` is, straightforwardly, an event id. Its requirements are ignored; use an `if` condition instead. Only events with `Company` or `Commander` scope are supported at the moment. If you really want another event scope, ask BlueWinds.

### `video`
Play a video cutscene. The next Entry triggers once the video is done playing (or the player skips it).

Example:
```
  - video: 1A-prologue.bk2
```

The value is the name of file. You can either use Videos from the base game (`Battletech_Data/Videos/*.bk2`) or a [custom video loaded with ModTek](https://github.com/BattletechModders/ModTek/tree/master?tab=readme-ov-file#custom-types).

### `reward`
Give the player an itemcollection. This pops up a reward dialogue. The next Entry triggers once the reward is dismissed.

Example:
```
  - reward: BTA_FP_ThreeRogueItemsA
```

### `fakeFlashpoint`
Add a fake flashpoint to the galaxy map. The campaign will be on hold until the player flys to that start system and accepts it. Beginning the fake "flashpoint" triggers the next Entry.

Example:
```
  - fakeFlashpoint:
      name: Arano Restoration - Coronation
      employer: AuriganRestoration
      employerPortrait: castDef_DariusDefault
      target: Unknown
      at: starsystemdef_Coromodir
      description: Your old mentor, Raju, has invited you to be one of [[DM.BaseDescriptionDefs[LoreKameaArano],Lady Kamea Arano's]] honor guards on the day of her coronation. Travel to [[DM.BaseDescriptionDefs[LoreCoromodir],Coromodir]] to meet with him. The [[DM.BaseDescriptionDefs[LoreAuriganCoalition],Aurigan Coalition]] will supply you with a mech for use in her procession, a venerable Shadowhawk that has been with the Aurigan Royal Guard for decades.
```

All fields are required, and displayed to the player. `employer` and `target` need not be who the player is actually going to fight for / against; they're display only.

`at` can be the current system; this is a fine way to offer the player breakpoints in the action that they can return to later.

### `contract`
Offer a contract in the command center. Its requirements are ignored; use an `if` condition instead. The campaign will be on hold until they complete the drop. If successful, it moves onto the next Entry. If the player fails the mission, or the mission expires, we instead `onFailGoto` (exactly as `goto`, explained above).

While a contract Entry is active, the player cannot leave the star system. Travel is completely blocked.

Example:
```
  - contract:
      id: StoryTime_3_Axylus_Default
      employer: AuriganRestoration
      target: AuriganPirates
      onFailGoto: CaptureTheScheria

      postContractEvent: forcedevent_FP_StoryTime3_A
      forced:
        maxDays: 0
```

`id`, `employer`, `target` and `onFailGoto` are required, all others are optional. `blockOtherContracts` defaults to false.

`postContractEvent` deserves some special explanation. If the player succeeds at the mission, the given event *replaces the objectives screen* in the after action report. The event must be `Company` or `StarSystem` scoped; no other scopes are supported.

A contract is either `forced` or `travel`:

```
forced:
  maxDays: 10
  
travel:
  at: starsystemdef_Coromodir
```

When a forced contract is available, it blocks all other contracts, and adds the contract in the current system, and prevents travel. `maxDays` is a ticking countdown of days; when it reaches zero (or immediately if it's set to 0), the player is forced into the drop, without a chance to opt out or further delay.

A travel contract is spawned `at` the given system, without a time limit; the player can take other contracts, travel around, do whatever; the contract will wait for them. It is common to spawn a `travel` contract `at` the current system; this is 100% valid.

### `conversation`
Trigger a SimGameConversation. Conversations are more immersive, but significantly more challenging to create, than Events. They also serve as an excellent place to offer the player choices. The next Entry triggers once the conversation is over.

Example:
```
  - conversation:
      id: StoryTime2-RentToOwn
      header: BETRAYAL AND DEBT
      subheader: In Orbit - Ur Cruinne
  - if:
      companyDoesNotHaveTag: refused_to_continue_storytime_campaign
    goto: Exit
```

All fields (`id`, `header`, `subheader`) are required. Create conversations using [ConverseTek](https://github.com/CWolfs/ConverseTek/).

### `wait`
Wait for some number of days to pass, optionally with a work order.

Example:
```
  - wait:
      days: 10
      
  - wait:
      days: 14
      workOrder: Wait for further details
      sprite: uixTxrLogo_SelfEmployed
```

`workOrder` and `sprite` are optional; the latter does nothing without the former. If there's no `workOrder`, the player will have no feedback that the campaiign is ongoing while the counter ticks down; use this sparingly.

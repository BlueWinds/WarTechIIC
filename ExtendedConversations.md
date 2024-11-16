# Extended Conversations
WIIC also provides a new format for creating sim game conversations a'la flashpoints and the base game campaign. Extended Conversations, or EConvs for short, do not map exactly onto HBS's Conversatioin API; this is not a one-for-one replacement for ConverseTek. It is both missing features for simplicity and clarity's sake, and also implements additional options that the base game does not support.

The overall goal is to make creating conversations pleasant and simple via JSON editing, with no import / export process or special cliant required.

## How they work

EConvs always begin with a black intro screen, showing the `introHeader` / `introSubHeader`. From there, theye fade into the conference room, showing characters gathered around the viewscreen.

A conversation is build of a series of `Nodes`, linked together by their `options`.

The conversation always opens with the `Entrypoint` Node (see below for an explanation of "Nodes"

## JSON Format
Extended Conversations are a custom resource type; to have the game load them, add them to your `mod.json`:

```
  "Manifest": [
    { "Type": "ExtendedConversation", "Path": "extendedConversations" }
  ]
```

Once that's done, you can create your own conversations. The top level fields are:

- `name` (REQUIRED): A string by which you'll refer to the conversation from other files. It is convenient to omit spaces, but that's not a requirement.
- `introHeader` (REQUIRED): First line on the opening screen of the conversation; usually something thematic about current events, like "Betrayal and Debt".
- `introSubHeader` (REQUIRED): Second line on the opening screen; usually explaining where or when a conversation is taking place. A star system, planet name, or "Three hours later", something of that sort.
- `nodes` (REQUIRED): An object with the keys being *the name of the node* and values being a `Node`. This is where most of the action takes place.

Simple, right?

### Nodes

Each node is itself an object.

- `speaker` (REQUIRED): Who is speaking. This must be one of:
  - `Sumire, Darius, Yang, Farah`: Known cast members.
- `text` (REQUIRED): A string to put on screen.
- `options` (REQUIRED): An object with the keys being what the commander says and the values being an `Option`. Basically, the commander's response to `text`.
  - If `options` has exactly one entry named `""` - empty string - then we instead give the player a "Continue" button.
- `showOnViewscreen`: Optionally, change what's displayed on the viewscreen.

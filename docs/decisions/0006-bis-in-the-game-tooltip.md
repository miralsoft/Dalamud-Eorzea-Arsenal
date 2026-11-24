# ADR 0006: BiS in the game's own tooltip, as a choice next to the overlay

- **Status:** Accepted
- **Date:** 2026-11-24

## Context

Since 0.2 the BiS information for a hovered item is an ImGui window docked to the game's tooltip, and the
overlay deliberately never touched the tooltip itself: nothing the plugin does could break it, and no patch
could move what it relies on (P2, P6). Players asked for the information inside the tooltip, where other
plugins already put theirs (item counts, sources, prices), so it reads as one piece.

## Decision

A setting chooses where it appears: the plugin's own window (the default, unchanged), the game's tooltip,
or both. In the tooltip the short form is appended to the item description field (index 13 of the
`ItemDetail` string array) in `PreRequestedUpdate` of the `ItemDetail` addon, through Dalamud's addon
lifecycle, the same field and moment other plugins use. The text is coloured with mid tones that read on
the light and the dark tooltip. The sourcing chain stays in the window, which has the room for it.

## Consequences

- The plugin now writes into the game's UI data. The handler is wrapped and logs a failure once; the
  tooltip then stays as the game made it, so a fault costs the BiS lines, never the client.
- A patch that renumbers the tooltip's fields can show the block in the wrong place until the field is
  corrected. The overlay stays the default, so a player who does not choose the tooltip is not affected.
- A second update of the same tooltip does not double the block: it is recognised by its heading.

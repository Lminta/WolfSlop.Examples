# The Brickyard

`level1.json` is the default playable level: an abandoned motor depot built around
a courtyard and its small utility hut. The car in the northern motor yard is the
destination landmark. Brick loading bays, stone arcades, green hanging lamps, and
barrel stores keep the spaces visually related.

Shared walls use different textures on their exposed faces: brick toward the
warehouse and workshop, stone toward the courtyard arcades, and mossy masonry
toward the service store. These partitions are one tile thick. The office has
wood-lined walls, while the utility hut keeps its brick exterior and weathered
stone interior, with a single outward-facing eagle plaque.

Stone loading pads break up the timber floors and mark the storage bays. Arcade
columns, masonry piers, hanging lamps, and a covered parts store beside the car
give each space a purpose. Mossy paving follows sheltered edges and the hut's
foundations. Storage stays against walls so the main paths and door approaches
remain clear.

The player starts in the southwest gatehouse at (6.5,26.5), facing its glass door.
The main route is wide enough for both camera modes. Follow the covered south
arcade into the courtyard, enter the warehouse on its east side, then head north
through the glass loading door to the motor yard. The warehouse's two courtyard
entrances and internal sliding partition create a loop around its storage bays.

A shorter service route leaves through the north gatehouse door. Jump the spilled
barrels, turn left at the junction, then crouch beneath the hanging columns to
reach the old workshop. Cross the workshop and its glass exit to emerge beside
the car. The junction also connects back into the courtyard, so either route can
be used for a return trip. A side store and the courtyard utility hut reward
exploring away from the main route.

```text
                   NORTH (-Y)
    [Office]--[Workshop]--glass--[Motor yard / car]
       |                               |
   low columns                       glass
       |                               |
       +----junction--door--[Courtyard]--door--[Loading hall]
               |               hut              |
         fallen barrels                         door
               |                                |
             door       [Covered arcade]--door--[Barrel store]
               |              |
           [Gatehouse]--glass--+
              START
```

Coordinates use `worldMap[x][y]`, with north toward decreasing Y. The diagram
describes room connections rather than tile scale.

| Feature | Tile |
| --- | --- |
| Gatehouse glass door | (10,26) |
| Service entrance | (6,22) |
| Jumpable barrel spill | (6,17) |
| Low overhead columns | (3,11) |
| Service / courtyard junction door | (10,14) |
| Courtyard utility hut entrance | (17,19) |
| Warehouse entrances | (25,15), (25,26) |
| Warehouse partition door | (29,21) |
| Warehouse / motor yard glass door | (29,9) |
| Workshop / motor yard glass door | (18,5) |
| Parked directional car | (29,5) |

Press F while facing a door from a neighboring tile. Space jumps; hold Alt to
crouch. The service obstacles have clear approaches and landings in both
directions. Decorative barrel stacks, columns, and the car block their owning
tile; hanging lights do not block movement.

Open courtyard tiles share a stone floor with the mesh-roofed arcades. Every
doorway and prop retains its area's floor and ceiling. The warehouse and workshop
use wood floors and stone ceilings; the gatehouse uses a blue stone floor and the
service passage uses mossy stone. Level-specific definitions have `brickyard` in
their filenames, so editing these presets does not change the Showcase map.

# Vanilla event preconditions (Stardew Valley 1.6.15)

Every precondition registered in `StardewValley.Preconditions`, extracted by decompiling 1.6.15. The short alias is in parentheses. Either form works in an event key: `Author.MyMod_Scene/n SomeFlag/F/t 1700 2100`.

- Separate preconditions with `/` in the event key. Arguments within one precondition are space-separated.
- Any precondition can be negated with a leading `!` (e.g. `!Weather rainy`). **VERIFY** this in 1.6.15's `Event.CheckPrecondition` before exposing it in the editor.
- The SDCS editor should offer these as a picker with argument fields, plus a free-text "advanced" box (validated: no `/`).

| Name | Alias | Args | Meaning |
|---|---|---|---|
| SawEvent | `e` | `<eventId>…` | has seen any of these events |
| NotSawEvent | `k` | `<eventId>…` | hasn't seen any of these |
| MissingPet | `h` | `<cat\|dog>` | no pet yet (and the preference is this type) |
| IsHost | `H` | | the main player |
| HostMail | `Hn` | `<flag>` | the host has this mail flag |
| NotHostMail | `Hl` | `<flag>` | |
| WorldState | `*` | `<id>` | world state ID is set |
| HostOrLocalMail | `*n` | `<flag>` | host or local player has the flag |
| NotHostOrLocalMail | `*l` | `<flag>` | |
| EarnedMoney | `m` | `<amount>` | total earned ≥ amount |
| HasMoney | `M` | `<amount>` | current money ≥ amount |
| FreeInventorySlots | `c` | `<count>` | |
| CommunityCenterOrWarehouseDone | `C` | | |
| NotCommunityCenterOrWarehouseDone | `X` | | |
| Dating | `D` | `<npc>` | |
| DaysPlayed | `j` | `<days>` | days played > N (**VERIFY** > or ≥) |
| JojaBundlesDone | `J` | | |
| Friendship | `f` | `<npc> <points> [<npc> <points>…]` | friendship ≥ points (250 = 1 heart) |
| FestivalDay | | | today is a festival |
| NotFestivalDay | `F` | | |
| Random | `r` | `<chance>` | e.g. `r 0.2` |
| Shipped | `s` | `<itemId> <count> …` | |
| SawSecretNote | `S` | `<noteId>` | |
| ChoseDialogueAnswers | `q` | `<answerId>…` | |
| LocalMail | `n` | `<flag>` | the **current** player has this mail flag (SDCS's main story-state check) |
| NotLocalMail | `l` | `<flag>` | |
| GoldenWalnuts | `N` | `<count>` | |
| InUpgradedHouse | `L` | `[level]` | |
| Time | `t` | `<min> <max>` | e.g. `t 1700 2100` |
| Weather | `w` | `<rainy\|sunny\|…>` | |
| DayOfWeek | | `<day>…` | e.g. `DayOfWeek Mon Tue` |
| NotDayOfWeek | `d` | `<day>…` | |
| Spouse | `O` | `<npc>` | |
| NotSpouse | `o` | `<npc>` | |
| Roommate | `R` | | |
| NotRoommate | `Rf` | | |
| NpcVisible | `v` | `<npc>` | the NPC isn't hidden |
| NpcVisibleHere | `p` | `<npc>` | the NPC is in this location |
| Season | | `<season>…` | |
| NotSeason | `z` | `<season>…` | |
| SpouseBed | `B` | | |
| ReachedMineBottom | `b` | `<count>` | |
| Year | `y` | `<year>` | year ≥ N (**VERIFY**: `y 1` is "year 1 only" in older versions) |
| Gender | `g` | `<male\|female>` | |
| HasItem | `i` | `<itemId>` | in inventory |
| Tile | `a` | `<x> <y> [<x> <y>…]` | the player is standing on one of these tiles |
| ActiveDialogueEvent | | `<id>` | |
| NotActiveDialogueEvent | `A` | `<id>` | |
| SendMail | `x` | `<letterId> [true]` | sends mail and marks the event seen; used for mail-only "events" |
| DayOfMonth | `u` | `<day>…` | |
| UpcomingFestival | | `<days>` | |
| NotUpcomingFestival | `U` | `<days>` | |
| GameStateQuery | `G` | `<query…>` | the rest of the segment is a GSQ, e.g. `G PLAYER_HAS_MAIL Current SomeFlag` |
| Skill | | `<skill> <level>` | |

The argument column is from the vanilla conventions and the wiki, **not** all verified against 1.6.15. Decompile `StardewValley.Preconditions <Name>` when exact behaviour matters (doc 08 §1.1).

Also available: `Event.RegisterPrecondition(name, handler)` and `Event.RegisterPreconditionAlias(alias, name)`, if SDCS ever needs a custom precondition (e.g. `SDCS_Ready`). v1 doesn't.

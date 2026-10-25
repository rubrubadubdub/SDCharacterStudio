# Vanilla event commands (Stardew Valley 1.6.15)

Every command name registered in `StardewValley.Event` (from `Event.DefaultCommands`), extracted by decompiling 1.6.15. Names are **case-insensitive** in scripts. Scripts conventionally use lower camel case (`faceDirection`, `addMailReceived`). Aliases are in parentheses.

- **Args**: argument signatures read from the decompiled `ArgUtility` calls (VERIFIED) for the commands SDCS uses. Others are listed by name only. Decompile `StardewValley.Event+DefaultCommands <Name>` before using them (doc 08 §1.1).
- Waiting behaviour matters for the compiler: a command that **waits** holds the event until something finishes.
- The best general reference for the rest is the Stardew Valley wiki, "Modding:Event data". Cross-check it against the decompiled code, since the wiki can lag behind the game version.

## Commands SDCS compiles to (verified arguments)

| Command | Args | Waits? |
|---|---|---|
| `speak` | `<actor> "<text>"` | until the dialogue box closes (the close increments) |
| `message` | `"<text>"` | until closed |
| `quickQuestion` | `<question>#<a1>#<a2>…(break)<cmds1>(break)<cmds2>…` (cmds separated by `\`) | until answered |
| `question` | `<forkId> "<question>#<a1>#<a2>"` (legacy fork) | until answered |
| `switchEvent` | `<key>` (in `Data/Events/<current location>`) | replaces all commands, index 0 |
| `pause` | `<ms>` | yes |
| `precisePause` | `<ms>` | yes (**VERIFY** difference from `pause`) |
| `move` | `<actor> <dx> <dy> <facing>` (groups of 4, repeatable) `[continue]` | until moves finish, unless `continue` is `true` |
| `speed` | `<actor> <speed>` | no |
| `faceDirection` | `<actor> <dir> [continue]` | no |
| `warp` | `<actor> <tileX> <tileY> [continue]` | no |
| `positionOffset` | `<actor> <dxWorldPx> <dyWorldPx> [continue]` | no |
| `drawOffset` | `<actor> <dx> <dy>` (×4 → world px) | no |
| `emote` | `<actor> <emoteId> [continue]` | until the emote ends, unless `continue` |
| `shake` | `<actor> <ms>` | no |
| `jump` | `<actor> [jumpV=8] [noSound=false]` | no |
| `animate` | `<actor> <flip> <loop> <frameMs> <frame> [frame…]` | no |
| `showFrame` | `<actor> <frame> [flip]`, or `<frame>` for the farmer | no |
| `stopAnimation` | `<actor> [frame]` (**VERIFY** the optional frame) | no |
| `hideShadow` | `<actor> <bool>` | no |
| `changeSprite` | `<actor> [suffix]` (none = restore) | no |
| `changePortrait` | `<npc> [suffix]` (none = restore) | no |
| `textAboveHead` | `<actor> "<text>"` | no |
| `playSound` | `<cue>` | no |
| `stopSound` | `<cue> [immediate]` (**VERIFY**) | no |
| `playMusic` | `<cue>` | no |
| `stopMusic` | | no |
| `viewport` | `<tileX> <tileY> [clamp] [fade] [option]`, or `<actorName\|player> …`, or `move <dxPerTick> <dyPerTick> <ms>` | no (the `move` form pans over time) |
| `globalFade` | `[speed=0.007] [continue=false]` | until the fade ends, unless `continue` |
| `globalFadeToClear` | `[speed] [continue]` | same |
| `fade` | `[unfade]` | until black |
| `screenFlash` | `<alpha>` | no |
| `friendship` | `<npc> <points>` | no |
| `money` | `<amount>` | no |
| `addItem` | `<itemId> [count] [quality]` | no (may open the item menu if the inventory is full) |
| `removeItem` | `<itemId> [count]` | no |
| `itemAboveHead` | `[type\|itemId] [showMessage]` | no |
| `addMailReceived` (`mailReceived`) | `<flag> [add=true]` (**`false` removes the flag**) | no |
| `mail` | `<letterId>` (mail for tomorrow) | no |
| `mailToday` | `<letterId>` | no |
| `addQuest` / `removeQuest` | `<questId>` | no |
| `addConversationTopic` | `<topicId> [days]` | no |
| `addTemporaryActor` | `<spriteAsset> <w> <h> <tileX> <tileY> <facing> [isBreather=true] [character\|animal\|monster\|displayName] [overrideName]` | no |
| `temporaryAnimatedSprite` | `<texture> <x> <y> <w> <h> <interval> <length> <loops> <tileX> <tileY> <flicker> <flip> <layerDepth> <alphaFade> <scale> <scaleChange> <rotation> <rotationChange> [flags…]` | no |
| `removeTemporarySprites` | | no |
| `glow` | `<r> <g> <b> [hold]` | no |
| `stopGlowing` | | no |
| `makeInvisible` | `<tileX> <tileY> [w] [h]` (hides map objects) | no |
| `skippable` / `unskippable` | | no |
| `setSkipActions` | `<triggerAction>#<triggerAction>…` | no |
| `beginSimultaneousCommand` / `endSimultaneousCommand` | | no |
| `end` | `[variant …]` (doc 08 §2.8) | ends |

Note: `addMailReceived <flag> false` **removes** a flag (verified signature: arg 2 `bool add`). So the `Mail` beat's `Remove` can compile to vanilla `addMailReceived <flag> false` instead of `SDCS_Mail`. Prefer the vanilla command.

## Full list of command names (1.6.15)

```
IgnoreEventTileOffset   Move                    Action                  Speak
BeginSimultaneousCommand EndSimultaneousCommand MineDeath               HospitalDeath
ShowItemsLost           End                     LocationSpecificCommand Unskippable
Skippable               SetSkipActions          Emote                   StopMusic
PlayPetSound            PlaySound               StopSound               TossConcession
Pause                   PrecisePause            ResetVariable           FaceDirection
Warp                    WarpFarmers             Speed                   StopAdvancedMoves
DoAction                RemoveTile              TextAboveHead           ShowFrame
FarmerAnimation         IgnoreMovementAnimation Animate                 StopAnimation
ChangeLocation          Halt                    Message                 AddCookingRecipe
ItemAboveHead           AddCraftingRecipe       HostMail                Mail
MailToday               Shake                   TemporaryAnimatedSprite TemporarySprite
RemoveTemporarySprites  Null                    SpecificTemporarySprite PlayMusic
MakeInvisible           AddObject               AddBigProp              AddFloorProp
AddProp                 RemoveObject            Glow                    StopGlowing
AddQuest                RemoveQuest             AddSpecialOrder         RemoveSpecialOrder
AddItem                 AwardFestivalPrize      AttachCharacterToTempSprite Fork
SwitchEvent             GlobalFade              GlobalFadeToClear       Cutscene
WaitForTempSprite       Cave                    UpdateMinigame          StartJittering
Money                   StopJittering           AddLantern              RustyKey
Swimming                StopSwimming            TutorialMenu            AnimalNaming
SplitSpeak              CatQuestion             AmbientLight            BgColor
ElliottBookTalk         RemoveItem              Friendship              SetRunning
ExtendSourceRect        WaitForOtherPlayers     RequestMovieEnd         RestoreStashedItem
AdvancedMove            StopRunning             Eyes                    AddMailReceived (alias: mailReceived)
AddWorldState           Fade                    ChangeMapTile           ChangeSprite
WaitForAllStationary    ProceedPosition         ChangePortrait          ChangeYSourceRectOffset
ChangeName              TranslateName           ReplaceWithClone        PlayFramesAhead
ShowKissFrame           AddTemporaryActor       ChangeToTemporaryMap    PositionOffset
Question                QuickQuestion           DrawOffset              HideShadow
AnimateHeight           Jump                    FarmerEat               SpriteText
IgnoreCollisions        ScreenFlash             GrandpaCandles          GrandpaEvaluation2
GrandpaEvaluation       LoadActors              PlayerControl           RemoveSprite
Viewport                BroadcastEvent          AddConversationTopic    Dump
EventSeen               QuestionAnswered        GainSkill               MoveToSoup
```

Also useful: `Event.RegisterCommandAlias(alias, name)` exists, and `Event.TryResolveCommandName`.

## 1.6 trigger actions (for `setSkipActions`)
`Null`, `If`, `AddBuff`, `RemoveBuff`, `AddMail`, `RemoveMail`, `AddQuest`, `RemoveQuest`, `AddSpecialOrder`, `RemoveSpecialOrder`, `AddItem`, `RemoveItem`, `AddMoney`, `AddFriendshipPoints`, `AddConversationTopic`, `RemoveConversationTopic`, `IncrementStat`, `MarkActionApplied`, `MarkCookingRecipeKnown`, `MarkCraftingRecipeKnown`, `MarkEventSeen`, `MarkQuestionAnswered`, `MarkSongHeard`, `RemoveTemporaryAnimatedSprites`, `SetNpcInvisible`, `SetNpcVisible`.

Arguments (verified):
| Action | Args |
|---|---|
| `AddMail` / `RemoveMail` | `<Current\|Host\|All> <mailId> [now\|tomorrow\|received]` (target values: **VERIFY** the enum names) |
| `AddMoney` | `<amount>` |
| `AddItem` | `<itemId> [count] [quality]` |
| `RemoveItem` | `<itemId> [count]` |
| `AddFriendshipPoints` | `<npc> <points>` |
| `AddQuest` / `RemoveQuest` | `<questId>` |
| `AddConversationTopic` | `<topicId> [days]` |
| `MarkEventSeen` | `<target> <eventId> [seen=true]` |
| `IncrementStat` | `<statKey> [amount]` |
| `SetNpcInvisible` | `<npc> <days>` |
| `SetNpcVisible` | `<npc>` |

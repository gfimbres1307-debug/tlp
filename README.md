# TLP (Dalamud plugin)

Venue games for The Last Protocol in one window. Pick the game from the **Game** dropdown at the top;
the **Announce in** box sets the chat channel (default `/sh`) for every game.

**3-2-1 countdown:** every game shouts "3... 2... 1... GO!" (one second apart) before rolls or answers count.
Anything rolled or answered before GO is ignored. Toggle it with the **3-2-1 countdown** box at the top.

## Truth or Dare

1. **Start round + shout**: everyone `/random`s for 60 seconds (after the countdown).
2. Time's up: it shouts who rolled highest and that they ask the lowest roll "Truth or Dare?".
   (Ties go to whoever rolled first.)
3. Stuck for ideas? **Shout a random TRUTH / DARE** from your list, or pick a specific one.
4. **Truths & Dares** tab: add, delete (Ctrl-click) or bulk-paste your own (`T: ...` / `D: ...`).
   It comes loaded with flirty/spicy starters you can edit.
5. **Next round** starts again straight away.

## System Crash

Elimination by `/random`:
1. Type a prize and click **Start System Crash + shout**. Everyone who `/random`s in round 1 is in.
2. Each round lasts 90 seconds (changeable). When time is up the plugin shouts who crashed (lowest roll) and
   starts the next round in the same message. Turn off "automatically" if you'd rather click to announce.
3. Crashed players' rolls are ignored for the rest of the game. Players who don't roll in a round crash too
   (can be switched off in Settings).
4. When two are left, the final round is highest roll wins. Ties are re-rolled by just the tied players (30 s).
5. You can **Crash** (remove) or **Revive** anyone by hand, **End round now**, or Ctrl-click **Stop game**.
6. **History** keeps winners, player counts and rounds.

## Giveaway

1. **Pick prize from inventory**, then hover the item in your inventory (or type a prize such as "1,000,000 gil"). Set the quantity.
2. Choose **Highest roll**, **Lowest roll** or **Closest to** a number. Rolls are taken for 90 seconds (changeable; 0 = you close it).
3. **Start giveaway + shout** tells everyone the prize, to type `/random`, and how to win.
4. Every `/random` you can see is listed and ranked live (first roll per person; `/random 10` style rolls are ignored).
5. When rolls close it shows the winner. **Announce** shouts them to come claim it, and **Send them a /tell** messages them.
   Ties: shout a re-roll that only the tied players count in, or pick one yourself.
6. The **History** tab keeps every giveaway and winner.

Note: rolls are read from the English game client's "Random! ... rolls a ..." lines, and you can only see rolls from people near you (or in your party).

## Trivia

- Keep your own bank of questions and answers (add, edit, bulk import `Question | Answer`).
- Click **Ask** and the question is shouted (or sent to whatever channel you set).
- Every player's answer from Say / Shout / Yell / Tells / Party is collected into a list, with
  anything close to your answer highlighted green.
- Tick whoever got it right and click **Award**: each gets +1 point and +100,000 gil owed, and the
  plugin shouts who got it right. Or click **Nobody got it** to shout the answer.
- The Scoreboard tracks points, gil won and gil still unpaid (mark paid once you've traded).
- End of night: it shows the leader(s) and **Announce winner** shouts the 5,000,000 gil winner.
- Everything (questions, scores, settings, message wording) is saved between sessions.

The plugin never trades gil and never sends chat on its own: every message is sent only when you click a button.

Open it with **/tlp** (or **/tlp trivia** to jump straight to Trivia).

## Build it (two ways)

### A. Let GitHub build it (no installs)
1. Make a free GitHub account, create a new repository, and upload everything in this folder
   (keep the `.github` folder).
2. Open the **Actions** tab, wait for "Build TLP" to finish (green tick), click it and
   download the **TLP** artifact.
3. Unzip it somewhere, e.g. `C:\FFXIVPlugins\TLP\`.

### B. Build on your PC
1. Install the .NET SDK (9 for Dalamud API 13; if Dalamud has moved to API 14, install .NET 10).
2. Have XIVLauncher with Dalamud installed (it provides the references).
3. In this folder run: `dotnet build TLP/TLP.csproj -c Release`
4. The plugin is in `TLP/bin/x64/Release/`.

If the build complains the SDK version is wrong, edit the first line of `TLP.csproj`
(`Dalamud.NET.Sdk/13.0.0`) to the newest version listed at nuget.org/packages/Dalamud.NET.Sdk.

## Load it in game
1. Type `/xlsettings` -> **Experimental** tab.
2. Under **Dev Plugin Locations** add the full path to `TLP.dll`, click **+**, then **Save**.
3. Type `/xlplugins` -> **Dev Tools** -> enable **TLP**.
4. Type `/tlp`.

## Running a night
1. **Questions** tab: add your questions.
2. **Settings**: check the channel (`/sh` by default), prize amounts, and which chats count as answers.
3. **Host** tab: **Ask next question**. Watch answers come in, **Close answers** when time's up,
   tick the right ones, **Award**.
4. **Scoreboard**: **Shout standings** any time; at the end **Announce <name> as the winner**.
   Pay out in game, then **Mark paid**.
5. Next event: hold **Ctrl** and click **Start a new night**.

## Adding a new game
Create a folder under `TLP/Games/`, make a class that implements `IGame` (Name, Draw, OnChatMessage),
put its saved settings on `Configuration`, and add it in `Plugin.cs` with `Games.Add(new YourGame(this));`.

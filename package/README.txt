DAVE THE DIVER - ULTRAWIDE FIX
==============================

Lets Dave the Diver use the full width of ultrawide monitors (21:9, 32:9, ...)
instead of showing pillarbox art at the sides.

  * The game world (diving, boat, sushi bar, cutscenes) fills the whole screen.
  * The HUD and menus stay in a centered 16:9 area, the way the game was designed,
    so nothing ends up in the far corners of your screen.
  * Screen fades, dimmed backgrounds behind popups and cutscene bars are extended
    to the full width.
  * The small pixel-art scenes (e.g. after a Time Skip) fill the screen height,
    and loading screen backgrounds fill the width.
  * Bubbles and markers that follow things in the world (sushi bar orders,
    speech bubbles, damage numbers) stay above their target.
  * Story characters' boats that wait just outside the old 16:9 view on the
    boat stay hidden until they sail in.
  * Below 16:9 or at 16:9 the game behaves exactly as without the fix.

Tested with the Steam version v1.0.6.2113 at 3440x1440.


INSTALL
-------
1. Open your game folder: in Steam, right-click Dave the Diver >
   Manage > Browse local files. (This is the folder with DaveTheDiver.exe.)
2. Copy EVERYTHING from this zip into that folder (the BepInEx and dotnet
   folders, winhttp.dll, doorstop_config.ini and .doorstop_version).
   Say yes if Windows asks to merge folders.
3. Start the game from Steam.
   THE FIRST LAUNCH TAKES 1 TO 3 MINUTES before the game window appears,
   because the mod loader has to prepare itself. Later launches are normal.
4. In the game: Settings > Graphics, pick your monitor's resolution
   (for example 3440 X 1440) and "Full Window Mode" or "Fullscreen".


UNINSTALL
---------
Delete these from the game folder:
  BepInEx (folder), dotnet (folder), winhttp.dll, doorstop_config.ini, .doorstop_version
Or use Steam > Verify integrity of game files after deleting winhttp.dll.


OPTIONS
-------
After the first launch you can edit
  BepInEx\config\dtd.ultrawide.cfg
with Notepad:

  Enabled          Turn the whole fix on/off.
  WidenOverlays    Extend fades and popup backgrounds to the full width.
  ClipUIToCenter   Hide UI that the game keeps just outside the 16:9 area
                   (otherwise it can show up at the edges).
  FixWorldUI       Keep sushi bar order bubbles, speech bubbles, damage numbers
                   etc. above their target.
  ScaleCutscenes   Show the small pixel-art scenes (e.g. after a Time Skip) at
                   full screen height instead of 2/3 size.
  CoverImages      Full-screen background pictures that are zoomed to fill the
                   width instead of showing bars next to them.

The mod's log is in BepInEx\LogOutput.log. To see a live log console while
playing, set Enabled = true under [Logging.Console] in BepInEx\config\BepInEx.cfg.


TROUBLESHOOTING
---------------
* Game doesn't start / crashes after a game update: BepInEx rebuilds its files
  automatically after an update (the first launch is slow again). If it keeps
  failing, delete the folders BepInEx\interop and BepInEx\cache and try again.
* Antivirus complaints about winhttp.dll: that file is the BepInEx loader
  (Doorstop). It's what loads the mod into the game.
* Steam Deck / Linux (Proton): add  WINEDLLOVERRIDES="winhttp=n,b" %command%
  to the game's launch options.


CREDITS
-------
Built on BepInEx 6 (https://github.com/BepInEx/BepInEx, LGPL-2.1),
bleeding-edge build 6.0.0-be.789.
Not affiliated with MINTROCKET or NEXON.

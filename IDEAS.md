# Ideas

What the owner wants to build later, kept here so it is not lost. None of it is built, and none of it should be until the owner asks. Each idea has a note on where it would meet the code as it stands; `ARCHITECTURE.md` describes that code.

## The suit

The jetpack, the bike and every carried firearm make way for one suit of powered armour.

- **The look:** a cyberpunk take on medieval plate. Fluted and riveted plates, a great helm with a glowing visor slit, mail of cable and conduit, seams that glow with heat. Built in `Models.cs` like everything else, and seen in first person as gauntlets in place of the guns in `Arms.cs`.
- **It survives anything a world can do:** vacuum, the sun's radiation, furnace heat and deep cold. Today `Climate`, `Combat.Scorch` and `Player.Irradiated` hurt the player on foot; the suit's limits would sit far above anything outside a star or a giant's depths.
- **It flies, but not to space.** Thrusters carry it anywhere over a world, as the jetpack does well off the ground today, but never to escape velocity: `Walker` already holds a leap short of orbital speed on a small moon. Reaching space stays the ship's job.
- **What it fights with:**
  - **Fists:** melee punches of extreme strength that throw foes back and break the ground. A short-range strike in `Combat`, digging with `Dig` like a blast.
  - **Beams from the hands:** see Beam weapons below.
  - **Missiles from the back:** the launcher's homing missiles, fired from launchers on the shoulders.
- **The campaign:** `Gear` would hold suit upgrades instead of guns and vehicles: thrusters, gauntlet beams, back missiles, heavier plate and shielding. The missions in `Campaign.Missions` win those instead.
- **Upgrades are choices, not a ladder.** The suit has a few slots and one energy budget shared by its beams, thrusters and shields, so not everything won can be worn at once. A player builds a style: brawler, beams, missiles or stealth, swapped at the robots' workshop.
  - Where it fits: today `Player.Has` asks whether something has been won. It would ask whether it is fitted, from a loadout kept in the save beside what has been won.

## The hermit becomes a robot

- **What he is:** a robot AI, not a man. He speaks to the player remotely anywhere, joins the ship as copilot once it is won, and narrates the flight: arrivals, raiders inbound, heat, radiation, landings.
- **Where it fits:** `Hermit.cs` already rides as copilot (`Aboard`) and speaks up unasked (`Remark`). Narration would hang off what `Game` already notices: arrival, raids, the hull failing, the landing charge.
- **What changes:** his persona text, his model in `Models.Camp` and his voice. `Speech` could process the system voice so it sounds synthetic.
- **He knows more than he says.** About who the player is, why they were buried and what the presence is. Early on he deflects and lets things slip; later the truth comes out. Whether he made the player, or is a splinter of the enemy mind himself, is still to be decided.
  - Where it fits: his set lines in `Campaign.Missions` seed the hints. The oracle's persona in `Hermit.cs` would be told the secret and how much of it he may give away at each stage, so free talk never spoils it.

## The opponent: one mind, many bodies

An enemy that is one species with one will: nothing to bargain with and no end to it. The names, look and story are the game's own.

- **Every raider and drone is a body of one alien mind.** Each world's outpost hides a fixed controlling mind that directs them. Kill the mind and its bodies falter: they stop hunting in step and scatter. That gives each mission a real target to reach.
  - Where it fits: a new `FoeKind` that does not move, which `Outpost` guards, and which changes how `Combat.Hunt` steers its bodies once it is dead.
- **They cannot be talked to.** The hermit can only tell the player what he has overheard. The oracle could write their intercepted thought-chatter as eerie radio.
- **They copy what they capture.** Their ships take up each weapon soon after the player wins it, which is why each world fights harder.
- **They never stop spreading.** The presence is a clock on the whole system: it takes world after world as the hours of play go by, and each world it holds visibly worsens. A blight creeps over the ground, the sky darkens, outposts multiply and raids come harder. Every world taken back pushes it back.
  - Where it fits: how far it has spread would be kept in the save and grow with time played. `Battle` would take its threat from it as well as from the missions, `Terrain.shader` would draw the blight, and the map would show its reach. Whether it can ever win outright, or only press harder, is still to be decided.
- **A twist for the end:** the radiation boundary was what kept them penned in this system, and the gate the player is mending to leave it is what would let them out.

## More than one way through a mission

A place of theirs can be taken by fighting in, sneaking in, hacking in (turning its turrets and doors) or digging in from under the ground with the suit's fists. Their bodies react to noise and to light, so a quiet way in stays quiet.

- Where it fits: `Outpost` would give its guards states (calm, searching, alarmed) and `Combat.Hunt` what each body can see and hear. Digging in uses the ground that can already be dug. Most of it matters most inside places (see Fighting inside).

## Shielded bases

Their bases are shielded against anything from orbit. Ship's guns, missiles and even nuclear torpedoes break on the shield, so a base cannot be blown up from the ship: it has to be gone into, on foot in the suit, or by ship where the way in is big enough.

- Where it fits: a strike that lands inside a shield would do nothing in `Battle` but light the shield up. Torpedoes would be kept for what is not shielded: their ships, and the ground.

## Into the citadel by ship

The first half of the end. The ship flies into the citadel itself: down its tunnels at speed, through turning passages and past its guns to the core that powers the shield over the enemy's last base, then out again before it blows.

- Where it fits: the ship can only hit the ground's height function today, so the tunnels need collision of their own, as rooms inside places do (see Fighting inside). `Effects` already draws the end of it.

## The last base: dropping a moon

The second half of the end. With the citadel's core gone, the last base's shield is down, but the base is dug so deep into its world that nothing short of breaking the world will reach it. So a moon does: engines strapped to a small moon push it out of orbit and onto that world, and the whole world goes with the base, like a nuclear blast on the scale of a planet.

- Where it fits: orbits are on rails (`Orbit`), so a moon that is pushed needs a path of its own from its orbit to the impact, worked out in the core. A world that is destroyed would be taken out of the system, with its wreckage left as a ring of rubble (`Rings`, `RingField`) and an `Effects` blast to match. Its gone place would show in every other world's sky.

## Weapons

- **Rail gun:** a slug so fast it lands at once, with a long reach and a long recharge, that goes through one foe and into the next. `Combat.Step` stops a bolt at the first foe it crosses, so going through would need a change there.
- **Beam weapons:** a continuous beam that hurts whatever it touches while the trigger is held, drawn as a line of light. `Combat` would need a beam as a ray tested each step, not a bolt, and `Effects` a way to draw one and its burn where it touches.

## Fighting inside

On some planets the player has to get around inside a place, such as a base, a bunker or the citadel, and kill the people in it.

- **The ground is not enough here.** `Walker` stands on the ground's height function alone. Inside, it would need floors, walls and stairs: a simple grid of rooms with collision of its own, kept apart from the terrain.
- **Foes would need to walk:** today's drones hover and raiders fly.
- **The other parts:** doors, interior models and lighting.
- **The biggest change on this list.** Worth a design of its own before it is started.

## Radar

The owner's words: "Radar isn't quite right". What is wrong has not been said, so ask before changing it.

- **How it works now:** a disc seen from behind and above, ahead at the top. Foes are blips on stalks for how far above or below they are, 150 m out on foot and 3 km in the ship, with a mark for where the mission leads.
- **Possible fixes:**
  - Turn it with the ground, not the ship.
  - Label its rings with their range, and change its scale smoothly with speed.
  - Show the ship and the hermit.
  - Draw the ground's horizon on it.

## The gate, and the systems beyond

- **Leaving the system is by a gate, not a drive of the ship's own.** The system has a gate that is broken and without power. Mending and powering it is what the later missions and the robots' work are for, and once it runs it takes the ship on to the next system. This replaces the warp drive won from the citadel (`Gear.Warp`, `Game.Warp`).
  - Where it fits: the gate would be a body of its own, as the raiders' station is (`BodyKind.Station`), with missions in `Campaign` for its parts and its power.
- **The first system stays our own.** Every system after it is generated at random: any number of planets and moons, more than one living world, living moons and binary stars, each with names of its own. Going through to a new system also goes on the cheats page.
- **Where it meets the code:**
  - **Generation:** `SolarSystemDescriptor.Generate` lays out the real system. Other systems need a generator of their own.
  - **One star at the centre:** the code assumes body 0 is the only star. `Climate`, the lighting in `SolarSystem` and the map table all count on it. A binary needs two stars circling a shared centre, two lights and heat from both.
  - **Missions name their worlds:** `Campaign.World` finds a world by its real name. Missions in a generated system would be dealt out to its worlds instead.

## Self-replicating robots

There are no other people to find. What grows is a colony of self-replicating robots: fed ore, ice and salvage, they mine, build and make more of themselves.

- **Three resources:**
  - ore, from blasting rock (craters become mines);
  - ice, from the poles and icy moons;
  - salvage, from wreckage.
- **What they build on worlds taken back:**
  - a pad and depot;
  - a mine or ice drill;
  - a defence turret;
  - a radar mast that shows more of the map;
  - a workshop where the suit and the ship are upgraded;
  - more robots.
- **The loop:** the presence attacks what is built, and robots lost have to be made again.
- **The win:** the colony grows big enough to power the gate, and to build the engines that move a moon onto the last base.
- **Where it fits:** the colony's numbers would be a simulation of their own in the core, with tests, kept in the save; the robots and buildings models of their own.

## Platforms

The targets after macOS are iPhone (18 Pro) and iPad, and the Valve Index through SteamVR on a Windows PC.

- **iPhone and iPad (iOS and iPadOS):**
  - **Controls:** touch input that fills `Controls`, as keyboard, mouse and gamepad do now.
  - **Detail:** the Mobile tier in `LodSettings.cs` exists but has not been tried on a device.
  - **Language models:** no command-line tools can run there. The oracle reaches a model by the player's own key or OpenAI's sign-in, which opens its page through `Assets/Plugins/iOS/InfinigameBrowser.m`, never yet built. Keys belong in the Keychain, not a file.
  - **Speech:** Apple's speech synthesiser in place of the `say` command.
- **SteamVR (Valve Index on Windows):**
  - **The build:** a Windows build script.
  - **Controls:** OpenXR, with tracked controllers filling `Controls`.
  - **Detail:** the VR tier in `LodSettings.cs`.
  - **Interface:** the HUD set in the world, as a panel or the cockpit's own screens, not over the view.
  - **Comfort:** a seated cockpit and comfortable movement on foot.
  - **Language models:** the makers' tools exist on Windows, though `Oracle` looks for them only where macOS installs them.
  - **Speech:** Windows' own speech for the hermit.

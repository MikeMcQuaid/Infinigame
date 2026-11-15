# Missions

This table is the campaign source. Edit names, objectives, briefings, guard counts and rewards here, then run `script/play` or enter Play mode in Unity. The editor packages it into the game. Tests read this same file.

Keep the stage numbers 0–14 and their order: save files store these numbers. `World` uses a first-system world name; generated systems map the later missions onto their own worlds. `Guard` uses `FoeKind` names and `Rewards` uses `Gear` names. Stages 0–5 stay on the home world. Two guards watch the interior as sentries; any remaining guards use the authored patrol kind. Rewards are comma-separated. Do not put a vertical bar inside a cell.

The first two journeys are at most 300 m each. After recovering thrusters, the next two are at most 800 m each. Recovering the ship removes the mission-distance limit. Terrain searches include space for the arrival and pickup positions.

| Stage | Name | World | Guard | Count | Rewards | Objective | Briefing |
|---|---|---|---|---|---|---|---|
| 0 | Buried | Earth | Drone | 0 |  | CLICK TO DIG YOURSELF OUT | Weight above you. No memory, only the sound of a machine calling through the soil. |
| 1 | The recovery machine | Earth | Drone | 0 | Suit | A ROBOT IS CALLING  ·  FIND ITS WORKSHOP | I am a Foundry recovery robot. Our machines protect what still lives; the presence consumes it. I found you after the crash. Take this armour, then silence the nearby mind so we can reclaim its workshop. Your recovery record can wait. |
| 2 | The outpost | Earth | Sentry | 4 | Thrusters | SILENCE THE OUTPOST MIND | A controlling mind lives inside that outpost. Its shield will stop anything fired from outside. Go through the door, break it or find the terminal. Its thruster assembly belongs on your suit. |
| 3 | The depot | Earth | Drone | 6 | Beams,Stealth | SILENCE THE DEPOT MIND | The depot holds gauntlet emitters and a cloak. Your suit has three slots and six units of power. Take a style to the workshop, not every piece at once. |
| 4 | The hangar | Earth | Drone | 8 | Capacitor,BackMissiles,Ship | TAKE THE HANGAR  ·  YOUR SHIP IS INSIDE | That ship was yours. I knew its call sign before I found the wreck. The mind in the hangar has its power cell and your shoulder missiles. |
| 5 | The gun tower | Earth | Raider | 3 | Orbiter | ENTER THE TOWER AND SILENCE ITS MIND | The orbital drive is inside the tower. The ship can carry you there, but its guns cannot cross the shield. Your suit can. |
| 6 | Luna | Luna | Raider | 4 | Shield | SILENCE THE MIND ON LUNA | The moon holds a shield for the ship. Your suit already survives vacuum and radiation, but its thrusters cannot carry you between worlds. |
| 7 | Mars | Mars | Raider | 4 | Missiles | SILENCE THE MIND ON MARS | They are not separate raiders. Every hull is a body of the same presence. They copy the weapons they capture. Take the seekers and expect them to learn. |
| 8 | Venus | Venus | Raider | 5 | Plating | SILENCE THE MIND ON VENUS | You can walk safely in this heat. The ship needs the plating kept inside this base. I recognise the design of your armour. I helped build it. |
| 9 | Mercury | Mercury | Raider | 5 | Lance | SILENCE THE MIND ON MERCURY | The lance burns continuously while you hold the trigger. Do not mistake a quiet doorway for a safe one. Their bodies listen as well as watch. |
| 10 | Jupiter | Ganymede | Raider | 6 | Mender | SILENCE THE MIND ON GANYMEDE | This mind keeps a repair system for the ship. The robots can copy it at a workshop. Feed them ore, ice and salvage and they will build more of themselves. |
| 11 | Saturn | Titan | Raider | 6 | Rail,Armour | SILENCE THE MIND ON TITAN | The rail gauntlet pierces one body and the next behind it. Its long recharge leaves you open. Heavy plate draws from the same reserve as your beams and thrusters. |
| 12 | Uranus | Titania | Raider | 7 | Bulkheads | SILENCE THE MIND ON TITANIA | Bulkheads for the ship. The presence keeps spreading while we work. Defend the colonies: their robots will have to build something much larger than either of us. |
| 13 | Neptune | Triton | Raider | 8 | Nukes | SILENCE THE MIND ON TRITON | These torpedoes break ships and ground. They still cannot cross a base shield. The final mind lies too deep for even these. We will need a moon. |
| 14 | The citadel |  | Base | 1 | GateParts | ENTER THE CITADEL BY SHIP  ·  DESTROY THE SHIELD CORE  ·  ESCAPE | Fly into the citadel through its open tunnel, follow the turns and destroy the shield core. Then get out. I helped create you to end this presence. The radiation is its prison, and the gate we are repairing could release it. |

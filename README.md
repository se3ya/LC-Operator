<div style="display: flex; gap: 10px; flex-wrap: wrap; margin-bottom: 16px;">
  <img src="https://img.shields.io/codefactor/grade/github/se3ya/LC-Operator?style=flat&logo=codefactor&logoColor=white&color=83E6FB&cacheSeconds=1200" alt="CodeFactor Grade">
  <img src="https://img.shields.io/badge/dynamic/json?url=https%3A%2F%2Fthunderstore.io%2Fapi%2Fv1%2Fpackage-metrics%2Fseechela%2FOperator%2F&query=%24.downloads&label=downloads&style=flat&logo=thunderstore&logoColor=white&color=83E6FB&cacheSeconds=1200" alt="Thunderstore Downloads">
  <img src="https://img.shields.io/github/v/release/se3ya/LC-Operator?style=flat&logo=github&logoColor=white&color=83E6FB&cacheSeconds=1200" alt="GitHub Release Version">
  <a href="https://discord.gg/Z9rUwTtUpx"><img src="https://img.shields.io/badge/dynamic/json?url=https%3A%2F%2Fdiscordapp.com%2Fapi%2Finvites%2FZ9rUwTtUpx%3Fwith_counts%3Dtrue&query=approximate_member_count&style=flat&logo=discord&logoColor=white&color=83E6FB&cacheSeconds=1200&label=Discord" alt="Discord"></a>
  </div>
<br><br>

---
# Operator
### **Camcorder to record, rewatch and sell cassettes.**
---

## Features

- Buyable Camcorder in the terminal store for 260 credits
- Records `.mp4` files with game audio and your microphone
- Zoom, night vision, front/back camera flip, autofocus and fold out screen
- Raindrops on feed when it rains, lens cracks when you take damage or drop the camera
- Eject cassettes and sell them at Company, every enemy you record pays its own bounty
- Cassette colour shows what it is worth, from plain up to gold and holo
- Watch cassettes on the terminal or ship TV
- Cassettes and their recordings stay with your save
- Compatible with **GeneralImprovements**, **OpenBodyCams**, **CullFactory**, **Lategame Upgrades**, **LethalSponge**, **Mirage**, **BetterScanVision**, **JLL**, **SoundAPI**, **darmuhsTerminalStuff**, **SelfSortingStorage** and **LCVR**

---

## Controls

- **Left Click** - start / stop recording
- **R / Q** - zoom in / out
- **F** - flip camera front / back
- **T** - toggle night vision
- **C / V** - pull focus near / far *[ Manual focus mode ]*
- **X** - eject the cassette

---

## Terminal commands

- **`cassettes`** - list your cassettes and their IDs
- **`play <id>`** - play cassette on the terminal
- **`stop`** - stop playback
- **`pause`** / **`resume`** - pause or resume playback
- **`slow` / `normal` / `fast` / `turbo`** - change playback speed
- **`volume <0-100>`** - set playback volume
- **`transfer`** - move what is playing onto the big ship monitor on the right
- **`save <id>`** - keep cassette in your library and copy it to the Recordings/ folder
- **`delete <id>`** - remove saved cassette
- **`ohelp`** - show full command list

---

## Configuration

### **1. General**

- **Store Cost** - Camcorder price in store. *[ host ]*
- **Terminal Help Keyword** - Word to type in terminal to list every command of this mod.

### **2. Camera**

- **Min Fov** - How far camera can zoom in.
- **Max Fov** - How far camera can zoom out.
- **Zoom Speed** - Zooming speed.
- **Idle Battery Drain** - Battery drain while Camcorder is on but not recording.
- **Bob Intensity** - How much camera sways as player with camera walks.
- **Screen Emission Intensity** - How brightly camera screen emits.

### **3. Feed**

- **Render Resolution** - Resolution camera renders its own screen at.
- **Filter Mode** - How camera image is sampled onto screen. 'Point' is pixelated. 'Bilinear' is smooth.
- **Render Fps** - How many times per second camera screen rerenders, lower saves CPU. Recordings are unaffected.
- **Render Quality** - 'Quality' graphics like in cyberpunk. 'Performance' renders the feed without shadows or volumetric fog and with lower detail models. 'Quality' keeps all of that.
- **Weather Perspective** - Show weather from where camera is instead of where players are. Turn this off if modded weather looks wrong on screen.
- **Overlay** - Camcorder style screen display. 'Off' is clear screen. 'Minimal' shows REC and battery. 'Classic' adds timecode and frame. 'Vhs' is retro look. 'Pro' is full Handycam style.
- **Focus Mode** - 'Off' keeps everything always sharp. 'Auto' blurs and refocuses on its own. 'Manul' lets you pull focus yourself with Focus Near/Far keys using reticle on screen to see when youre sharp.
- **Exposure** - Simulates camcorders exposure catching up when lighting changes suddenly. 'Casual' is gentle lag that never lowers visibility. 'Real' does it properly and can briefly wash out or go dark.
- **Occlusion Style** - How feed reacts when camera is pressed against objects so you cannot see through walls. 'PlainBlackFade' just dims to black. 'VignetteClosing' closes edges inward and blurs as it darkens. 'VignetteSmear' adds directional smear at close range.

### **4. Effects**

- **Effect Strength** - Master value over every effect below.
- **Fear Response** - How hard camera reacts to held camera mans fear.
- **Vignette** - How dark corners of frame darken.
- **Barrel Distortion** - How much lens distorts image outward at edges.
- **Chromatic Aberration** - Colour fringing that grows toward edges of frame where lens fails to focus red and blue on same spot.
- **Focus Breathing** - Slight zoom that happens as lens racks focus.
- **Rain Intensity** - How heavily rain beads on lens while outside in rainy or stormy weather.
- **Grain** - Tape grain over the whole image.
- **Scanlines** - Darkened alternating rows, look of an interlaced tape.
- **Lens Dust** - Dust and smears on front element.
- **Veiling Glare** - Stray light scattering off lens elements into haze around bright sources.
- **Tracking Glitch** - Tape tracking errors that tear across frame when fear jumps suddenly.
- **Cold Desaturation** - Colour draining toward cold grey as fear rises.
- **Exposure Pump** - Exposure hunting up and down when player is frightened.
- **White Balance Drift** - White balance sliding warmer or cooler with fear.
- **Crack On Lens** - Camera lens cracks when you take damage or drop camera from height.
- **Crack Damage Threshold** - How much health you must lose to add crack stage.
- **Crack Max Stages** - Max lens crack stages.
- **Lens Repair** - When cracked camera lens is repaired. 'EachOrbit' repairs every time ship leaves moon and returns to orbit. 'EachQuota' repairs only when new profit quota begins. 'Never' leaves cracks permanently.

### **5. Recording**

- **Enabled** - Write recordings to output directory while camera is powered on. Turning this off leaves camera working as viewfinder.
- **Resolution** - Resolution of recorded MP4.
- **Fps** - Framerate of recorded MP4. Higher increases the size.
- **Crf** - Recording quality. Lower is better quality and bigger file. 16 is near lossless, 32 is visibly blocky.
- **Max Minutes** - Recording stops on its own after this many minutes of filming.
- **Max Queued Video Frames** - INTENDED FOR TESTING ONLY. Leave this at 96 unless you are doing stuff. Maximum video frames buffered between capture pump and ffmpeg. ffmpeg takes few seconds to start reading and anything captured past this buffer is replaced by repeated frame, so lowering it brings back stutter at start of every recording. Raising it trades RAM for headroom on slower machines.
- **Death Behavior** - What camera does when operator dies while filming. 'KeepFilming' keeps recording with static hiss. 'DeadManRoll' rolls static instead.
- **Keep Unsaved Per Lobby** - Keep finished recordings per lobby under Recordings/Lobbies/<save file>/ and only move one into main Recordings folder when you save that cassette. Cassette that no longer exists is cleaned up automatically. Turn this off to write every finished recording straight to main Recordings folder and keep it.
- **Output Directory Override** - Path for output folder. Leave empty to use %USERPROFILE%\AppData\LocalLow\ZeekerssRBLX\Lethal Company\Recordings\.
- **Encoder** - Auto tries NVIDIA, Intel and AMD, then the CPU (x264). Cpu always works.
- **Ffmpeg Exe Path** - Path to an existing ffmpeg.exe. Leave empty to auto download and cache pinned build under BepInEx/cache/ffmpeg/.

### **6. Audio**

- **Record Audio** - Capture game audio onto recording. Turning this off saves disk space and makes MP4 silent hill.
- **Audio Bitrate Kbps** - AAC audio bitrate. 32 and below sound compressed
- **Mic Character** - Run recording through camcorder microphone. It band limits sound, adds tape hiss and rides level so a quiet room swells up and a loud noise ducks everything for moment.
- **Mic Environment Effects** - Run your recorded microphone through games real environmental voice filters so it matches what other players hear.
- **Mic Reverb Amount** - How much room players own recorded microphone picks up so your own voice sits in world instead of on top of it. Only affects players own mic.
- **Mic Volume** - Volume of your own voice on recording.
- **Echo Strength** - How much room recorded voice is heard in.
- **Voice Range** - Meters at which recorded voices fade out.
- **Death Static Fade Seconds** - How long static takes to fade out after player dies with camera and camera keeps recording.

### **7. Sharing**

- **Broadcast Recordings To Clients** - After recording finishes send MP4 to everyone else so they can watch it. They get saved under ZeekerssRBLX/Lethal Company/Recordings/Received/. This is read on whoever did recording so host cannot turn it off for everyone. [Client side]
- **Keep Received Clips** - Keep other players recordings that were sent to you, under Recordings/Received/ so you can rewatch them anytime. If false, each is marked for deletion once no longer needed. Use 'save <id>' in terminal to keep permanently.
- **Terminal Co-Watch** - When anyone plays cassette in terminal/TV, mirror it to every players terminal so whole lobby sees and hears same recording.
- **Transfer Audio From Speaker** - Play transferred recording through ship speaker. If false, sound comes from monitor itself.
- **Television Cassette Watching** - Add cassette deck on top of ship Television. Insert cassette to play its recording on TV screen for whole lobby.
- **Television Screen Light Intensity** - How hard playing recording drives Television light.
- **Monitor Screen Light Intensity** - How hard transferred recording drives ship monitor light.

### **8. Economy**

- **Blank Cassettes Per Orbit** - Blank cassettes restocked each time the ship reaches orbit. Counts for the whole ship when 'BlanksPerShip' is true, otherwise each camera gets this many.
- **Blanks Per Ship** - Every camera takes from one shared stock of 'BlankCassettesPerOrbit' blanks, so second camera does not mean more tapes.
- **Flat Base Value** - Scrap value added to every cassette regardless of what is on it.
- **Dead Body Penalty** - Percent taken off the earned part of recording for each dead crewmate filmed long enough to count. Bodies already in the ship or being carried don't count.
- **Min Seconds For Bounty** - How long an individual enemy or dead body must spend on camera before any value is added for it.
- **Worthless Moons Forced** - Extra moons whose cassettes is worth nothing, separated by commas, i.e. "Assurance, Titan". Cassettes from the Company is already worth nothing.
- **Worthless Moons Excluded** - Moons to keep paying for even though they look like the Company moons, separated by commas, i.e. "Assurance, Titan".
- **Worthless Orbit Footage** - Cassettes shot in orbit is worth nothing.

---

## FAQ

### **Q: Where do my recordings go?**

`%USERPROFILE%\AppData\LocalLow\ZeekerssRBLX\Lethal Company\Recordings`. Set `OutputDirectoryOverride` if you want them somewhere else.

### **Q: Do I need to install ffmpeg?**

No, Operator downloads a pinned build into `BepInEx/cache/ffmpeg/` on first use. Set `FfmpegExePath` to use your own. If the download fails, an `ffmpeg.exe` on your `PATH` is used instead.

### **Q: Does recording work on my graphics card?**

- **NVIDIA** - GTX 750 and newer, driver **551.76** or newer
- **AMD** - Radeon RX 5000 and newer, Ryzen 5000 and newer with built-in graphics. RX 400, 500 and Vega should work too, *not tested*. Files come out about as big as on NVIDIA at the same `Crf`, with a slightly softer picture
- **Intel** - 6th gen Core and newer and Arc, with the built-in graphics enabled in the BIOS. *Not tested on real hardware yet*
- **CPU** - NVIDIA drivers older than 551.76, GTX 600 and 700, GT 1030, GeForce MX150 to MX450, RX 6300, 6400 and 6500 XT, Intel before 6th gen. About 6% of one CPU core at the default 426x240 and 24 fps, about 25% at `High_860x520`

### **Q: My recordings are too big.**

Lower `Resolution`, raise `Crf`, or drop `Fps`. Turning `RecordAudio` off saves a little more.

---

## Planned

- Enemy
- Model optimization
- Stuff

---

## Credits

- Developed by **[seeya](https://thunderstore.io/c/lethal-company/p/seechela/)**
- Models by Nebuling

---

## License

Distributed under the GPL v3 License.

---

### 💖 Support

If you enjoy my work, consider [supporting](https://www.buymeacoffee.com/see_ya) me. Donations are optional but greatly appreciated.

---

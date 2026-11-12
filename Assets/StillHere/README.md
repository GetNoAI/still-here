# Still Here (Unity)

Open `Assets/StillHere/StillHere.unity` and press Play. Wear headphones. The screen stays black: a faint
outline of headphones means "press anything to begin". Nothing else is explained in the game.

## Controls (never shown on screen)

| | Walk | Turn / tilt head | Reach out and touch |
|---|---|---|---|
| Keyboard + mouse | W A S D (or up/down arrows) | Mouse, or left/right arrows | Left click, E, Space, Enter |
| Gamepad | Left stick | Right stick | Any face button, RB or RT |
| Phone / tablet | Left half of the screen: put a thumb down and drag (an invisible stick centred where you first touch) | Right half of the screen: drag | Quick tap anywhere |

Phones vibrate, and gamepads rumble, when you bump into something, stumble, or a cat brushes past.

## How it is put together

- `Scripts/Game*.cs`: the whole game. It builds the camera, audio, kitchen and cats at runtime, so the scene holds one object.
- `Scripts/World.cs`: the invisible kitchen (boxes and cylinders) used for collision, reaching and outlines.
- `Scripts/TouchParticles.cs` + `Shaders/TouchParticle.shader`: the only thing drawn. Each mote wanders around a fixed anchor.
- `Scripts/AudioDirector.cs`: all sound. Direction is rendered with Steam Audio's HRTF (Project Settings > Audio > Spatializer Plugin);
  distance roll-off, air absorption and room reverb are added on top. If the spatializer is unset it falls back to panning plus filtering.
- `Assets/Plugins/SteamAudio`: Valve's Steam Audio 4.8.1 Unity integration (HRTF spatializer).
- `Scripts/InputHub.cs`: keyboard, mouse, gamepad and touch folded into one input frame.
- `Resources/CatPoses.asset`: single frames of the Realistic Cat pack's animations baked to point clouds
  (menu: StillHere > Bake Cat Poses). The game does not need the cat model at runtime.
- `Resources/VO`, `Resources/SFX`: voice lines and sound effects generated with ElevenLabs.
  `Reference/audio_manifest.tsv` (project root) lists every clip id, its generation id and its prompt.

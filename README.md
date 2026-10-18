# DuckyMicrophone

A free Windows audio mixer that combines apps and microphones into one virtual microphone. Mix a browser, soundboard, music player, and your voice—with separate volume and mute controls for each source.

## Download and run

1. Open this repository's **Releases** section and download **DuckyMicrophone-1.0.0-win-x64.zip** (or the latest versioned Windows ZIP).
2. Extract the entire ZIP into a folder you can write to.
3. Open **DuckyMicrophone.exe**. Keep the bundled files together.

You do not need to install .NET. The ZIP includes the runtime. GitHub's **Code → Download ZIP** downloads the source, not the ready-to-run app.

### Requirements

- Windows 11, 64-bit Intel/AMD (or Windows build 20348 or later).
- A virtual audio cable to expose the mix as a microphone. Install [VB-CABLE from its official site](https://vb-audio.com/Cable/) or configure a compatible virtual mixer. Follow the vendor's instructions and restart Windows if requested.
- Virtual audio drivers are installed separately and are not bundled with DuckyMicrophone. Their terms and availability are set by their vendors.

## Set up your mix

1. Set **Mix output** to **CABLE Input**.
2. Choose **App audio**, select an app, and click **Add source**. Repeat for more apps.
3. Choose **Microphone** and add your physical microphone to include your voice.
4. In your game, voice chat, or recorder, select **CABLE Output** as the microphone.
5. Click **Start mixing**. Use **Stop mixing** or close the app to stop.

The names are intentional: Ducky sends to **CABLE Input**; the receiving app listens to **CABLE Output**.

## Features

- Direct app audio capture, without selecting a speaker as the source.
- One source or multiple sources, with no fixed source-count limit; capacity depends on your computer.
- Add and remove sources while mixing.
- Individual volume, mute, and level meters; master volume and mute.
- 100% preserves the captured level; up to 400% boosts quiet sources.
- Output clipping protection and buffered capture for uneven audio delivery.
- Optional **Robot duck** effect on the combined mix.

## Troubleshooting

**Quiet audio:** Leave Master at 100% and gradually raise only the quiet source. Also check the website/player volume, Windows app volume, cable levels, and your game's microphone gain. If the mix distorts, lower the source levels.

**Music cuts out in game chat:** The game's noise suppression or voice-activation threshold may filter music. Check those settings if available. Ducky does not modify game or Windows audio settings.

**App missing or silent:** Play a sound in it, then click **Refresh apps**. If it restarted, remove the old source and add it again. Browsers include all audible tabs in the selected process tree, not individual tabs. Protected media and apps bypassing normal Windows audio may not be capturable.

**Echo:** Use headphones. Do not add the receiving voice-chat app as an audio source. Virtual recording inputs are excluded to help prevent cable feedback.

**No microphone in another app:** Choose the cable's recording side there. Selecting ordinary speakers as Ducky's mix output only plays audio through those speakers.

## Privacy

DuckyMicrophone processes audio locally. It does not record captured audio to disk, upload it, or include analytics. Normal use does not require an internet connection after setup. The app only captures selected sources while mixing is running.

## For developers

See [BUILDING.md](docs/BUILDING.md) for build/test commands and [RELEASING.md](docs/RELEASING.md) for GitHub upload instructions.

- `Source/` — editable application source and regression checks.
- `scripts/` — repeatable build, test, and ZIP packaging.
- `.github/workflows/` — Windows build verification and downloadable build artifacts.
- `licenses/` — third-party notices only.

The app is free to download. No license is added for the project's own code. Dependencies retain their own licenses; see [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

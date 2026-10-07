# Demo Video Assets

This folder contains everything needed to produce the 60–90 second Hercules demo.

## Files

- `demo-video-cover.svg` — thumbnail / cover image for README and docs.
- `demo-script.md` — exact narration and screen actions.
- `record-demo.ps1` — PowerShell script to capture the screen (Windows Graphics Capture or ffmpeg fallback).
- `synthesize-speech.ps1` — generates narration MP3 via Microsoft Edge TTS (`edge-tts`).
- `README.md` — this file.

## Quick workflow

1. **Record the screen**

   ```powershell
   .\assets\demo\record-demo.ps1 -OutputPath .\assets\demo\demo-raw.mp4 -Duration 90
   ```

2. **Generate the voice-over**

   ```powershell
   .\assets\demo\synthesize-speech.ps1 -Script .\assets\demo\demo-script.md -Output .\assets\demo\demo-voice.mp3
   ```

3. **Combine video + audio + captions** (requires ffmpeg)

   ```bash
   ffmpeg -i assets/demo/demo-raw.mp4 -i assets/demo/demo-voice.mp3 -c:v copy -c:a aac -shortest assets/demo/hercules-demo.mp4
   ```

4. **Make a GIF for README** (requires ffmpeg)

   ```bash
   ffmpeg -i assets/demo/hercules-demo.mp4 -vf "fps=15,scale=720:-1:flags=lanczos,split[s0][s1];[s0]palettegen=[s1]paletteuse=dither=bayer" assets/demo/demo.gif
   ```

5. Link the GIF in README:

   ```markdown
   ![Demo](assets/demo/demo.gif)
   ```

## If `edge-tts` is not installed

```bash
pip install edge-tts
```

Then re-run `synthesize-speech.ps1`.

## If ffmpeg is not installed

Install via winget:

```powershell
winget install Gyan.FFmpeg
```

Or use any screen recorder and video editor you prefer — the script and narration still apply.

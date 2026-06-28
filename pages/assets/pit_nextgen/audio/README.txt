PIT NEXT-GEN — AUDIO (optional sample drop-in)
==============================================
By default the game SYNTHESIZES all audio in-browser (Web Audio API): engine,
tire skid, crash, two-tone police siren, and win/lose jingles. No external audio
files are required, downloaded, or hosted — nothing to vet for safety, and the
console stays clean (no 404s).

OPTIONAL: to use your own recorded samples instead, do two things:
  1. Drop the audio file(s) in this folder (.ogg, .mp3, or .wav).
  2. List the filename in manifest.json next to the matching sound key.

manifest.json keys:
  engine   seamless looping engine tone (its playback rate tracks speed/RPM)
  skid     seamless looping tire screech/skid
  crash1   short crash/impact one-shot
  crash2   short crash/impact one-shot (a random one is chosen per impact)
  siren    looping two-tone police siren (kept quiet in-game)

Any key left "" uses the built-in synthesized sound. Example:
  { "engine": "engine_loop.ogg", "siren": "siren_wail.ogg" }

Use only assets you have the right to use (CC0 / public-domain / your own /
purchased). .ogg/.mp3/.wav are inert data decoded as audio samples.

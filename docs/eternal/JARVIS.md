# Jarvis in Core

Jarvis, the household's assistant (Aincrad's Home Assistant agent), now lives in Core too:

- **Chat** in Quick Access > Core > **Jarvis**, and on a full page (*Open full page*). Type with Steam's keyboard
  (swipe typing works there) or dictate into the field.
- **"What's on screen?"**: a screen-aware game helper. Core takes one screenshot of the game, sends it with your
  question to Claude, and shows the answer, spoiler-safe by default (a nudge first, **More** for the full answer).
- **Push-to-talk**: hold a controller combo and speak. Either the words are **typed** into the focused text field
  (dictation), or they go to Jarvis and the answer is **spoken** (talk to Jarvis).

Nothing is sent anywhere until you ask: a message, a combo held, a button pressed.

Settings: Core > Gaming Mode > **Jarvis** (stored in `%APPDATA%\GamingMode\config.json`, `gaming` section). The Home
Assistant token and the API keys are **not** in `config.json`: they are in `jarvis-secrets.json` next to it, each sealed
with DPAPI for your Windows user (`Shared/JarvisSecretStore.cs`). Health gains a **Jarvis** row.

## Who answers

| Backend | What it is | Memory and persona | House control | Screen helper |
| ------- | ---------- | ------------------ | ------------- | ------------- |
| **Home Assistant Jarvis** (default) | Home Assistant's conversation API (`POST /api/conversation/process`) with the agent `conversation.jarvis` and your long-lived access token | The same agent as the speakers and phones: Aincrad's instructions, `jarvis_recall` / `jarvis_remember`, model `claude-haiku-5-5` (set in Home Assistant) | Yes, whatever Aincrad exposes to the voice assistant | Not possible (see below); uses the Anthropic key if saved |
| **Direct Claude API** | `POST https://api.anthropic.com/v1/messages` with your own Anthropic API key | Jarvis's persona as the system prompt (copied from Aincrad's `assistant/home-agent-prompt.md` and `assistant/claude-account-persona.md`); the model setting defaults to `claude-haiku-5-5` | No | Yes |

- **Session history:** the last 8 exchanges, in memory only. Forgotten on **Clear**, after 30 quiet minutes, when
  Aincrad's sleep mode turns on (`aincrad/sleep_mode` over the Home Assistant MQTT bridge, batch 2) and when the PC
  sleeps. With Home Assistant the history is Home Assistant's own `conversation_id`; Core keeps the id and the text
  shown in Quick Access, nothing else.
- **Security actions are never available from Core.** Aincrad's rule is that locks, the garage and the alarm need a
  confirmation on the phone (`request_unlock`), and Home Assistant never exposes them to the voice agent. Core adds its
  own refusal before anything is sent (`JarvisGuard`): a message that mentions unlocking, a lock, the garage, the alarm
  or disarming is answered by Core ("That one needs your phone, sir.") and never reaches Home Assistant or Claude. It
  errs on the side of refusing; on the direct Claude backend, which controls nothing, game talk such as "how do I
  unlock the next level" passes.
- The agent id is a setting (Home Assistant names the Anthropic subentry "Jarvis" `conversation.jarvis`; inferred
  from Home Assistant's naming, checked by **Test connection**). Without it Home Assistant would use its default,
  built-in agent, not Claude.

### Why the screen helper always uses the Claude API

Home Assistant's conversation API takes **text only** (`text`, `conversation_id`, `language`, `agent_id`, `device_id`;
checked in Home Assistant's `conversation/http.py`). Home Assistant's newer AI Task (`ai_task.generate_data`) accepts
`attachments`, but only as **media-source references**: the screenshot would first have to be stored as a file on the
server, which Core never does (screenshots stay in the PC's memory). So the screen helper sends the image straight to
the Claude API with your Anthropic key. Without a key, the button is off and Quick Access says the helper needs one.

## The screen helper

1. Press **What's on screen?** in Quick Access (Quick Access closes first, so the game is what gets captured), or hold
   the screen combo (default **L4+L5**). A question typed in the field goes with it; otherwise "What's going on here,
   and what should I do next?".
2. Core captures **one frame** of the monitor the game is on with **Windows Graphics Capture** (falling back to a GDI
   copy of the screen), scales it to at most 1280 px on the long edge, encodes it as JPEG (quality 80) in memory and
   sends it with the question in a single request. The screenshot is cleared from memory as soon as the request is
   built; it is never written to disk.
3. **Spoiler-safe mode** (on by default): Claude answers with a `NUDGE:` line and a `FULL:` answer in one call. Quick
   Access and the toast show the nudge; **More** shows the full answer without a second upload. An answer without the
   expected form shows a neutral "I have an answer ready" instead, so a malformed answer cannot spoil. Off: the full
   answer at once.
4. **Every screenshot is announced**: a small crimson "Screenshot taken for Jarvis" mark appears at the top right for
   two seconds, also while Stream mode sends the picture to someone else (the helper still works then).
5. Optional: *Read the screen helper aloud* speaks the answers.

**Anti-cheat safe.** Nothing touches the game's process. Windows Graphics Capture reads the picture from Windows'
compositor (DWM), the same API the Xbox Game Bar, OBS and Discord use; there is no injection, no hook, no overlay inside
the game, no read of game memory. The "screenshot taken" mark is Core's own window that takes no focus or input. On
Windows 10/11 the capture may draw a thin yellow border around the screen for the fraction of a second it runs
(Windows' own privacy cue; Core's SDK level cannot turn it off). Full-screen exclusive (DirectX) games can come out
black with either method; borderless and modern flip-model games capture normally (checklist).

Ideas from Quest Compendium (BSD-3-Clause, as of 2026-11-24) and Decky AI; **no code was borrowed**.

## Push-to-talk

| Combo (hold) | Default | What happens on release |
| ------------ | ------- | ----------------------- |
| Dictation | **L5+R5** (both lower grips) | Whisper transcribes; the text is typed into the focused Steam text field |
| Talk to Jarvis | **L4+R4** (both upper grips) | Whisper transcribes; Jarvis answers in Quick Access and a toast, and speaks it |
| What's on screen? | **L4+L5** (both left grips) | (pressed, not held) the screen helper above |

- **Combos** are two to four buttons held together; Steam's own buttons (Steam, Quick Access) are not allowed, and a
  combo that contains another is turned off rather than firing both. The defaults use the 2026 Steam Controller's
  four grip buttons, which Steam Input leaves unbound unless you assign them. Change them in the desktop card
  (names: `A B X Y L1 R1 L2 R2 L3 R3 L4 R4 L5 R5 SELECT START UP DOWN LEFT RIGHT`, also `LB RB LT RT VIEW MENU`); an
  empty box turns one off.
- **How Core sees the combo:** inside Steam, through `SteamClient.Input.RegisterForControllerStateChanges`
  (`ulButtons`, and `ulUpperButtons` for L4/R4). The agent still never reads a controller (the rule in
  `DashboardShortcutService.cs`). The bit positions come from Decky's typings and the Steam Deck; that the 2026 Steam
  Controller reports its grips the same way is **inferred** (checklist). Quick Access also has *Dictate* and *Talk to
  Jarvis* buttons (press to start, press again to stop); dictation from Quick Access goes into the chat field.
- **Microphone:** Core Voice's choice. The **calls mic** chosen in Quick Access > Voice (the DJI Mic 3 receiver) when it
  is connected, otherwise Windows' calls default, then the games-and-apps default.
- **Never while muted.** With *Mute everything*, nothing records while Core's mute is on or while any default
  microphone is muted. With *Mute calls only*, the muted calls microphone is skipped and the games-and-apps microphone
  records (that is what this scope keeps live for apps); if both are the same device, nothing records. Core checks
  again every 0.4 s while recording: muting mid-sentence (or the microphone changing) discards the recording at once.
- **Xbox party:** recording works while a party is open in the Game Bar, but Windows shares the microphone, so the
  party hears what you say. A toast says so.
- **Crimson indicator:** "Jarvis is listening" with a crimson dot at the top right for as long as Core records (over
  Big Picture and over games; no focus taken). Recording stops by itself after 30 seconds.
- **Typing:** Steam's own keyboard path first (`SteamClient.Input.ControllerKeyboardSendText`, what Steam's on-screen
  keyboard uses); if Steam has no such call, the agent types with `SendInput` (Unicode keystrokes). `SendInput` is the
  one synthetic-input path and is **refused while anti-cheat safe mode is engaged** (the text then stays in Quick
  Access). Dictation types only into what has the keyboard focus; it never presses Enter.
- Too short (under 0.3 s) or silent: nothing is sent.

Ideas from WhisPTT (BSD-3-Clause), Decktation and SpeechToDeck; no code taken.

### Speech to text

1. **Aincrad's Whisper over Wyoming** (`serverv3:10300`, local and free). Core speaks the Wyoming protocol itself
   (`Shared/JarvisWyoming.cs`: `transcribe`, `audio-start`, `audio-chunk`..., `audio-stop`, then `transcript`).
   **Note:** Aincrad's `docker-compose.yml` publishes Whisper and Piper on the server's loopback only
   (`127.0.0.1:10300`), so from the PC this port is refused today; Core then skips it for 10 minutes and uses:
2. **The same Whisper through Home Assistant** (`POST /api/stt/stt.faster_whisper` with the token, raw 16 kHz mono
   16-bit PCM described by `X-Speech-Content`). This works with Aincrad as it is.
3. **OpenAI-compatible cloud Whisper**, only if you save its key **and** turn on *Cloud Whisper if the server doesn't
   answer* (`POST <url>/audio/transcriptions`, model `whisper-1`).

There is no Anthropic route: the Claude API does not take audio. To use the direct Wyoming route, publish the port on
the LAN in Aincrad (for example `"10300:10300"`, or bound to the server's LAN or Tailscale address). Wyoming has no
authentication, so keep it off the internet; Home Assistant's route needs no change.

### Speaking the answer

*Spoken answers* on (default): talk-to-Jarvis answers (and Quick Access's *Speak*) play through Windows' default
output with Aincrad's Piper voice `en_GB-alan`, through Home Assistant (`POST /api/tts_get_url` with `tts.piper`, then the
audio from the returned `/api/tts_proxy/...` path on Core's own Home Assistant address). If that fails: Piper over
Wyoming on port 10200 (same loopback note as above), then Windows' own British English voice (Microsoft George,
Hazel, Susan, Ryan or Sonia, whichever is installed; otherwise Windows' default voice). Playback never pauses or stops
the game.

## Privacy

- Nothing is sent until you ask. No microphone is opened without a held combo or a pressed button; no screenshot is
  taken without the button or the combo.
- **Never logged:** prompts, answers, transcripts, screenshots, the token and the keys. `agent.log` gets one line per
  action with its outcome only, for example `Jarvis: Quick Access message answered by home-assistant (ok).` or
  `Jarvis: screen helper (graphics-capture, 142 KB) answered (ok); the screenshot is discarded.`
- **In memory only:** audio and screenshots, cleared right after use; the session's text until it is cleared.
- **Secrets:** sealed with DPAPI for your Windows user, readable only by the agent and the desktop app on this PC; the
  token goes only in the `Authorization` header to your Home Assistant, the Anthropic key only in the `x-api-key`
  header to `api.anthropic.com`.
- What leaves the PC: to Home Assistant (your server), the text and audio you send; to Anthropic (direct backend and
  screen helper), the text and the screenshot; to OpenAI only with cloud Whisper on, the audio.

## Costs

- **Both backends bill per use.** Home Assistant's Jarvis agent runs on Aincrad's Anthropic API key (Aincrad's setup
  estimates about 0.2 cents per command with `claude-haiku-5-5`; *Prefer handling commands locally* answers simple
  commands for free). The direct backend and the screen helper bill **your** Anthropic key. Set a monthly spend limit in
  the Anthropic Console.
- A screenshot costs extra input tokens: Claude counts an image as `ceil(width/28) x ceil(height/28)` visual tokens,
  so a 1280x720 screenshot is about 1,200 tokens on top of the text. Prices: https://claude.com/pricing.
- **Test connection** costs nothing: it reads Home Assistant's `/api/` and the agent's state, and the Claude API's model
  entry (`GET /v1/models/<model>`); no conversation is started. Speech on the home server (Whisper, Piper) is free.

## Setup

1. Home Assistant: your profile > **Security** > **Long-lived access tokens** > *Create token* (name it "Core").
2. Core on the desktop > Gaming Mode > **Jarvis**: leave *Home Assistant's Jarvis*, check the address
   (`http://serverv3:8123`; the Tailscale HTTPS address works too) and the agent (`conversation.jarvis`), paste the
   token, and for the screen helper your Anthropic API key. **Save token and keys**, then **Test connection**.
3. Optional: change the combos, spoiler-safe mode, spoken answers.

`config.json` keys (`gaming`): `jarvisEnabled`, `jarvisBackend` (`home-assistant` or `claude`), `jarvisHaUrl`,
`jarvisHaAgent`, `jarvisModel`, `jarvisDictateCombo`, `jarvisTalkCombo`, `jarvisScreenCombo`, `jarvisSpoilerSafe`,
`jarvisVoice`, `jarvisSpeakScreen`, `jarvisCloudWhisper`; config-only: `jarvisWyomingHost` (`serverv3`),
`jarvisWyomingPort` (10300), `jarvisSttEntity` (`stt.faster_whisper`), `jarvisTtsEntity` (`tts.piper`),
`jarvisOpenAiUrl` (`https://api.openai.com/v1`). The entity ids are the ones the Wyoming integration usually creates
(inferred; change them if yours differ).

## Health

Row **Jarvis** (information only, never an alert): *Off*, *Not set up* (no token for Home Assistant, or no key for
the direct backend), *Ready*, *Connected*, *Home Assistant unreachable*, *Claude API unreachable*, *Token refused*,
*Key refused*, *Agent not found*; the details say which way it answers and whether the screen helper has its key. The
status comes from the last use and a free check every 10 minutes.

## How it works

- Pure rules (tested on Linux): `Shared/JarvisCore.cs` (settings, combos, history, the guard, prompts, request bodies
  and answer parsing, spoiler flow, speech routes, audio conversion), `Shared/JarvisWyoming.cs` (Wyoming framing and
  client), `Shared/JarvisClient.cs` (time-boxed HTTP calls, every failure an error code), `Shared/JarvisSecretStore.cs`,
  and `GamingModeAgent/GamingMode.Services/Jarvis.cs` (microphone choice, combo set, screenshot size, Health row).
- Windows half: `Jarvis.Windows.cs` (WASAPI shared-mode capture, Windows Graphics Capture with a GDI fallback,
  `MediaPlayer` playback and `SpeechSynthesizer`, `SendInput`, the indicator window). Wiring and endpoints:
  `MarkIIIHost.Jarvis.cs` (loopback only): `GET /jarvis/state`, `POST /jarvis/chat`, `/jarvis/clear`,
  `/jarvis/listen/start?mode=dictate|talk`, `/jarvis/listen/stop?steamTyping=`, `/jarvis/listen/cancel`,
  `/jarvis/screen`, `/jarvis/screen/more`, `/jarvis/speak`, `/jarvis/speak/stop`, `/jarvis/type`, `/jarvis/reload`.
- Plugin: `src/JarvisSection.tsx` (Quick Access section and the full page, route `/core-jarvis`), `src/jarvisRuntime.ts`
  (push-to-talk and screen flow), `src/jarvisCombos.ts` (combos), `src/jarvisReport.ts` (text in 12 languages);
  `main.py` `core_jarvis_*`. Desktop card: `Source/Playhub/MainWindow.Jarvis.cs`.
- Every network call has its own deadline (Home Assistant 25 s, Claude 30 s, screen 45 s, Whisper 12-20 s, voice 15 s,
  status checks 6-8 s) and fails soft with an error code shown in words in Quick Access.

## Tests

- `Source/CoreHealth.Tests` (`JarvisTests.cs`): settings cleaning, combo parsing, the history, the guard, request
  bodies, Home Assistant conversation / speech-to-text / text-to-speech / test against a fake HTTP server, the Claude
  API and cloud Whisper against a fake, the spoiler flow, speech routes and audio conversion, Wyoming framing and a fake
  Wyoming server on loopback (Whisper and Piper, closed port, a server that never answers), the microphone choice under
  both mute scopes, screenshot size and tokens, the Health row, the secret store.
- `Source/LocalizationResource.Tests`: the desktop card in all 12 languages. `Source/MarkIII.Tests`: the indicator text.
- `GamingModeDeckyPlugin/tests/coreJarvis.test.mjs`: Quick Access text in 12 languages, status and error lines, the
  Health row, combo parsing and the hold watcher against a fake Steam input, push-to-talk (Steam typing, the agent
  fallback, anti-cheat, dictation into the field, muted, talk, party, off) and the screen helper against a fake backend,
  the backend calls. `tests/optionalStartup.test.mjs` counts the new optional integration.

## Needs the PC (or the server)

Nothing here could be tried against the real thing on Linux: the agent and app builds (CI), WASAPI capture from the
DJI receiver, Windows Graphics Capture over Big Picture and over games (and the yellow border), `MediaPlayer` and the
en-GB voices, `SendInput` and `ControllerKeyboardSendText` into Steam's text fields and into games, the indicator over
full-screen games, the Steam Controller's grip bits through `RegisterForControllerStateChanges` (in Big Picture and
in game), Home Assistant's conversation, speech-to-text and text-to-speech endpoints with the real token, the agent id
`conversation.jarvis` and the entity ids `stt.faster_whisper` / `tts.piper`.

## On the TV: checklist

1. Desktop: Core > Gaming Mode > Jarvis: paste the token and the Anthropic key, **Save**, **Test connection**: "Connected
   to Home Assistant: the conversation.jarvis agent answers" and "Anthropic key works". If the agent is not found, copy
   its entity id from Home Assistant (Settings > Devices & services > Anthropic > Jarvis).
2. Quick Access > Core > **Jarvis**: the status line is green. Type "What's on my calendar tomorrow?" with Steam's
   keyboard (try a swipe): Jarvis answers in character. "Dim the living room": the light changes.
3. Type "unlock the front door": Core answers "That one needs your phone, sir." and Home Assistant's logbook shows
   nothing.
4. **Clear**: the conversation is gone. Turn sleep mode on in Home Assistant: the session clears (`agent.log`:
   *session cleared (Aincrad's sleep mode)*).
5. In Big Picture, hold **L5+R5**: the crimson "Jarvis is listening" pill appears; say a sentence into a Steam search
   field; release: the text appears in the field. Note whether the grips fired (if not: the combo bits differ on the
   2026 controller; try `L1+R1+SELECT`).
6. Hold **L4+R4**, ask "What time is it?", release: the answer appears and Piper's British voice speaks it.
7. Mute in Quick Access > Voice (*Mute everything*), hold a combo: "Microphone muted", no pill. Switch to *Mute calls
   only* with a second microphone: it records from that one. Start recording, then mute: the pill disappears at once.
8. In a game without anti-cheat: hold **L4+L5** (or Quick Access > *What's on screen?*): "Screenshot taken for
   Jarvis" flashes, a toast shows a nudge; Quick Access > Jarvis > **More** shows the full answer. Note whether the
   screenshot shows the game or black (full-screen exclusive), and whether a yellow border flashes.
9. During a stream to the Steam Frame, use the screen helper: the mark still shows; the stream keeps running.
10. In a game with kernel anti-cheat (Call of Duty): dictation into a chat field says typing is paused (when Steam's
    keyboard path is unavailable); the screen helper works; the game is never paused.
11. With an Xbox party open: hold the talk combo: the toast says the party hears you.
12. Health: the *Jarvis* row says *Connected*. Stop Home Assistant: within 10 minutes it says *Home Assistant
    unreachable*; Quick Access shows the same, nothing crashes.
13. `agent.log`: Jarvis lines carry outcomes only, no text, no token.

# AnchorFeeds

AnchorFeeds is a Windows WPF application that generates one MP3 per RSS or Atom article, displays feed images and article text, opens source articles in a browser, plays generated audio, and remembers played and starred articles.

## Run from source

Install the .NET 10 SDK and the `edge-tts` command, then run:

```powershell
dotnet run --project .\AnchorFeeds\AnchorFeeds.csproj
```

The app reads `settings.yaml` next to the executable. Relative output paths are resolved from that directory. Played status is stored separately under `%LOCALAPPDATA%\AiNewscast\library-state.json` to preserve existing library data.

The app keeps YAML as its user-facing settings format so feeds and paths remain editable and portable without registry migration. It creates the library-state directory and audio output directory as needed; the temporary output directory is created when feed retrieval starts. Configured directories are created when used rather than all being checked in a separate startup preflight.

## Publish a Windows executable

```powershell
dotnet publish .\AnchorFeeds\AnchorFeeds.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

The published executable still needs `settings.yaml` beside it. Audio generation also requires `edge-tts.exe` and FFmpeg on `PATH` (or `edge-tts.exe` in the current user's Python Scripts directory). FFmpeg encodes generated audio at the selected MP3 bitrate; Windows playback, RSS fetching, and the GUI are provided by the app.

## Playback settings

Use **Settings > Configure all settings...** in the app to edit `settings.yaml` options, including RSS/Atom feeds, generation limits, output paths, retention, playback behavior, article text visibility, MP3 bitrate, Scan mode countdown, and the Anchor(s) voice list. Settings are grouped into collapsible **General**, **Appearance**, **Feeds**, **Paths**, and **Audio** sections. **Appearance** offers **Light**, **Dark**, **Charcoal**, and **System Desktop Theme** themes, applied immediately after saving and persisted as `appearance_theme`. System Desktop Theme follows the Windows light/dark app preference, uses Windows system colors for controls, text, and selection, and refreshes when Windows system colors change. In **General**, **Retrieve latest feeds on application launch** is on by default; turn it off to skip automatic feed retrieval at startup. **Application Language / Locale** defaults to **System default**, which detects the Windows display language at startup; users can choose another language explicitly. Available languages are English, Czech, German, Spanish, French, Italian, Japanese, Korean, Polish, Brazilian Portuguese, Russian, Turkish, Simplified Chinese, and Traditional Chinese. A language change applies the next time the application starts. The language setting is stored as `application_language` in `settings.yaml`.

Each setting has a clickable **(?)** help button and a hover tooltip with definitions or examples. Voices are grouped by language and country, with the system's exact locale expanded initially; other groups can be expanded as needed. If no voices have been configured, the app defaults to Guy and Jenny for en-US, or one female and one male voice for the system locale (falling back to the system language). The voice catalog does not include popularity data, so this is a practical default rather than a measured popularity ranking. Select multiple voices with their checkboxes. Use **Sample** beside a voice to hear it before selecting. The sample button spins while the clip is being generated, then highlights while playing and returns to its normal state when finished or canceled. Newly generated articles rotate through the selected voices; if edge-tts reports that no audio was received, the app tries the other selected voices for that article before reporting a failure. Existing `anchor_1`, `anchor_2`, `anchor_1_voices`, and `anchor_2_voices` settings are still accepted and migrated to the single `anchor_voices` list when settings are next saved. **Default** uses Edge TTS's native 24 kHz, 48 kbps mono MP3 output without FFmpeg. The explicit 48 kbps choice uses the same native output; 96, 128, 160, 192, 256, and 320 kbps choices use FFmpeg to re-encode new files. Existing article audio is skipped rather than re-encoded. Playback speed is selected from the rabbit menu beside the player controls (0.7x through 1.3x); the selected speed is saved immediately and applied to current playback. The page-flip sound plays after an article's narration ends, before the next article starts, and is not included in the MP3 files. Changes are saved beside the executable and applied immediately.

Use **View > Show article text** to show or hide the body text used to synthesize the selected article's audio. Title narration uses a separate hidden audio player so it does not share the visible body's progress slider. The player keeps the article title at the top, image and text stacked in the available center area, and playback controls at the bottom. During body playback, the article text scrolls in proportion to audio progress. Click the Scan/Full icon beside the player buttons to switch modes; the chosen mode is remembered. Scan mode (on by default) plays the title audio, displays a countdown over the image, then advances to the next visible article regardless of Auto Play Next. Click the countdown overlay to play the article body instead; click the image when the countdown is not visible to open its source in a browser. Configure the countdown in Audio settings to 3, 5, 7, 10, 15, 20, 30, or 60 seconds (default 7). Full mode plays title audio followed by body audio; Auto Play Next determines whether playback continues after the body. A page-flip sound plays when moving to the next article after playback completes. Read status follows the configured mark-as-played rule in both modes. Opening the article in a browser skips auto-advance once when that article finishes. Played article titles are shown in regular-weight text in the title list. **View > Unplayed only** can be set to start enabled in Configure all settings and is remembered across launches. The divider between the article list and player can be dragged to adjust their relative widths. Articles can be starred from the list or player; starred articles remain visible when **Unplayed only** is active. Star state is saved with the local library state.

Each generated article is stored in its own folder inside the audio output directory. The `final_output` setting describes this layout as a folder template, for example `./audio/{feed}_{title}/`; that folder contains `title.mp3`, `body.mp3`, and the cached article image when one is available. Body narration uses the feed description/content without repeating the title. If title and body MP3 sizes are within 3%, body playback and its text are suppressed as likely duplicate narration. Existing flat MP3s are moved into the corresponding article folder when their feed entry is retrieved; saved played/starred state is retained.

The article-list divider position is saved internally as `article_list_width_ratio` in `settings.yaml` when dragged and restored the next time the app starts. It is intentionally not shown in the Configure all settings window.

The article list shows each article's feed publication date/time when available, or the audio file creation date/time otherwise. Use the circular-arrow **Update Feeds** icon and **Mark all read** button beside the Audio library heading. The sort icon opens alphabetical, newest-first, and oldest-first order options, plus **Group by Feed**. The funnel button opens **Unread only** and **Starred only** filters. These list sort, grouping, and filter selections are saved in `settings.yaml`. The library refreshes automatically after retrieving feeds. If an RSS server returns an HTTP error, that feed is skipped, the remaining feeds are still processed, and a warning lists the feed URL and error. For development or troubleshooting, **Settings > Rebuild Library...** reconstructs the library from matching MP3 files, resets article details, played status, and stars, and leaves audio files untouched.

Use **Mark all read** to mark every unplayed article in the library as played, including articles hidden by the current filter. The trash button on an article immediately deletes its MP3 and saved article details; deleting the currently playing article stops playback first.

Set `mark_as_played` in `settings.yaml` to one of:

- `when_started` — mark an article played as soon as playback starts.
- `when_ended_or_skipped` — mark it when playback ends or you move to another article.

Set `playback_speed` to `0.7`, `0.8`, `0.9`, `1`, `1.1`, `1.2`, or `1.3` (default `1`); `auto_play_next` defaults to `true`. Playback speed is changed from the rabbit menu beside the player controls. Auto Play Next and the mark-as-played rule remain available under **Settings > Playback**. Older speed settings above 1.3x are migrated to 1.3x when loaded.

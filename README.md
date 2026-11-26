# AnchorFeeds

AnchorFeeds is a Windows desktop app that turns RSS and Atom feeds into a personal news broadcast. It reads each new article aloud with text-to-speech, shows the article's images, text and YouTube thumbnails, and remembers what you have played and starred.

## What you can do with it

- **Listen to your feeds.** Add RSS/Atom sources. AnchorFeeds fetches new articles, generates a title and body MP3 for each one with Edge TTS, and plays them in order. Several voices can be selected and are rotated between articles.
- **Choose how to listen.** *Scan mode* (default) plays each title, shows a countdown, then moves on; click the countdown to hear the full body. *Full mode* plays the title and then the body.
- **Read along.** The article text scrolls with the audio. Scroll the text to seek, or drag the progress bar.
- **Organize the library.** Filter by feed or category, sort, group by feed, show unread or starred only, and star articles you want to keep.
- **Stay on top of favorites.** Favorite and starred keywords and categories are highlighted with pills and a ribbon, and favorite articles get an attention chime.
- **Run in the background.** Feeds update on a schedule while the app is minimized or in the notification area. A tray icon shows a blue dot when unread articles exist.
- **Take it with you.** Export the visible articles as one commute MP3.
- **Control it from the keyboard or headset.** Configurable hot keys and Windows media buttons (Play/Pause, Previous, Next).
- **Make it yours.** Light, Dark, Charcoal and System Desktop themes, custom fonts, 14 interface languages, and share buttons for X, Facebook and email.

## Requirements

| Requirement | Needed for |
| --- | --- |
| Windows 10 (19041) or later, x64 | Running the app |
| [`edge-tts`](https://github.com/rany2/edge-tts) command (`edge-tts.exe` on `PATH`, or in the current user's Python Scripts directory) | Generating article audio |
| [FFmpeg](https://ffmpeg.org/) on `PATH` (FFprobe optional) | MP3 bitrates above 48 kbps, commute MP3 export and its detailed progress |
| .NET 10 SDK | Only to run from source or build the installer |

## Installation

### Option 1: MSI installer (easiest)

1. Install the requirements above (`edge-tts` and FFmpeg).
2. Download [`AnchorFeeds-Setup.msi`](AnchorFeeds-Setup.msi) from the repository root (or the latest GitHub Release) and run it. Click **Install**, then leave **Launch AnchorFeeds** checked. No .NET SDK or command line is needed.
3. Start **AnchorFeeds** from the Start Menu.

The MSI installs per user to `%LocalAppData%\Programs\AnchorFeeds`, adds a Start Menu shortcut, and keeps your `settings.yaml` when you upgrade.

### Option 2: Run from source

```powershell
dotnet run --project .\AnchorFeeds\AnchorFeeds.csproj
```

### Option 3: Publish a Windows executable

```powershell
dotnet publish .\AnchorFeeds\AnchorFeeds.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

A Release publish also builds the MSI and copies it to the repository root as `AnchorFeeds-Setup.msi`. Add `-p:BuildInstaller=false` to skip it. The published executable needs `settings.yaml` beside it.

In VS Code, **Ctrl+Shift+B** publishes without the MSI. **Alt+Shift+B** prompts for a version, builds the MSI, commits the version bump, pushes, and publishes a GitHub release (requires the `gh` CLI, signed in).

## Quick start

1. Launch AnchorFeeds and open **Settings** (the cog, or press `O`).
2. In **Feeds**, add your RSS/Atom sources. In **Audio**, select one or more voices (use **Sample** to preview).
3. Click **Update Feeds** (`Ctrl+F5`). New articles are converted to audio and appear in the Feed Library.
4. Select an article, or press `Space` to start playback.

## Using the app

### Player

- On startup the AnchorFeeds logo is shown until an article is selected. Selecting an article starts its title narration immediately.
- The article title is at the top, image and text are stacked in the center, and the playback controls are at the bottom. When available, the feed's logo appears beside the title; if it fails to load, the app tries the site's `/favicon.ico` and `/apple-touch-icon.png`.
- **View > Show article text** shows or hides the body text used for the audio. During body playback the text scrolls in proportion to audio progress. Scrolling the text seeks the body audio to the matching percentage, even when paused (approximate, not word-synchronized; scrolling during the title does not seek it).
- Click or drag the progress bar to seek, or focus it and use the arrow keys (1%), Page Up/Down (10%), or Home/End.
- Click the Scan/Full icon to switch modes; the choice is remembered.
  - **Scan mode** (default): plays the title, shows a countdown over the image, then advances to the next visible article regardless of Auto Play Next. Click the countdown to play the body instead. Click the image when no countdown is visible to open the source in a browser. Configure the countdown in Audio settings: 0 (skip), 3, 5, 7, 10, 15, 20, 30 or 60 seconds.
  - **Full mode**: plays the title then the body. **Auto Play Next** decides whether playback continues afterwards.
- A page-flip sound plays after an article ends, before the next one starts. It is not included in the MP3 files.
- Opening an article in a browser skips auto-advance once when that article finishes.
- The divider between the article list and player can be dragged. When both image and text are visible, their horizontal divider can also be dragged.
- Starred articles can be set from the list or the player. Share buttons beside the progress bar open the article in X, Facebook or your default email app using share links (no API access needed).
- **Media buttons:** AnchorFeeds publishes the current article to the Windows media session, so headphone or keyboard Play/Pause, Previous and Next work in the background. Windows may route them to another app that owns the active media session.

### Feed Library

- Each entry shows its feed publication date/time, or the audio file creation date/time otherwise. Played titles use regular-weight text, and read articles have a background 29% darker than unread ones.
- Beside the Audio library heading: the circular-arrow **Update Feeds** icon and **Mark all read**. The sort icon offers alphabetical, newest-first and oldest-first order plus **Group by Feed**. The funnel opens **Unread only** and **Starred only** filters. These selections are saved in `settings.yaml`.
- **Unread only** (`View > Unplayed only`) can start enabled and is remembered. Articles marked read automatically (playback, navigation or scrolling) stay visible until the next successful feed update. Explicitly marking articles read filters them immediately. Starred articles remain visible while it is active.
- The library refreshes automatically after retrieving feeds. If a feed returns an HTTP error it is skipped, the rest are still processed, and a warning lists the feed URL and error.
- Right-click the **Load in browser** icon to open the article's local media folder in File Explorer. Left-click opens the article in the browser.
- The **AI** button beside **Load in browser** (enabled in General settings) opens a menu of chat services. The chosen service opens with a URL-encoded query containing your instruction, the article title and its source URL, and the article body is copied to the clipboard. Gemini opens its home page and copies the full query plus body because it has no prompt-prefill URL.

#### Navigation pane

The pane beside the Feed Library filters by one or more sources and categories. **All feeds** clears selections. Expand **Sources** to pick a feed or its categories. Click to replace the selection; hold **Ctrl** to add more. Its divider position and collapsed state are saved and restored.

#### Marking, deleting and rebuilding

- **Mark visible as read** applies only to articles currently shown. To mark the whole library, choose **All feeds** and clear other filters.
- **Mark all read** marks every unplayed article as played, including articles hidden by the current filter.
- The trash button deletes an article's MP3 and saved details. Deleting the playing article stops playback first.
- The circular-arrow icon at the right of the player's top action row rebuilds the selected article from its current feed entry. It replaces the audio and clears cached media before fetching fresh images, then reloads and reselects the article without starting playback.
- **Optional scroll-to-read:** the setting *Mark articles as read when scrolled above the feed list* marks items once they scroll completely above the list viewport. Extra space at the end lets the last item scroll out too. It is off by default, and read updates are saved together shortly after scrolling pauses.

#### Categories, keywords and favorites

- Right-click an article in the list to favorite or block one of its feed's categories. The **Favorites** filter shows saved articles matching favorite categories, and matching items show a blue corner ribbon.
- The feed properties gear shows saved favorite and blocked category pills; remove a pill to clear it. Preferences are stored in `%LOCALAPPDATA%\anchorfeeds\feed-category-preferences.json`.
- Blocked categories stop matching items from being added during feed updates; existing items are not deleted. Favorite categories are labels only and do not star articles.
- Favorite and starred words/phrases (set in **Settings > Feeds**) that match an article title appear as green and gold pills beside its action buttons. Matching favorite categories appear as dark blue pills. When pills do not fit, **...** opens Feeds settings.
- Right-click a pill to hide all matching pills, open Feeds settings, or remove that keyword or category from its list. Deleting a starred keyword does not unstar existing articles. Restore hidden pills with **Show matching keywords** in Feeds.
- Articles in favorited categories get a short attention chime before the title audio in exports.

#### Commute MP3 export

**Export commute MP3** exports the articles currently visible in the Feed Library, in displayed order and respecting active filters. Choose titles only or titles plus bodies, then pick where to save. Progress shows the item being processed and the encoding percentage when FFprobe is available (FFmpeg and FFprobe on `PATH` enable detailed progress).

### Hot keys

Focus a shortcut field in **Settings > Interface** and press the desired combination. Ctrl, Shift and Alt are supported; Backspace/Delete disables a shortcut. Hot keys work in the main window except while typing. Custom shortcuts are saved in `settings.yaml`.

| Key | Action |
| --- | --- |
| Space | Play / pause |
| Down / Up | Next / previous article |
| Right / Left | Forward 30 s / back 10 s (see below) |
| Shift+Up / Shift+Down | Volume up / down (10% per press) |
| M | Mute |
| F / S | Full mode / Scan mode |
| Ctrl+F5 | Update the Feed Library |
| F5 | Rebuild the current article (after confirmation) |
| Ctrl+Shift+A | Mark all as read |
| O | Open Settings |

When an article has more than one image/video, Right and Left first step through the carousel; once at that end (or with a single media item) they skip the audio.

## Settings

Open **Settings** with the cog. Each option has a clickable **(?)** help button and a hover tooltip. Changes are saved to the selected settings destination and applied immediately (except language, which applies on next start). Settings are grouped into five tabs.

### General

- **Application Language / Locale:** defaults to **System default** (the Windows display language at startup). Available: English, Czech, German, Spanish, French, Italian, Japanese, Korean, Polish, Brazilian Portuguese, Russian, Turkish, Simplified Chinese and Traditional Chinese. Stored as `application_language`.
- **Retrieve latest feeds on application launch:** on by default.
- **Check for application updates:** on by default. At startup (when the window is visible) AnchorFeeds checks the latest GitHub release; if it is newer than the running version, it offers to download and install `AnchorFeeds-Setup.msi`, then closes, updates and restarts. Stored as `check_for_updates`.
- **Feed update polling interval:** defaults to **Manual only**; feeds set to **Use global interval** inherit it.
- **Launch at startup:** starts AnchorFeeds at Windows sign-in (synchronized with the current user's Run registry key).
- **Minimize to tray / Close to tray:** hide the window when minimized or closed. With launch at startup and minimize to tray both on, the app starts hidden. Right-clicking the title-bar Close button always exits. The tray icon offers Open and Exit.
- **Media output directory:** click the path field or **Browse...**.
- **AI chat:** enable the AI button and set its prompt instruction.
- **Portable Mode:** see [Where settings and data live](#where-settings-and-data-live).

### Interface

- **Theme:** **Light**, **Dark**, **Charcoal**, or **System Desktop Theme** (follows the Windows light/dark preference and system colors, and refreshes when they change). Saved as `appearance_theme`.
- **Fonts:** family and size for the Title List and Article Text, applied immediately.
- **Hot keys:** see [Hot keys](#hot-keys).

### Feeds

- Configure each RSS/Atom source independently: name, link, group/category, and update period. Each feed can inherit the global interval, use a fixed interval, or be **Manual only**. Manual **Update Feeds** checks every source.
- Favorite and blocked categories are managed per feed from article context menus and the feed properties gear.
- Banned, favorite and starred words and phrases are edited here.
- Structured feed settings are stored in `feed_sources`.

### Audio

- **Voices:** grouped by language and country, with the system locale expanded first. Select several with their checkboxes and preview with **Sample** (it spins while generating and highlights while playing). New articles rotate through the selected voices; if edge-tts returns no audio, the other selected voices are tried before failing. With none configured, the app defaults to Guy and Jenny for en-US, or one female and one male voice for the system locale (falling back to the system language), a practical rather than popularity-based default.
- **MP3 bitrate:** **Default** and 48 kbps use Edge TTS's native 24 kHz mono output without FFmpeg. 96, 128, 160, 192, 256 and 320 kbps re-encode new files with FFmpeg.
- **Scan mode countdown**, **Auto Play Next** (default `true`) and the mark-as-played rule.
- **Retention:** general audio retention plus separate **Prune Starred** and **Prune Favorites** periods. Enter `never` (default) or a duration such as `12 hours`, `3 days` or `2 weeks`. Favorites include favorite feed categories and phrases. When an article is both starred and favorite the longer period applies, and `never` wins. Expiration uses the audio file's creation time (or the article folder's when generation failed) and runs on library load, refresh, or manual pruning. Saving settings can prune immediately; manual pruning uses the last saved settings.
- `mark_as_played` in `settings.yaml` is either `when_started` (as soon as playback starts) or `when_ended_or_skipped` (when playback ends or you move on).

### Maintenance

- **Enable verbose debug logging:** appends detailed startup, feed-update, selection and audio-playback diagnostics to `AnchorFeeds-debug.log` beside the executable. It can include article titles, feed URLs and local media paths. Off by default.
- **Rebuild Library** is a total reset: it permanently deletes matching article audio and folders (including cached images and text) and clears saved library state.
- **Prune** removes expired articles using the last saved settings.

## How feed updates work

- Each update processes up to **100 new articles per feed**, oldest first, and resumes any remaining backlog on a later update. This cap bounds feed and Edge TTS work.
- Feed item cursors and last successful check times are stored in `%LOCALAPPDATA%\anchorfeeds\feed-update-state.json`, separate from settings. Items at or before a feed's cursor are skipped without checking audio files or reporting per-item skips.
- Scheduled updates continue while the app is minimized or in the notification area. Due feeds are checked about every 10 seconds, each on its own interval; manual-only feeds are never polled automatically. A failed feed is retried after its interval. The computer must be awake and the app running.

## Media and article content

- Each article gets a folder named `{feed}_{title}` (sanitized slugs) in the media output directory containing `title.mp3`, `body.mp3` and the cached article image when available. Temporary generation files go in a `.tmp` subfolder, created when generation runs.
- Body narration uses the same filtered, visible text as Article Text, without repeating the title. Article Text keeps supported feed formatting (headings, lists, emphasis, selected inline font styles); links and their text are omitted from display and narration. If formatted content is unavailable, the saved plain text is used.
- When the body text equals the title, body playback is suppressed as duplicate narration but formatted text remains visible. MP3 file sizes are never used to detect duplicates.

## Where settings and data live

| Data | Location |
| --- | --- |
| Settings (Portable Mode, default) | `settings.yaml` beside the executable; relative paths resolve from there |
| Settings (Portable Mode off) | YAML text in `HKEY_CURRENT_USER\Software\Satalink\AnchorFeeds` |
| Played/starred library state | `%LOCALAPPDATA%\anchorfeeds\library-state.json` |
| Feed update cursors | `%LOCALAPPDATA%\anchorfeeds\feed-update-state.json` |
| Category preferences | `%LOCALAPPDATA%\anchorfeeds\feed-category-preferences.json` |
| Debug log | `AnchorFeeds-debug.log` beside the executable |
| Article audio and images | The configured media output directory |

Only one settings source is active at a time. If a registry settings document exists it takes precedence at startup, and any adjacent `settings.yaml` is deleted. Switching off Portable Mode saves the settings to the registry and deletes `settings.yaml`; switching it back recreates `settings.yaml` and removes the registry settings. Publish output includes `settings.yaml` for first-run portable use, so publishing can recreate it while registry mode is active; the app deletes that copy on its next startup.

Only one AnchorFeeds instance runs per Windows user; launching again restores the existing window. The app creates the library-state and media output directories when needed. The article-list divider is saved as `article_list_width_ratio` and the image/text divider as `article_image_height_ratio` in `settings.yaml`; neither appears in the settings window.

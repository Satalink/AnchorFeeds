## Development Tasks

- [ ] Add Discord feed-category discussions — Build a Satalink discussion bridge for AnchorFeeds. When a user chooses **Discuss** on an article, identify the discussion by normalized feed URL and category. Automatically create the Discord thread if one does not exist, reuse it for later articles in that feed/category, and return a direct link that opens the discussion in Discord or a browser. Keep replies and moderation in Discord; do not build an in-app commenting system. Store Discord bot credentials only on the service, and make thread creation idempotent with reasonable rate limiting to prevent duplicates or accidental floods. *(Added: 2026-10-29)*

- [ ] Add Disqus functionality (research how to link it) *(Added: 2026-10-26)*
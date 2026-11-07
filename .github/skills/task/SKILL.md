---
name: task
description: Append a new development task to TODO.md for AnchorFeeds
disable-model-invocation: true
---
You are assisting with the C# WPF .NET 10 desktop application **AnchorFeeds**. 

When the user invokes `/task [description]`, perform the following actions:
1. Locate or create the `TODO.md` file in the workspace root directory.
2. Ensure it contains a `## Development Tasks` section header.
3. Append the new task as an unchecked markdown list item with the current date: `- [ ] [description] *(Added: YYYY-MM-DD)*`.
4. Confirm to the user that the task has been successfully logged to `TODO.md`.
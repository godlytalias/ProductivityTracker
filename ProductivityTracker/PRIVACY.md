# Productivity Tracker Privacy Policy

**Effective date: November 6, 2026**

Productivity Tracker is maintained in the godlytalias/ProductivityTracker GitHub repository. This policy explains how the app handles information when you use it.

## Information processed

To display your PC-usage history and activity insights, the app processes:

- Foreground application names and executable/process names.
- Activity start and end times, durations, and active or inactive status.
- Locked, sleeping, and confirmed powered-off periods, using Windows session/power notifications and available system power-event records.
- Local preferences, such as the inactivity threshold and optional sign-in startup configuration.
- Local diagnostic logs, which may contain error messages, timestamps, technical details, and file paths.

Recent-input information is used to distinguish active time from inactivity. The app does not record individual keystrokes, typed text, window titles or contents, browser history, URLs, screenshots, clipboard contents, microphone audio, or camera video.

No account is required.

## How information is used

Information is used on your PC to record usage intervals, display charts and timelines, calculate activity patterns, apply your tracking preferences, and diagnose application errors. Activity time is an interaction-time measure, not a measure of work output.

The app runs with normal desktop-user permissions, declared as `runFullTrust` in its Windows package. This does not grant administrator privileges.

## Local storage and retention

Usage history, preferences, and diagnostic logs are stored locally, normally in `%LOCALAPPDATA%\ProductivityTracker`. Use **Tracking settings > Open local data folder** to find the app's data.

Detailed activity history is retained for one calendar month, starting at local midnight on the corresponding date in the previous month. Older intervals are automatically removed; intervals crossing that boundary are clipped to the retained period. This also applies to previously stored history when the app loads it and next saves it with exclusive storage access.

Diagnostic logs rotate at 1 MiB per file, with at most three archived files in addition to the current log. Preferences remain until you change or delete them. The app does not apply additional encryption to these files; protection depends on your Windows account, file permissions, and device security. Other software or users with sufficient access to your device may be able to read them.

## Background tracking and your controls

Tracking runs while the dashboard or windowless collector is running. **Keep tracking after closing** and **Run at sign-in** are enabled by default and can be changed independently in **Tracking settings**. The first keeps a windowless collector running after the dashboard closes. The second starts the collector automatically at sign-in without opening a window.

**Pause** stops foreground-application tracking. Inactive, lock, and power-state timing may still be recorded while the background collector remains running.

To stop all app recording, turn off both **Keep tracking after closing** and **Run at sign-in**, then close the dashboard. Closing with background tracking disabled stops the collector. Disabling sign-in startup alone does not terminate an already-running collector. If an older collector cannot be stopped by the app, stop that Productivity Tracker process in Windows Task Manager.

You can review your history in the app. To delete locally stored information, first stop the dashboard and collector, then delete the files in the local data folder. Uninstalling the app may leave this folder behind.

Use the app only on your own account/device, or with the appropriate knowledge and permission of other affected users.

## Sharing and platform services

Productivity Tracker does not automatically upload your usage history or local log files to the developer. We do not sell or rent usage data or use it for advertising. The app does not provide cloud synchronization.

Windows, Microsoft Store, and Windows App SDK components may separately process installation, update, and diagnostic information under your Windows settings and Microsoft's privacy policy. Crash diagnostics can include technical information or portions of application memory. Device backup or management software may also copy local files independently of this app.

## Support communications

If you contact the maintainers through GitHub, you choose what information to include. Public issues and their attachments are visible to others. Do not post private usage history, personal identifiers, credentials, or sensitive logs in public issues. The app does not automatically attach or submit usage data.

## Changes and contact

We may update this policy when the app or its information-handling practices change. The effective date above identifies the current version.

For privacy questions, see the repository owner's GitHub profile. Do not include private information in public issues.

[Repository owner](https://github.com/godlytalias)

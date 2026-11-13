## Populating the carousel

Last updated: 2026-10-01 16:15

1. Add the images
  Put jpg files in `GreenSwamp.Alpaca.Server\wwwroot\images\carousel\<folder>\`, for example `...\carousel\astronomy\M42.jpg`.
  •	The two folders are reference (server screenshots) and astronomy (astronomical images).
  •	Folder and file names must be plain: no subfolders, .. or \.
  •	Aim for about 1600–1920 px wide and under 500 KB each.
  use the `wwwroot\images\carousel\<folder>\` folder under
2. List them and add captions
  •	Edit carousel.settings.json in the versioned settings folder, for example %AppData%\GreenSwampAlpaca\0.2.0\carousel.settings.json.
  •	The app creates this file on first run.
  •	Slides play in the order listed. Each caption is one line.

`{`
  `"DwellSeconds": 5,`
  `"ActiveShow": "reference",`
  `"Shows": [`
    `{`
      `"Id": "reference",`
      `"Title": "Server Reference",`
      `"Folder": "reference",`
      `"Slides": [`
        `{ "File": "mount-control.jpg", "Caption": "Mount Control - hand controller and GoTo" },`
        `{ "File": "settings.jpg",      "Caption": "Settings Explorer" }`
      `]`
    `},`
    `{`
      `"Id": "astronomy",`
      `"Title": "Astronomical Images",`
      `"Folder": "astronomy",`
      `"Slides": [`
        `{ "File": "m31.jpg", "Caption": "M31 - Andromeda Galaxy" }`
      `]`
    `}`
  `]`
`}`

3. Check it
•	Reload the home page. You don't need a restart or a rebuild.
•	DwellSeconds is clamped to 2–60 s.
•	The Folder value must match the folder name on disk.
•	A show with no slides is hidden. The show toggle only appears when two or more shows have slides.
A missing image shows `Image not found: <file>` for that slide.
•	Entries with an invalid folder or file name are dropped, and a warning goes to the log.
4. Things to know
•	The app rewrites ActiveShow when someone switches show. Your other edits are kept.
•	Each new app version gets its own settings folder. It copies the file from the previous version, so an old copy can carry forward.
•	Add any new images to GreenSwamp.Alpaca.Installer\ProductFiles.wxs before you build an installer, or they won't ship.
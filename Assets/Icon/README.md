# LogLens icon sources

- `LogLens.svg`: detailed artwork, used for 48 px and larger
- `LogLens-small.svg`: simplified artwork (thicker ring, lighter tile with an edge), used for 16, 24 and 32 px so it stays readable in the tray and taskbar

`LogLens.ico` (project root) contains 16, 24, 32, 48, 64, 128 and 256 px PNG images rendered from these files. `wwwroot/favicon.ico` (browser tab) contains 16, 32 and 48 px.

To change the icon, edit these SVGs, export PNGs at those sizes, pack them into `LogLens.ico`, and rebuild.

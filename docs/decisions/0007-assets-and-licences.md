# 0007: Asset sources and licences

- Status: Proposed
- Proposed by: @SWTY7
- Affects: character, audio, world, design
- Date: 2026-10-13
- Supersedes: none

## Question

Where may models, textures, sounds and music come from, and how do we record them?

## Facts

- The repo is public and its code is MIT. Third-party notices live in [THIRD_PARTY.md](../../THIRD_PARTY.md).
- Binary assets are tracked with Git LFS ([.gitattributes](../../.gitattributes)).
- The Sketchfab Manhattan model the team linked is by user "truekit", licence **CC BY 4.0** (attribution required), 336,600 faces, downloadable, as read from Sketchfab's public API on 2026-10-13. Check again before use; this is not a legal review.

## Options

### A. Original or CC0 only

No attribution headaches, safe for any later use. Slowest to source.

### B. CC0 and CC BY, with attribution tracked

More material available. Every CC BY item needs a credit line shown in the release's notices. Requires the discipline of a licence table.

### C. Anything free to download

Unclear rights and unsafe in a public repo. Not acceptable.

## Recommendation

B, with a hard rule: no merge without a row in the licence table (name, author, source link, licence, where used). Prefer A where equal. Trademarked material (Marvel, Sony, music, sound effects ripped from films or games) is excluded; see [0002](0002-name-and-ip.md). Add `.ogg` and `.flac` to LFS patterns.

## Outcome

Pending.

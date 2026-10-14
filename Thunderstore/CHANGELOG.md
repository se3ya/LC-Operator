# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [1.1.0] - 2026-10-14

### Added

- Joining while cassette plays on the terminal starts it at the same time *[ if you have that recording on disk ]*
- TV deck waits for everyone to get the recording before playing
- SelfSortingStorage compat
- LCVR compat

### Changed

- Terminal video has its own screen, now everyone sees it
- Terminal video keeps its aspect ratio with black bars instead of stretching to 4:3
- Code cleanup

### Fixed

- `play` on another cassette not stopping the one already playing
- Saved cassettes showing `NOT ON DISK` after their recording was deleted by hand, they are removed from list now
- `delete` also deleting the recording of another saved cassette that used the same file
- Recorded voice sounding underwater when only being half in water

## [1.0.0] - 2026-10-08

### Added

- Initial release!

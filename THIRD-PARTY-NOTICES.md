# Third-party notices

YSM Installer redistributes the following third-party components.

## 7-Zip (native `7z.dll`)

The installer bundles the native 7-Zip library to extract mod archives
(`.7z`, `.rar`, `.tar`, `.gz`, `.bz2`, `.xz`). It is shipped gzip-compressed at
[`src/Resources/native/7z.dll.gz`](src/Resources/native/7z.dll.gz) and inflated
to a per-user cache at runtime.

- **Component:** 7-Zip `7z.dll`
- **Version:** 26.01 (2026-04-27), 64-bit
- **Copyright:** Copyright (C) 1999-2026 Igor Pavlov
- **Source:** https://www.7-zip.org/ — official x64 installer
  `7z2601-x64.exe` (https://github.com/ip7z/7zip/releases/download/26.01/7z2601-x64.exe)
- **License:** GNU LGPL v2.1+ (parts under BSD-2/BSD-3 and the unRAR restriction)
- **License text:** [`licenses/7-Zip-LICENSE.txt`](licenses/7-Zip-LICENSE.txt)
- **SHA-256 (uncompressed `7z.dll`):**
  `9657871fdd714c96a3a21f59abf04abb14944516c2a10c8cb606c12a71a75957`

The hash and the uncompressed size are pinned in
[`src/Utilities/SevenZip/SevenZipLibrary.cs`](src/Utilities/SevenZip/SevenZipLibrary.cs)
(`ExpectedSha256` / `ExpectedRawSize`) and verified before the library is loaded.

### unRAR restriction

7-Zip's RAR support is built from unRAR sources. As required by that license:
this software uses the unRAR code only to **decompress** RAR archives and **may
not be used to develop a RAR (WinRAR) compatible archiver**.

### LGPL re-linking

`7z.dll` is bundled unmodified and loaded dynamically, so it can be replaced by
the user with another build of the same library (drop a compatible `7z.dll` into
the runtime cache directory).

### How to update the bundled `7z.dll`

1. Download the official 7-Zip release for Windows x64 from https://www.7-zip.org/.
2. Take `7z.dll` from the installed/extracted package.
3. Re-create `src/Resources/native/7z.dll.gz` (gzip of the raw `7z.dll`).
4. Update `ExpectedSha256`, `ExpectedRawSize`, and the version note in
   `SevenZipLibrary.cs`, and the version/hash above.
5. Rebuild and re-run the extraction tests.

> **Distribution requirement:** when shipping a release build, include
> `licenses/7-Zip-LICENSE.txt` (or the contents of this file) alongside the
> executable — 7-Zip's license requires binary redistributions to reproduce its
> license information.

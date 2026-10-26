# Build and release

Windows release executable: Python 3.11+ with Tkinter, and `pip install pyinstaller==6.22.3`. The manager source itself supports Python 3.9+ and has no third-party runtime dependencies.

```powershell
python -m unittest discover -s tests -v
python -m unittest discover -s analysis -p "test_*.py" -v
python tools/release.py verify
python tools/release.py build
```

`build` produces `dist/GambleChoice-<version>-Windows.zip` containing the standalone manager EXE, source, payload, examples, docs and tests. No whole M-USE project, recordings, participant data or local Git metadata is included. SHA-256 sidecars describe the distributable; `module.json` checks every payload file. These checks detect accidental changes; they are not an executable signature.

Update source, bump the version in `module.json`, refresh hashes with `python tools/release.py manifest`, and verify the result before tagging. A new manifest should only be generated for reviewed source changes, never used to hide corruption in a downloaded release.

The GitHub Actions workflow runs manager/analysis tests and creates a Windows artifact on pushes and pull requests. A `v*` tag also publishes a release ZIP in the same repository. A private repository keeps its releases private. Unity destination checks run locally because CI does not contain a licensed M-USE project.

Before marking a version compatible, retain the target M-USE commit, Unity version, install/validate/disconnect results and data checks. CI's temporary-project test doubles are not Unity runtime evidence.

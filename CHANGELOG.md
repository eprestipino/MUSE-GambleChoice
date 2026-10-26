# Changelog

## 0.1.1

- Normalize temporary test paths before comparison, including Windows short-name aliases used by hosted runners.
- Preserve previously published release assets when a tag workflow rebuilds them; independently built artifacts remain available in Actions.
- Task payload and installer behavior are unchanged from 0.1.0.

## 0.1.0

- Standalone GambleChoice task source, example protocol, configuration reference and analysis tools.
- Connect/update/disconnect manager with conflict/GUID checks, transaction recovery, validation and rollback.
- Portable editor templates, configuration/icon export, and demo output under Unity persistent data.
- Destination Unity model/scene/export validation; separate installer and malformed-data regressions.
- Windows release executable and GitHub Actions release workflow.

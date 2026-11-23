# Babylon.js tree-shaken build

Last updated: 2026-11-23 10:51

The TelescopeView 3D page loads `GreenSwamp.Alpaca.Server/wwwroot/lib/babylon/babylon.slim.js`. It is built here from the
Babylon.js ES modules, so it contains only the classes the viewer uses (about 1.5 MB instead of the 9.5 MB full UMD
bundle plus loaders and materials library). The built file is committed, so normal builds and CI need no Node toolchain.

## Rebuild

```powershell
cd tools\babylon-build
npm install
npm run build
```

Commit the regenerated `babylon.slim.js`, and `package-lock.json` if it changed.

## When to rebuild

- **The viewer uses a new Babylon class** (in `telescopeViewer.js` or `telescopeAssembly.js`): import it in `src/entry.js`,
  add it to `window.BABYLON`, then rebuild. A missing class shows up at runtime as `B.<Name> is not a constructor`.
- **Upgrading Babylon.js**: change both `@babylonjs/*` versions in `package.json` (keep them identical), then rebuild
  and test every model type and background in the 3D view.
- **Loading another model format** (for example `.glb`): import its loader from `@babylonjs/loaders` in `src/entry.js`.

`entry.js` exposes `MeshBuilder` as an object with only `CreateBox`, `CreateCylinder` and `CreateGround`; add other
builders there as needed.

// telescopeViewer.js  —  minimal Babylon.js interop for telescope 3D prototype
// Babylon.js 7.x loaded from CDN; OBJ loader plugin included via separate script tag in the razor page.

let _engine = null;
let _scene = null;
let _root = null;   // parent mesh — rotate this for Alt/Az

export function init(canvasId, objUrl) {
    const canvas = document.getElementById(canvasId);
    if (!canvas) { console.error('telescopeViewer: canvas not found:', canvasId); return; }

    _engine = new BABYLON.Engine(canvas, true, { preserveDrawingBuffer: true });
    _scene = new BABYLON.Scene(_engine);
    _scene.clearColor = new BABYLON.Color4(0.1, 0.1, 0.15, 1);

    // Camera — user can orbit with mouse
    const camera = new BABYLON.ArcRotateCamera('cam', -Math.PI / 2, Math.PI / 3, 6,
        BABYLON.Vector3.Zero(), _scene);
    camera.attachControl(canvas, true);
    camera.lowerRadiusLimit = 2;
    camera.upperRadiusLimit = 20;

    // Hemisphere light
    new BABYLON.HemisphericLight('light', new BABYLON.Vector3(0, 1, 0), _scene);

    // Root mesh — all imported meshes are parented here so a single rotation drives both axes
    _root = new BABYLON.Mesh('telescopeRoot', _scene);

    // Load the OBJ (falls back gracefully if file is missing)
    const lastSlash = objUrl.lastIndexOf('/');
    const rootUrl = objUrl.substring(0, lastSlash + 1);  // "/models/"
    const fileName = objUrl.substring(lastSlash + 1);     // "telescope.obj"

    BABYLON.SceneLoader.ImportMesh('', rootUrl, fileName, _scene,
        (meshes) => {
            meshes.forEach(m => { m.parent = _root; });
            console.log('telescopeViewer: loaded', meshes.length, 'mesh(es) from', objUrl);
        },
        null,
        (scene, msg) => {
            // Fallback: show a box so the interop chain is still provably working
            console.warn('telescopeViewer: OBJ load failed, using placeholder box.', msg);
            const box = BABYLON.MeshBuilder.CreateBox('placeholder', { size: 1 }, _scene);
            box.parent = _root;
        }
    );

    _engine.runRenderLoop(() => _scene && _scene.render());
    window.addEventListener('resize', () => _engine && _engine.resize());
}

// Called from Blazor on every state update
export function updateRotation(altitudeDeg, azimuthDeg) {
    if (!_root) return;
    // Babylon.js is left-handed Y-up:
    //   Altitude  → pitch  → rotation.x
    //   Azimuth   → yaw    → rotation.y
    // Adjust signs if the model points in the wrong direction.
    _root.rotation.x = BABYLON.Tools.ToRadians(altitudeDeg);
    _root.rotation.y = BABYLON.Tools.ToRadians(azimuthDeg);
}

export function dispose() {
    _engine?.stopRenderLoop();
    _scene?.dispose();
    _engine?.dispose();
    _scene = null;
    _engine = null;
    _root = null;
}
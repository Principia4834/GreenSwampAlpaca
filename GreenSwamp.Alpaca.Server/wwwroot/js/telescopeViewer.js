// Telescope 3D viewer (Babylon.js) for the /telescope-view page.
//
// Ported from GS.Point3DWeb (model3d.js), which follows GSServer's Helix 3D view so the GSServer
// .obj models and axis offsets work unchanged. Unlike Point3DWeb, which derives the mount axes from
// RA / Dec / side of pier, the server posts the raw mount axis positions, so the pose is computed
// server side (Models/Model3D.cs) and arrives here as setPose(x, y, z, yCentre).
//
//   - Helix is right-handed with Z up. The Babylon scene is right-handed too, and everything
//     mount-related lives under a root node that turns Helix Z-up into Babylon Y-up and scales
//     Helix units (pier = 500) down to scene units.
//   - The model is moved by GSServer's transform group: translate (1,0,0), then rotate about
//     Z by XAxisOffset, about Y by YAxisOffset, about X by ZAxisOffset, all about the centre
//     (0, YAxisCentre, 525).
//   - The pier is GSServer's LoadPierModel: a plain pier for Alt-Az, a pivot arm for German
//     polar and a latitude-canted pier for polar (fork) mounts.
//
// Camera angles are in degrees (alpha, beta). Babylon is loaded by window.ensureBabylonLoaded().

const assetBase = new URL('../model3d/', import.meta.url).href;

// GSServer defaults (Server.settings): ModelPierHeight, ModelBeamWidth, ModelPierPivot.
const PIER_HEIGHT = 500;
const BEAM_WIDTH = 50;
const PIER_PIVOT = 130;
const ROTATION_CENTRE_Z = 525;
const HELIX_TO_SCENE = 7 / PIER_HEIGHT; // pier is 7 scene units tall

// Ground grid (scene units; the pier is 7 tall and the compass base 15 across).
const GRID_SIZE = 160;          // width of the ground plane
const GRID_CELL = 1;            // minor grid spacing
const GRID_MAJOR_EVERY = 5;     // a major line every 5 cells
const GRID_Y = -1.01;           // just under the compass base (its underside is at -1)

// ASCOM AlignmentModes
const ALT_AZ = 0, POLAR = 1, GERMAN_POLAR = 2;

const rad = deg => deg * Math.PI / 180;
const deg = r => r * 180 / Math.PI;

// Default camera: looking at the mount head from the south-west, a little above.
const DEFAULT_CAMERA = { alpha: 150, beta: 60, radius: 40, target: { x: 0, y: 6, z: 0 } };

class TelescopeViewer {
    constructor(canvas, options) {
        const B = window.BABYLON;
        this.canvas = canvas;
        // Green Swamp palette: primary = telescope (green), secondary = light (moon white).
        this.primaryColor = options.primaryColor || '#52b788';
        this.secondaryColor = options.secondaryColor || '#e8e6df';
        this.compassRequest = 0; // so a compass image that loads late can't replace a newer one
        this.modelMeshes = [];
        this.modelMaterials = [];
        this.pierMeshes = [];
        this.loadCount = 0;      // so an older loadModel that finishes late is dropped
        this.pose = { x: 0, y: 0, z: 0, yCentre: 0 };

        this.engine = new B.Engine(canvas, true, { preserveDrawingBuffer: true, stencil: true, antialias: true });
        this.scene = this.createScene();
        this.engine.runRenderLoop(() => this.scene && this.scene.render());

        // The canvas fills a flex panel, so follow its size rather than the window.
        this.resizeObserver = new ResizeObserver(() => this.engine.resize());
        this.resizeObserver.observe(canvas);

        this.setGrid(options.grid !== false);
        this.setCamera(options.camera);
    }

    /** Applies a saved camera ({ alpha, beta (degrees), radius, target: { x, y, z } }), or the default. */
    setCamera(view) {
        const B = window.BABYLON;
        const v = view || DEFAULT_CAMERA;
        const t = v.target || DEFAULT_CAMERA.target;
        // setTarget can rebuild alpha / beta / radius, so set the target first.
        this.camera.setTarget(new B.Vector3(t.x, t.y, t.z));
        this.camera.alpha = rad(v.alpha);
        this.camera.beta = rad(v.beta);
        this.camera.radius = v.radius;
    }

    getCamera() {
        const c = this.camera;
        const round = (n, d) => Math.round(n * 10 ** d) / 10 ** d;
        return {
            alpha: round(((deg(c.alpha) % 360) + 360) % 360, 2), // the camera can wind past 360
            beta: round(deg(c.beta), 2),
            radius: round(c.radius, 3),
            target: { x: round(c.target.x, 3), y: round(c.target.y, 3), z: round(c.target.z, 3) }
        };
    }

    createScene() {
        const B = window.BABYLON;
        const scene = new B.Scene(this.engine);
        scene.useRightHandedSystem = true; // same handedness as Helix / WPF 3D
        scene.clearColor = B.Color4.FromHexString('#070b0eff');

        const d = DEFAULT_CAMERA;
        this.camera = new B.ArcRotateCamera('Camera', rad(d.alpha), rad(d.beta), d.radius,
            new B.Vector3(d.target.x, d.target.y, d.target.z), scene);
        this.camera.attachControl(this.canvas, true);
        this.camera.lowerRadiusLimit = 5;
        this.camera.upperRadiusLimit = 200;
        this.camera.wheelDeltaPercentage = 0.02;

        const hemi = new B.HemisphericLight('hemisphericLight', new B.Vector3(0, 1, 0), scene);
        hemi.intensity = 0.75;
        hemi.diffuse = B.Color3.FromHexString(this.secondaryColor);
        hemi.specular = new B.Color3(0.2, 0.2, 0.2);
        hemi.groundColor = B.Color3.FromHexString('#172226');

        const dir = new B.DirectionalLight('directionalLight', new B.Vector3(-1, -2, -1), scene);
        dir.position = new B.Vector3(10, 20, 10);
        dir.intensity = 0.55;

        // Root in Helix coordinates: Z up -> scene Y up, Helix units -> scene units.
        this.root = new B.TransformNode('Helix', scene);
        this.root.rotation.x = -Math.PI / 2;
        this.root.scaling = new B.Vector3(HELIX_TO_SCENE, HELIX_TO_SCENE, HELIX_TO_SCENE);

        // Compass base: a disc whose top face is the Helix z = 0 plane (GSServer's compass box).
        const compass = new B.StandardMaterial('Compass', scene);
        // The compass image is drawn inverted (light markings on a dark disc); see setHemisphere.
        compass.diffuseColor = new B.Color3(1, 1, 1);
        compass.emissiveColor = new B.Color3(0.04, 0.06, 0.06);
        compass.specularColor = new B.Color3(0.05, 0.05, 0.05);
        const faceUv = [];
        faceUv[1] = new B.Vector4(0, 0, 0, 0);
        const thickness = 1 / HELIX_TO_SCENE;
        this.compassBase = B.MeshBuilder.CreateCylinder('Compass', {
            height: thickness, diameter: 15 / HELIX_TO_SCENE, faceUV: faceUv, tessellation: 64
        }, scene);
        this.compassBase.material = compass;
        this.compassBase.parent = this.root;
        this.compassBase.rotation.x = Math.PI / 2; // cylinder axis along Helix Z
        this.compassBase.position.z = -thickness / 2;

        this.ground = this.createGround(scene);

        this.pierMaterial = new B.StandardMaterial('Pier', scene);
        this.pierMaterial.diffuseColor = B.Color3.FromHexString('#34474f');
        this.pierMaterial.specularColor = new B.Color3(0.1, 0.1, 0.1);

        // The moving part of the mount (the .obj model) hangs off this node.
        this.modelNode = new B.TransformNode('Model', scene);
        this.modelNode.parent = this.root;
        this.modelNode.rotationQuaternion = B.Quaternion.Identity();

        return scene;
    }

    /**
     * The ground: a large plane under the compass base with a tiled grid, fading out towards its
     * edge so it has no hard border.
     */
    createGround(scene) {
        const B = window.BABYLON;

        // One texture tile = GRID_MAJOR_EVERY x GRID_MAJOR_EVERY cells, major lines on its edges.
        const px = 512, step = px / GRID_MAJOR_EVERY;
        const grid = new B.DynamicTexture('GridTexture', { width: px, height: px }, scene, true);
        const ctx = grid.getContext();
        ctx.clearRect(0, 0, px, px);
        ctx.fillStyle = 'rgba(16, 24, 28, 0.45)'; // faint floor so the mount stands on something
        ctx.fillRect(0, 0, px, px);
        const line = (pos, width, color) => {
            ctx.fillStyle = color;
            ctx.fillRect(pos - width / 2, 0, width, px);
            ctx.fillRect(0, pos - width / 2, px, width);
        };
        for (let i = 1; i < GRID_MAJOR_EVERY; i++) line(i * step, 2, 'rgba(82, 183, 136, 0.28)');
        // Major lines straddle the tile edges, so each tile draws half of them.
        line(0, 6, 'rgba(82, 183, 136, 0.65)');
        line(px, 6, 'rgba(82, 183, 136, 0.65)');
        grid.update();
        grid.hasAlpha = true;
        grid.wrapU = grid.wrapV = B.Texture.WRAP_ADDRESSMODE;
        grid.uScale = grid.vScale = GRID_SIZE / (GRID_CELL * GRID_MAJOR_EVERY);
        grid.anisotropicFilteringLevel = 8; // keeps lines crisp at low viewing angles

        // Radial fade (not tiled): solid in the middle, gone at the edge.
        const fadePx = 256;
        const fade = new B.DynamicTexture('GridFade', { width: fadePx, height: fadePx }, scene, false);
        const fctx = fade.getContext();
        const gradient = fctx.createRadialGradient(fadePx / 2, fadePx / 2, 0, fadePx / 2, fadePx / 2, fadePx / 2);
        gradient.addColorStop(0, 'rgba(255, 255, 255, 1)');
        gradient.addColorStop(0.3, 'rgba(255, 255, 255, 1)');
        gradient.addColorStop(1, 'rgba(255, 255, 255, 0)');
        fctx.clearRect(0, 0, fadePx, fadePx);
        fctx.fillStyle = gradient;
        fctx.fillRect(0, 0, fadePx, fadePx);
        fade.update();
        fade.hasAlpha = true;

        const material = new B.StandardMaterial('Ground', scene);
        material.diffuseTexture = grid;
        material.useAlphaFromDiffuseTexture = true;
        material.opacityTexture = fade;
        // Unshaded: with lighting off the texture shows through the emissive color, so make it white.
        material.disableLighting = true;
        material.emissiveColor = new B.Color3(1, 1, 1);
        material.specularColor = new B.Color3(0, 0, 0);
        material.backFaceCulling = false;   // the camera can tilt below the ground

        const ground = B.MeshBuilder.CreateGround('Ground', { width: GRID_SIZE, height: GRID_SIZE }, scene);
        ground.position.y = GRID_Y;
        ground.material = material;
        ground.isPickable = false;
        return ground;
    }

    /** Shows or hides the ground grid. */
    setGrid(visible) {
        this.ground.setEnabled(!!visible);
    }

    /** Helix BoxVisual3D: Length = X, Width = Y, Height = Z, rotated about the X axis around a pivot. */
    addBeam(name, size, centre, angleDeg, pivot) {
        const B = window.BABYLON;
        const box = B.MeshBuilder.CreateBox(name, { width: size.x, height: size.y, depth: size.z }, this.scene);
        box.material = this.pierMaterial;
        box.parent = this.root;
        const rotation = B.Matrix.RotationX(rad(angleDeg));
        const offset = B.Vector3.TransformCoordinates(centre.subtract(pivot), rotation);
        box.position = offset.add(pivot);
        box.rotation.x = rad(angleDeg);
        this.pierMeshes.push(box);
    }

    /** Port of GSServer Model3DVM.LoadPierModel. */
    buildPier(mode, latitude) {
        const B = window.BABYLON;
        this.pierMeshes.forEach(m => m.dispose());
        this.pierMeshes = [];

        const h = PIER_HEIGHT, bw = BEAM_WIDTH, half = bw / 2;
        let lowerLength = h, lowerAngle = 90;
        let upperLength = 0, upperAngle = 90;

        if (mode === POLAR) {
            // Latitude-canted two-beam pier
            const pier = { x: 0, y: h - bw };
            upperLength = 0.8 * h;
            const lat = Math.abs(latitude);
            const len = upperLength - bw;
            const upper = { x: Math.cos(rad(lat)) * len, y: -Math.sin(rad(lat)) * len };
            upperAngle = lat - 90;
            const lower = { x: pier.x + upper.x, y: pier.y + upper.y };
            lowerLength = Math.hypot(lower.x, lower.y);
            // WPF Vector.AngleBetween(lower, pier): signed angle from lower to pier, degrees.
            const angleBetween = Math.atan2(lower.x * pier.y - lower.y * pier.x, lower.x * pier.x + lower.y * pier.y) * 180 / Math.PI;
            lowerAngle = 90 - angleBetween;
        } else if (mode !== ALT_AZ) {
            // German polar: vertical pier with a 90 degree pivot arm
            upperLength = PIER_PIVOT;
        }

        this.addBeam('LowerPierBeam', new B.Vector3(bw, lowerLength, bw),
            new B.Vector3(0, lowerLength / 2, half), lowerAngle, new B.Vector3(0, 0, half));
        if (upperLength > 0) {
            // Rotated about -X, so the angle is negated for a rotation about +X.
            this.addBeam('UpperPierBeam', new B.Vector3(bw, bw, upperLength),
                new B.Vector3(0, 0, h - upperLength / 2), -upperAngle, new B.Vector3(0, 0, h));
        }
    }

    /**
     * Loads a model and its pier. Calls can overlap (the model can change while one is downloading);
     * only the latest one is kept, and the old model stays on screen until the new one is ready.
     * mount: { mode: ASCOM alignment mode, latitude, northCompass }.
     * Returns 'Superseded' for a load that a newer one replaced.
     */
    async loadModel(url, mount) {
        const B = window.BABYLON;
        const load = ++this.loadCount;
        const mode = mount?.mode ?? GERMAN_POLAR;
        const latitude = Number.isFinite(mount?.latitude) ? mount.latitude : 0;

        this.setHemisphere(mount?.northCompass !== false);

        // Materials are set below, so don't request a .mtl file (avoids a 404).
        if (B.OBJFileLoader) B.OBJFileLoader.SKIP_MATERIALS = true;

        const slash = url.lastIndexOf('/') + 1;
        let result;
        try {
            result = await B.SceneLoader.ImportMeshAsync('', url.substring(0, slash), url.substring(slash), this.scene, null, '.obj');
        } catch (e) {
            console.error('Telescope model failed to load:', url, e);
            return `Error: ${e && e.message ? e.message : e}`;
        }
        if (load !== this.loadCount || this.scene.isDisposed) {
            result.meshes.forEach(m => m.dispose());
            return 'Superseded';
        }
        const meshes = result.meshes.filter(m => m.getTotalVertices() > 0);
        if (meshes.length === 0) {
            result.meshes.forEach(m => m.dispose());
            return 'Error: model has no meshes';
        }

        // Replace the previous model, its materials and the pier.
        this.modelMeshes.forEach(m => m.dispose());
        this.modelMeshes = [];
        this.modelMaterials.forEach(m => m.dispose());
        this.buildPier(mode, latitude);

        // As GSServer: object 0 is the OTA, 1 the counterweights (German polar) or a second OTA,
        // 2 the bar.
        const ota = new B.StandardMaterial('OTA', this.scene);
        ota.diffuseColor = B.Color3.FromHexString(this.primaryColor);
        ota.specularColor = new B.Color3(0.3, 0.3, 0.3);
        const weights = new B.StandardMaterial('Weights', this.scene);
        weights.diffuseColor = new B.Color3(0.25, 0.27, 0.28);
        const bar = new B.StandardMaterial('Bar', this.scene);
        bar.diffuseColor = new B.Color3(0.86, 0.86, 0.84);
        this.modelMaterials = [ota, weights, bar];
        const germanPolar = mode === GERMAN_POLAR;

        meshes.forEach((mesh, i) => {
            mesh.parent = this.modelNode;
            mesh.material = i === 1 && germanPolar ? weights : i === 2 ? bar : ota;
            this.modelMeshes.push(mesh);
        });
        result.meshes.filter(m => m.getTotalVertices() === 0).forEach(m => m.dispose());

        this.applyPose();
        return 'Model loaded successfully';
    }

    /**
     * GSServer's model transform: XAxisOffset (about Z), YAxisOffset (about Y),
     * ZAxisOffset (about X), centred on (0, yCentre, 525).
     */
    setPose(xAxisOffset, yAxisOffset, zAxisOffset, yAxisCentre) {
        this.pose = { x: xAxisOffset, y: yAxisOffset, z: zAxisOffset, yCentre: yAxisCentre };
        this.applyPose();
    }

    applyPose() {
        const B = window.BABYLON;
        const { x, y, z, yCentre } = this.pose;
        const centre = new B.Vector3(0, yCentre, ROTATION_CENTRE_Z);
        const toCentre = B.Matrix.Translation(-centre.x, -centre.y, -centre.z);
        const back = B.Matrix.Translation(centre.x, centre.y, centre.z);
        const about = r => toCentre.multiply(r).multiply(back);

        // Row vectors, applied left to right like a WPF Transform3DGroup.
        const m = B.Matrix.Translation(1, 0, 0)
            .multiply(about(B.Matrix.RotationZ(rad(x))))
            .multiply(about(B.Matrix.RotationY(rad(y))))
            .multiply(about(B.Matrix.RotationX(rad(z))));

        const scale = new B.Vector3(), rotation = new B.Quaternion(), position = new B.Vector3();
        m.decompose(scale, rotation, position);
        this.modelNode.position = position;
        this.modelNode.rotationQuaternion = rotation;
    }

    setHemisphere(isNorthern) {
        if (this.northern === isNorthern) return;
        this.northern = isNorthern;

        const B = window.BABYLON;
        const url = assetBase + (isNorthern ? 'compassN.png' : 'compassS.png');
        const request = ++this.compassRequest;
        const image = new Image();
        image.onload = () => {
            if (this.scene.isDisposed || request !== this.compassRequest) return;
            // The compass image is dark markings on a transparent background. Draw it inverted
            // (light markings) over the panel color, so it reads as a dark disc.
            const size = 1024;
            const texture = new B.DynamicTexture('CompassTexture', { width: size, height: size }, this.scene, true);
            const ctx = texture.getContext();
            ctx.fillStyle = '#10181c';
            ctx.fillRect(0, 0, size, size);
            // The right-handed scene shows the cap texture mirrored; draw it mirrored so the
            // letters read correctly and E / W are on the right sides.
            ctx.save();
            ctx.translate(size, 0);
            ctx.scale(-1, 1);
            ctx.filter = 'invert(1)';
            ctx.drawImage(image, 0, 0, size, size);
            ctx.restore();
            texture.update();
            const old = this.compassBase.material.diffuseTexture;
            this.compassBase.material.diffuseTexture = texture;
            if (old) old.dispose();
        };
        image.onerror = () => {
            // Allow a retry on the next load.
            if (request === this.compassRequest) this.northern = undefined;
        };
        image.src = url;
    }

    dispose() {
        this.resizeObserver.disconnect();
        this.engine.stopRenderLoop();
        this.scene.dispose();
        this.engine.dispose();
    }
}

// ===== Blazor interop (one viewer per page) =====

let viewer = null;

/** options: { camera: { alpha, beta (degrees), radius, target: { x, y, z } } | null, primaryColor, grid } */
export function init(canvasId, options) {
    dispose();
    const canvas = document.getElementById(canvasId);
    if (!canvas || !window.BABYLON) return false;
    viewer = new TelescopeViewer(canvas, options || {});
    return true;
}

/** url is relative to the page; mount: { mode (0 Alt-Az, 1 Polar, 2 German polar), latitude, northCompass } */
export function loadModel(url, mount) {
    if (!viewer) return 'Error: viewer not initialised';
    return viewer.loadModel(new URL(url, document.baseURI).href, mount);
}

export function setPose(xAxisOffset, yAxisOffset, zAxisOffset, yAxisCentre) {
    if (viewer) viewer.setPose(xAxisOffset, yAxisOffset, zAxisOffset, yAxisCentre);
}

export function getCameraState() {
    return viewer ? viewer.getCamera() : null;
}

export function dispose() {
    if (!viewer) return;
    viewer.dispose();
    viewer = null;
}

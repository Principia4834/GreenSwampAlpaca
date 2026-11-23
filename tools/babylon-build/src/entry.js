// Entry point for the tree-shaken Babylon.js bundle. It exposes only what telescopeViewer.js and
// telescopeAssembly.js use through window.BABYLON (const B = window.BABYLON).
// When those scripts use a new Babylon class, import it here and add it to window.BABYLON, then run `npm run build`.
import { Engine } from '@babylonjs/core/Engines/engine.js';
import '@babylonjs/core/Engines/Extensions/engine.dynamicTexture.js'; // DynamicTexture (star field, cardinal labels)
import { Scene } from '@babylonjs/core/scene.js';
import { Vector3, Vector4, Quaternion, Matrix } from '@babylonjs/core/Maths/math.vector.js';
import { Color3, Color4 } from '@babylonjs/core/Maths/math.color.js';
import { ArcRotateCamera } from '@babylonjs/core/Cameras/arcRotateCamera.js';
import { HemisphericLight } from '@babylonjs/core/Lights/hemisphericLight.js';
import { DirectionalLight } from '@babylonjs/core/Lights/directionalLight.js';
import { StandardMaterial } from '@babylonjs/core/Materials/standardMaterial.js';
import { Texture } from '@babylonjs/core/Materials/Textures/texture.js';
import { DynamicTexture } from '@babylonjs/core/Materials/Textures/dynamicTexture.js';
import { CreateBox } from '@babylonjs/core/Meshes/Builders/boxBuilder.js';
import { CreateCylinder } from '@babylonjs/core/Meshes/Builders/cylinderBuilder.js';
import { CreateGround } from '@babylonjs/core/Meshes/Builders/groundBuilder.js';
import { TransformNode } from '@babylonjs/core/Meshes/transformNode.js';
import { SceneLoader } from '@babylonjs/core/Loading/sceneLoader.js';
import { OBJFileLoader } from '@babylonjs/loaders/OBJ/objFileLoader.js'; // the only model format the app loads (.obj)

window.BABYLON = {
    Engine, Scene, Vector3, Vector4, Quaternion, Matrix, Color3, Color4,
    ArcRotateCamera, HemisphericLight, DirectionalLight,
    StandardMaterial, Texture, DynamicTexture,
    MeshBuilder: { CreateBox, CreateCylinder, CreateGround },
    TransformNode, SceneLoader, OBJFileLoader
};

// Procedural telescope assembly ported from GS.Point3DWeb's model3d.js.
const rad = deg => deg * Math.PI / 180;
// Assembled mount (EXPERIMENTAL; loadAssembly): a support, a mount and an OTA, built here from simple shapes (the OTA
// can also be an .obj model's tube). Millimetres throughout.
const ASSEMBLY_SCALE = 0.6;             // Helix units per millimetre: the tripod's feet well inside the compass
const ASSEMBLY_COLUMN_TOP = 1248;       // top of the full-size tripod's centre column (and the piers), where a mount sits
// The supports (the AssemblySupport names in lower case): the tripod (buildTripod) at a scale (its top scales with
// it), or a pier (buildPier) of a diameter in millimetres (pier tops are at ASSEMBLY_COLUMN_TOP).
const ASSEMBLY_SUPPORTS = {
    // thick: the legs' thickness on top of the scale, so a bigger tripod is sturdier, not just taller.
    smalltripod: { tripod: 0.82, thick: 0.9 },
    mediumtripod: { tripod: 1, thick: 1.25 },
    largetripod: { tripod: 1.2, thick: 1.6 },
    smallpier: { pier: 152 },      // 6 inches
    mediumpier: { pier: 254 },     // 10 inches
    largepier: { pier: 305 }       // 12 inches
};
// The OTA: an .obj model's tube (its first object), scaled, its lowest point sat on the saddle. The .obj models
// put their Dec axis at y = -102; each tube keeps its own place along it.
const ASSEMBLY_OTA = { scale: 1.5, decAxisY: -102 };   // scale: unless the mount sets its own otaScale
// The OTAs an assembled model can carry: the .obj models (German polar) whose first object is the tube.
const ASSEMBLY_OTAS = ['Refractor', 'Reflector', 'SchmidtCassegrain', 'RitcheyChretien', 'RitcheyChretienTruss'];
// OTAs built here from simple shapes instead (the AssemblyOta names): each one's builder.
const ASSEMBLY_BUILT_OTAS = {
    Newtonian: (...a) => buildNewtonian(...a),
    MaksutovCassegrain: (...a) => buildMaksutov(...a)
};
// The mounts an assembled model can have: each one's builders.
//
// Each German equatorial mount's measurements (millimetres) and builders:
//   hinge       the latitude hinge, on the polar axis, above the support's top (the builders get it as mount.top)
//   decAlongRa  where the Dec axis crosses the polar axis, up the polar axis from the hinge
//   saddleTop   the top of the saddle's jaws, up the Dec axis from the polar axis (the OTA sits on it)
//   otaScale    how big the OTA is drawn on this mount (the .obj tube's scale), to suit the mount's size
//   base(s, holder, m, mount)          the fixed mount base, in the holder's frame
//   head(s, { latitude, ra, dec }, m, mount)  the parts on each joint, in that joint's frame
// To add a mount: an entry here (its key is the AssemblyMount name in lower case), its builders, and the
// AssemblyMount value and display name in Model3D.cs.
// Centre-balanced mounts: how far their counterweight bar sits along the polar axis toward the pole, from the Dec
// axis, so the bar and weight clear the pier through a full turn of RA at any latitude from 10° to 70° (about 70 mm
// to spare for the CEM40, 90 mm for the CEM70).
const CEM40_BAR_Y = -140;
const CEM70_BAR_Y = -160;
// The CQ350 Pro is laid out the same way: its bar hangs from an arm at the RA drum's north face (about 50 mm to spare).
const CQ350_BAR_Y = -130;

const ASSEMBLY_MOUNTS = {
    eq6rpro: { hinge: 142, decAlongRa: 230, saddleTop: 222, otaScale: 1.5, base: (...a) => buildEq6rBase(...a), head: (...a) => buildEq6rHead(...a) },
    heq5pro: { hinge: 116, decAlongRa: 176, saddleTop: 168, otaScale: 1.15, base: (...a) => buildHeq5Base(...a), head: (...a) => buildHeq5Head(...a) },
    eq8rpro: { hinge: 212, decAlongRa: 300, saddleTop: 314, otaScale: 2.1, base: (...a) => buildEq8Base(...a), head: (...a) => buildEq8Head(...a) },
    am5: { hinge: 96, decAlongRa: 150, saddleTop: 190, otaScale: 1.05, base: (...a) => buildAm5Base(...a), head: (...a) => buildAm5Head(...a) },
    // Centre-balanced: the Dec axis crosses the polar axis just above the hinge, so the load sits over the pier.
    cem70: { hinge: 172, decAlongRa: 40, saddleTop: 300, otaScale: 1.75, base: (...a) => buildCem70Base(...a), head: (...a) => buildCem70Head(...a) },
    // A fork: the OTA hangs between two arms on the Dec axis (centred on it, no saddle or counterweights), on a
    // wedge for a polar mount or straight up for Alt-Az.
    fork: { hinge: 200, decAlongRa: 300, saddleTop: 0, otaScale: 0.8, fork: true, base: (...a) => buildForkBase(...a), head: (...a) => buildForkHead(...a) },
    cem40: { hinge: 124, decAlongRa: 30, saddleTop: 216, otaScale: 1.35, base: (...a) => buildCem40Base(...a), head: (...a) => buildCem40Head(...a) },
    cq350pro: { hinge: 180, decAlongRa: 20, saddleTop: 248, otaScale: 1.8, base: (...a) => buildCq350Base(...a), head: (...a) => buildCq350Head(...a) },
    eqm35pro: { hinge: 100, decAlongRa: 150, saddleTop: 140, otaScale: 0.95, base: (...a) => buildEqm35Base(...a), head: (...a) => buildEqm35Head(...a) },
    wave150ipro: { hinge: 96, decAlongRa: 150, saddleTop: 140, otaScale: 1.2, base: (...a) => buildWave150Base(...a), head: (...a) => buildWave150Head(...a) }
};

// Alignment modes (ASCOM AlignmentModes)
const ALT_AZ = 0, POLAR = 1;
class AssemblyShapes {
    constructor(B, scene, keep) {
        this.B = B;
        this.scene = scene;
        this.keep = keep;
        this.count = 0;
        this.labelMaterials = [];   // the labels' own materials (with their textures), disposed with the model
        this.otaSize = 1;           // the OTA's size against the mount's own: the counterweights follow it
    }

    add(mesh, parent, material) {
        mesh.parent = parent;
        mesh.material = material;
        this.keep(mesh);
        return mesh;
    }

    /** A box from x0..x1, y0..y1, z0..z1. */
    box(parent, material, [x0, x1], [y0, y1], [z0, z1]) {
        const m = this.B.MeshBuilder.CreateBox(`Eq6r${this.count++}`,
            { width: Math.abs(x1 - x0), height: Math.abs(y1 - y0), depth: Math.abs(z1 - z0) }, this.scene);
        m.position = new this.B.Vector3((x0 + x1) / 2, (y0 + y1) / 2, (z0 + z1) / 2);
        return this.add(m, parent, material);
    }

    /**
     * A cylinder along `axis` ('x', 'y' or 'z') from `from` to `to`, centred on (a, b) across it (the other two
     * coordinates in x, y, z order). `top` narrows it to a cone toward `to`.
     */
    cylinder(parent, material, axis, diameter, from, to, [a, b] = [0, 0], { top = diameter, tessellation = 40 } = {}) {
        const B = this.B;
        const m = B.MeshBuilder.CreateCylinder(`Eq6r${this.count++}`,
            { height: Math.abs(to - from), diameterBottom: diameter, diameterTop: top, tessellation }, this.scene);
        const mid = (from + to) / 2, up = to >= from ? 1 : -1;
        // A Babylon cylinder stands along its own Y, top at +Y: turn that onto the axis, top toward `to`.
        if (axis === 'x') {
            m.position = new B.Vector3(mid, a, b);
            m.rotation.z = -up * Math.PI / 2;
        } else if (axis === 'y') {
            m.position = new B.Vector3(a, mid, b);
            if (up < 0) m.rotation.x = Math.PI;
        } else {
            m.position = new B.Vector3(a, b, mid);
            m.rotation.x = up * Math.PI / 2;
        }
        return this.add(m, parent, material);
    }

    /** A rod (a cylinder) from point `p0` to point `p1`, each [x, y, z], at any angle. */
    rod(parent, material, diameter, p0, p1, { top = diameter, tessellation = 24 } = {}) {
        const B = this.B;
        const a = new B.Vector3(...p0), b = new B.Vector3(...p1), d = b.subtract(a), length = d.length();
        const m = B.MeshBuilder.CreateCylinder(`Eq6r${this.count++}`,
            { height: length, diameterBottom: diameter, diameterTop: top, tessellation }, this.scene);
        m.position = a.add(b).scale(0.5);
        // Turn the cylinder's own axis (+Y) onto the rod's direction.
        const dir = d.scale(1 / length), axis = B.Vector3.Cross(new B.Vector3(0, 1, 0), dir), s = axis.length();
        m.rotationQuaternion = s < 1e-9
            ? (dir.y > 0 ? B.Quaternion.Identity() : B.Quaternion.RotationAxis(new B.Vector3(1, 0, 0), Math.PI))
            : B.Quaternion.RotationAxis(axis.scale(1 / s), Math.acos(Math.max(-1, Math.min(1, dir.y))));
        return this.add(m, parent, material);
    }

    /**
     * A box with rounded edges: the four edges along `axis` ('x', 'y' or 'z') rounded to radius `r`, so a casting
     * reads as a casting rather than a crate. Extents as for box.
     */
    rbox(parent, material, X, Y, Z, r, axis = 'z') {
        const R = { x: X, y: Y, z: Z };
        const [a, b] = { x: ['y', 'z'], y: ['x', 'z'], z: ['x', 'y'] }[axis];   // the rounded cross-section
        const lo = k => Math.min(...R[k]), hi = k => Math.max(...R[k]);
        const box = (ra, rb) => {
            const q = { ...R, [a]: ra, [b]: rb };
            this.box(parent, material, q.x, q.y, q.z);
        };
        box([lo(a) + r, hi(a) - r], [lo(b), hi(b)]);
        box([lo(a), hi(a)], [lo(b) + r, hi(b) - r]);
        for (const ca of [lo(a) + r, hi(a) - r]) {
            for (const cb of [lo(b) + r, hi(b) - r]) {
                this.cylinder(parent, material, axis, 2 * r, lo(axis), hi(axis), [ca, cb], { tessellation: 16 });
            }
        }
    }

    /**
     * A dovetail saddle along the polar axis (Y), standing on the Dec axis (Z): a rounded body `w` either side of
     * the axis and `len` either way along it, from `base` to `mid`, and two jaws from `mid` to `top`: a fixed one on
     * the west edge and a moving one `inset` in from the east edge, each `jaw` wide.
     */
    saddle(parent, material, { w, len, base, mid, top, jaw = 16, inset = 6, r = 6 }) {
        this.rbox(parent, material, [-w, w], [-len, len], [base, mid], r, 'y');
        this.box(parent, material, [-w, -w + jaw], [-len, len], [mid, top]);
        this.box(parent, material, [w - inset - jaw, w - inset], [-len, len], [mid, top]);
    }

    /** Star knobs along `axis` from `from` to `to`, one at each of `places` (each across the axis, as for knob). */
    knobs(parent, m, axis, from, to, places, knob = m.black) {
        for (const across of places) this.knob(parent, m, axis, from, to, across, knob);
    }

    /**
     * A printed label (a maker's name or a model): text on a thin plate at `at`, facing along `normal` ('x', '-x',
     * 'y', '-y', 'z' or '-z'), its text upright along `up`, `width` by `height` millimetres. Lines split on '\n'; a
     * line starting '~' is drawn in the accent colour. Seen from the front only.
     */
    label(parent, text, { at, normal, up, width, height, color = '#f2f2ee', accent = '#2fbf55', weight = 700 }) {
        const B = this.B;
        const axis = d => {
            const v = { x: [1, 0, 0], y: [0, 1, 0], z: [0, 0, 1] }[d.replace('-', '')].map(c => d.startsWith('-') ? -c : c);
            return new B.Vector3(...v);
        };
        const px = 512, ph = Math.max(16, Math.round(px * height / width));
        const texture = new B.DynamicTexture(`Label${this.count}`, { width: px, height: ph }, this.scene, true);
        const ctx = texture.getContext();
        ctx.clearRect(0, 0, px, ph);
        // The plate maps the texture's rows upside down: draw the text mirrored top to bottom to cancel that.
        ctx.save();
        ctx.translate(0, ph);
        ctx.scale(1, -1);
        const lines = text.split('\n');
        const size = Math.min(ph / lines.length * 0.78, px / Math.max(...lines.map(l => l.length)) * 1.7);
        ctx.textAlign = 'center';
        ctx.textBaseline = 'middle';
        lines.forEach((line, i) => {
            const accented = line.startsWith('~');
            ctx.font = `${weight} ${Math.round(size)}px Barlow, "Segoe UI", system-ui, sans-serif`;
            ctx.fillStyle = accented ? accent : color;
            ctx.fillText(accented ? line.slice(1) : line, px / 2, (i + 0.5) * ph / lines.length);
        });
        ctx.restore();
        texture.update();
        texture.hasAlpha = true;
        const material = new B.StandardMaterial(`Label${this.count}`, this.scene);
        material.diffuseTexture = texture;
        material.useAlphaFromDiffuseTexture = true;
        material.emissiveColor = new B.Color3(0.35, 0.35, 0.35);
        material.specularColor = new B.Color3(0, 0, 0);
        this.labelMaterials.push(material);
        // A ground plate lies in its X-Z plane facing +Y: turn it to face `normal` with the text's top toward `up`.
        const plate = B.MeshBuilder.CreateGround(`Label${this.count++}`, { width, height }, this.scene);
        const n = axis(normal), u = axis(up), right = B.Vector3.Cross(u, n);
        const turn = B.Matrix.FromValues(
            right.x, right.y, right.z, 0,
            n.x, n.y, n.z, 0,
            -u.x, -u.y, -u.z, 0,
            0, 0, 0, 1);
        plate.rotationQuaternion = B.Quaternion.FromRotationMatrix(turn);
        plate.position = new B.Vector3(...at);
        return this.add(plate, parent, material);
    }

    /** A star knob along `axis`: a steel stem from `from`, the knob (black unless `knob` says) at `to`. */
    knob(parent, m, axis, from, to, across, knob = m.black) {
        const toward = to >= from ? 1 : -1;
        this.cylinder(parent, m.steel, axis, 10, from, to - toward * 12, across, { tessellation: 12 });
        this.cylinder(parent, knob, axis, 34, to - toward * 12, to, across, { tessellation: 5 });
        this.cylinder(parent, knob, axis, 18, to, to + toward * 4, across, { tessellation: 12 });
    }
}

/**
 * The full-size tripod (holder frame, millimetres, Z up; scaled for the small and large tripods): a hub on three
 * splayed legs (an upper and a lower section each, a hinge at the top and a foot), a spreader with an accessory
 * tray between the legs, and a centre column up to ASSEMBLY_COLUMN_TOP, where a mount's base stands.
 */
function buildTripod(s, node, m, thick = 1) {
    const at = (r, az, z) => [r * Math.cos(az * Math.PI / 180), r * Math.sin(az * Math.PI / 180), z];
    const t = thick, core = Math.min(thick, 1.15);     // the centre parts grow less, to keep the weights clear
    s.cylinder(node, m.tripod, 'z', 156 * core, 1008, 1072);                        // hub
    s.cylinder(node, m.tripod, 'z', 124 * core, 1071, 1089);                        // mount plate
    s.cylinder(node, m.tripod, 'z', 90 * core, 1072, ASSEMBLY_COLUMN_TOP);          // centre column
    s.cylinder(node, m.tripod, 'z', 96 * t, 625, 675);                              // spreader collar
    s.cylinder(node, m.tripod, 'z', 380, 619, 631, [0, 0], { tessellation: 48 });  // accessory tray
    for (const az of [90, 212, 328]) {                                              // one leg south, two north
        s.rod(node, m.tripod, 64 * t, at(36, az, 1060), at(62, az, 984));           // hinge
        s.rod(node, m.tripod, 46 * t, at(30, az, 1030), at(400, az, 400));          // upper section
        s.rod(node, m.tripod, 28 * t, at(350, az, 480), at(575, az, 60));           // lower section
        const [x, y] = at(572, az, 0);
        s.cylinder(node, m.tripod, 'z', 76 * t, 11, 49, [x, y]);                    // foot
        s.rod(node, m.tripod, 30 * t, at(48, az, 650), at(320, az, 650));           // spreader arm
    }
}

/**
 * A pier for an assembled model (holder frame, millimetres, Z up), `diameter` across its whole height: a foot plate
 * on the ground, a flared foot, the pier tube up to a top plate of the same width, and the steel mount adapter at
 * `top`. On the large (12-inch) pier the weights of some small, low mounts (CEM40, CQ350 Pro, EQM-35 Pro) reach it,
 * as they would on a real one without an extension.
 */
function buildPier(s, holder, m, top, diameter) {
    const d = diameter;
    s.cylinder(holder, m.tripod, 'z', Math.max(460, d * 1.9), 0, 24, [0, 0], { tessellation: 6 });   // foot plate
    s.cylinder(holder, m.tripod, 'z', d * 1.3, 24, 70, [0, 0], { top: d });         // flared foot
    s.cylinder(holder, m.tripod, 'z', d, 70, top - 40);                             // pier tube
    s.cylinder(holder, m.tripod, 'z', d, top - 40, top - 22);                       // top plate
    s.cylinder(holder, m.tripod, 'z', d + 4, top - 30, top - 26);                   // the plate's seam
    for (const a of [30, 150, 270]) {                                               // levelling bolts in the plate
        const r = d / 2 - 14, x = r * Math.cos(a * Math.PI / 180), y = r * Math.sin(a * Math.PI / 180);
        s.cylinder(holder, m.steel, 'z', 14, top - 22, top - 10, [x, y], { tessellation: 12 });
    }
    s.cylinder(holder, m.steel, 'z', 150, top - 22, top);                           // mount adapter
}

/**
 * The EQ6-R Pro's mount base, fixed on the tripod's centre column (holder frame, millimetres, Z up, pole -Y):
 * the white azimuth drum, the central latitude tower with a silver hinge cap each side, the latitude rod and
 * L-lever on the south side, the latitude lock knob on the north side, and the azimuth adjuster knobs.
 */
function buildEq6rBase(s, holder, m, mount) {
    const z0 = mount.top, hinge = z0 + mount.hinge;
    // Azimuth drum on the column.
    s.cylinder(holder, m.white, 'z', 150, z0, z0 + 44);
    s.cylinder(holder, m.white, 'z', 136, z0 + 44, z0 + 56);
    // Latitude tower: a white upright under the RA body, rounded over the hinge, a silver cap on each side.
    s.box(holder, m.white, [-44, 44], [-58, 58], [z0 + 56, hinge]);
    s.cylinder(holder, m.white, 'x', 116, -44, 44, [0, hinge]);
    for (const side of [-1, 1]) {
        s.cylinder(holder, m.white, 'x', 70, side * 44, side * 48, [0, hinge]);
        s.cylinder(holder, m.steel, 'x', 46, side * 48, side * 54, [0, hinge], { top: 34 });
    }
    // Latitude rod on the south side, with its black L-lever; the lock knob on the north side.
    s.box(holder, m.white, [-20, 20], [58, 80], [z0 + 60, z0 + 92]);
    s.cylinder(holder, m.steel, 'y', 12, 80, 190, [0, z0 + 76]);
    s.cylinder(holder, m.black, 'y', 22, 190, 204, [0, z0 + 76], { tessellation: 16 });
    s.box(holder, m.black, [-6, 6], [198, 210], [z0 + 20, z0 + 82]);
    s.knob(holder, m, 'y', -58, -92, [0, hinge - 40]);
    // Azimuth adjusters: a knob out each side at the tower's foot.
    for (const side of [-1, 1]) s.knob(holder, m, 'x', side * 44, side * 96, [0, z0 + 66]);
}

/**
 * The EQ6-R Pro's head on the three joints (each joint's frame, millimetres; home position: the polar axis level
 * along -Y toward the pole, the Dec axis along +Z):
 *   latitude: the RA body (tilts, doesn't turn in RA): housing, ribbed polar scope cover, carry handle on top,
 *             RA clutch lever, the green RA setting circle
 *   ra:       the head: one rounded white block with the control panel on its west face, a nose underneath
 *             with the counterweight shaft and two white weights, the green Dec setting circle
 *   dec:      the puck: a short white body, the Dec clutch lever and a white saddle with star knobs
 *             (the OTA is added after)
 */
function buildEq6rHead(s, j, m, mount) {
    const D = mount.decAlongRa, saddleTop = mount.saddleTop;
    const junction = -(D - 92);   // where the RA body meets the head, in the latitude joint's frame

    // RA body (latitude joint; the hinge is at the origin, on the polar axis).
    s.cylinder(j.latitude, m.white, 'y', 132, 150, junction);             // housing
    s.cylinder(j.latitude, m.white, 'y', 124, 150, 164);                  // step to the cover
    for (let y = 164; y < 212; y += 8) {                                  // ribbed polar scope cover
        s.cylinder(j.latitude, m.white, 'y', 118, y, y + 5, [0, 0], { tessellation: 32 });
        s.cylinder(j.latitude, m.white, 'y', 110, y + 5, y + 8, [0, 0], { tessellation: 32 });
    }
    s.cylinder(j.latitude, m.white, 'y', 110, 212, 226, [0, 0], { top: 70 });
    s.cylinder(j.latitude, m.green, 'y', 140, junction + 14, junction);   // green RA setting circle
    // Carry handle on top: two posts and a grip along the polar axis.
    for (const y of [20, 112]) s.box(j.latitude, m.white, [-12, 12], [y - 9, y + 9], [60, 104]);
    s.box(j.latitude, m.white, [-13, 13], [6, 126], [100, 118]);
    // RA clutch lever on the east side.
    s.cylinder(j.latitude, m.black, 'x', 20, 66, 80, [-50, 0], { tessellation: 16 });
    s.box(j.latitude, m.black, [76, 86], [-56, -44], [0, 58]);

    // Head (RA joint; origin where the Dec axis crosses the polar axis): one big block, its edges rounded.
    s.rbox(j.ra, m.white, [-94, 94], [-102, 104], [-96, 96], 16);
    s.rbox(j.ra, m.white, [-70, 70], [-78, 80], [96, 112], 12);           // its top, stepped in
    s.rbox(j.ra, m.white, [-70, 70], [-78, 80], [-112, -96], 12);         // and bottom
    s.cylinder(j.ra, m.white, 'y', 138, 92, 100);                         // collar meeting the RA body
    // Control panel on the west face: a raised panel, power switch, ports and two grey caps.
    s.rbox(j.ra, m.white, [-98, -94], [-70, 72], [-74, 84], 8, 'x');
    s.box(j.ra, m.black, [-102, -98], [-30, -16], [44, 62]);
    for (const [y, z, d] of [[-46, -10, 14], [-24, -10, 14], [-2, -10, 12], [24, 20, 18]]) {
        s.cylinder(j.ra, m.black, 'x', d, -98, -102, [y, z], { tessellation: 16 });
    }
    for (const [y, z] of [[-60, -86], [62, 92]]) s.cylinder(j.ra, m.grey, 'x', 26, -90, -100, [y, z], { tessellation: 20 });
    // Nose underneath, the counterweight shaft's lock knob, the shaft and its end stop.
    s.cylinder(j.ra, m.white, 'z', 96, -112, -150);
    s.cylinder(j.ra, m.white, 'z', 96, -150, -172, [0, 0], { top: 56 });
    s.knob(j.ra, m, 'x', 40, 80, [0, -140]);
    s.cylinder(j.ra, m.steel, 'z', 20, -172, -560);
    s.cylinder(j.ra, m.steel, 'z', 34, -560, -574);
    // Two white weights with black lock knobs.
    for (const z of [-340, -420]) {
        s.cylinder(j.ra, m.white, 'z', 124, z + 30, z - 30, [0, 0], { tessellation: 48 });
        s.knob(j.ra, m, 'x', 62, 92, [0, z]);
    }
    s.cylinder(j.ra, m.green, 'z', 120, 112, 124);                        // green Dec setting circle
    s.label(j.ra, 'Sky-Watcher\nEQ6-R PRO', { at: [94.5, 0, 24], normal: 'x', up: 'z', width: 150, height: 56, color: '#25292b' });

    // Puck (Dec joint): a short white body, the Dec clutch lever, and the saddle along the polar axis.
    s.cylinder(j.dec, m.white, 'z', 112, 124, 168);
    s.cylinder(j.dec, m.black, 'x', 20, 56, 70, [0, 146], { tessellation: 16 });
    s.box(j.dec, m.black, [66, 76], [-6, 6], [146, 200]);
    s.saddle(j.dec, m.white, { w: 58, len: 104, base: 168, mid: 206, top: saddleTop, jaw: 18 });
    s.knobs(j.dec, m, 'x', 52, 92, [[-52, 214], [52, 214]]);           // clamp knobs, east side
    s.knob(j.dec, m, 'x', -58, -92, [70, 190]);                           // and one on the west side
}

/**
 * Counterweights below the head (RA joint's frame): a shaft from `from` down to `to`, an end stop, and weights
 * centred at `at`, each with a lock knob. The shaft runs down the Dec axis, or at `y` along the polar axis from it
 * (negative: toward the pole), as a centre-balanced mount's does to clear its pier.
 */
function buildWeights(s, ra, m, { from, to, shaft, stop, weight, thick, at, material, knob = m.black, y = 0 }) {
    s.cylinder(ra, m.steel, 'z', shaft, from, to, [0, y]);
    s.cylinder(ra, m.steel, 'z', stop, to, to - 14, [0, y]);
    // The weights slide out along the shaft for a bigger OTA and in for a smaller one (the OTA size on the card),
    // as they would to balance it, staying on the shaft. A bar beside the pier (y off the Dec axis) only slides
    // them out: moving in would bring them against the pier.
    const mean = at.reduce((a, b) => a + b, 0) / at.length, reach = from - mean;
    let shift = reach * 0.45 * ((s.otaSize ?? 1) - 1);
    const out = Math.min(...at) - thick / 2 - (to + 2);
    const inward = y !== 0 ? 0 : (from - 4) - (Math.max(...at) + thick / 2);
    shift = Math.max(-inward, Math.min(out, shift));
    for (const z of at.map(z => z - shift)) {
        s.cylinder(ra, material, 'z', weight, z + thick / 2, z - thick / 2, [0, y], { tessellation: 48 });
        s.knob(ra, m, 'x', weight / 2 - 4, weight / 2 + 30, [y, z], knob);
    }
}

/**
 * Sky-Watcher HEQ5 Pro: the EQ6-R's smaller sibling. White; a round black badge on the latitude tower, silver
 * latitude T-bolts front and back, green bands on the RA housing and the head, a narrow Vixen saddle, two white
 * weights with black T knobs.
 */
function buildHeq5Base(s, holder, m, mount) {
    const z0 = mount.top, hinge = z0 + mount.hinge;
    s.cylinder(holder, m.white, 'z', 124, z0, z0 + 34);
    s.box(holder, m.white, [-34, 34], [-44, 44], [z0 + 34, hinge]);
    s.cylinder(holder, m.white, 'x', 92, -34, 34, [0, hinge]);
    for (const side of [-1, 1]) {
        s.cylinder(holder, m.black, 'x', 64, side * 34, side * 38, [0, hinge]);          // badge
        s.label(holder, 'Sky-Watcher\nHEQ5 PRO', { at: [side * 38.5, 0, hinge - 12], normal: side < 0 ? '-x' : 'x', up: 'z', width: 50, height: 22 });
    }
    for (const side of [-1, 1]) {                                                       // latitude T-bolts
        const y1 = side * 112, z = z0 + 64;
        s.cylinder(holder, m.steel, 'y', 10, side * 44, y1, [0, z]);
        s.cylinder(holder, m.steel, 'x', 8, -26, 26, [y1, z]);
    }
    s.knob(holder, m, 'x', 34, 70, [-30, z0 + 50]);                                     // azimuth
}

function buildHeq5Head(s, j, m, mount) {
    const D = mount.decAlongRa, junction = -(D - 70);
    // RA body (latitude joint).
    s.cylinder(j.latitude, m.white, 'y', 104, 112, junction);
    s.cylinder(j.latitude, m.green, 'y', 108, 100, 110);
    s.cylinder(j.latitude, m.white, 'y', 104, 112, 126, [0, 0], { top: 78 });
    s.cylinder(j.latitude, m.black, 'y', 38, 126, 138);                                 // polar scope cap
    s.cylinder(j.latitude, m.green, 'y', 110, junction + 10, junction);
    // Head (RA joint): a tall white block, its edges rounded, grey caps on its east face.
    s.rbox(j.ra, m.white, [-67, 67], [-71, 79], [-96, 92], 14);
    s.cylinder(j.ra, m.white, 'y', 108, 70, 78);
    for (const [y, z] of [[-30, -60], [30, 50]]) s.cylinder(j.ra, m.grey, 'x', 22, 64, 72, [y, z], { tessellation: 20 });
    s.box(j.ra, m.black, [-80, -67], [-8, 8], [-10, 34]);                               // RA clutch
    s.cylinder(j.ra, m.white, 'z', 82, 92, 120);                                        // neck
    s.cylinder(j.ra, m.green, 'z', 86, 100, 108);
    s.cylinder(j.ra, m.white, 'z', 70, -96, -124);                                      // nose
    s.cylinder(j.ra, m.white, 'z', 70, -124, -136, [0, 0], { top: 44 });
    s.knob(j.ra, m, 'x', 30, 62, [0, -116]);
    buildWeights(s, j.ra, m, { from: -136, to: -430, shaft: 18, stop: 30, weight: 110, thick: 46, at: [-290, -340], material: m.white });
    // Puck (Dec joint): a short neck and a narrow Vixen saddle with two knobs.
    s.cylinder(j.dec, m.white, 'z', 76, 120, 140);
    s.saddle(j.dec, m.white, { w: 38, len: 72, base: 140, mid: 158, top: mount.saddleTop, jaw: 12, inset: 4, r: 4 });
    s.knobs(j.dec, m, 'x', 34, 62, [[-32, 162], [32, 162]]);
}

/**
 * Sky-Watcher EQ8-R Pro: a big black observatory mount with bright green knobs. A tall latitude housing with round
 * side covers and a silver T-handle, a large head with a long handle on its west side and an electronics box on its
 * east side, a wide Dec flange under a huge saddle with three green knobs, black weights on a thick shaft.
 */
function buildEq8Base(s, holder, m, mount) {
    const z0 = mount.top, hinge = z0 + mount.hinge;
    s.cylinder(holder, m.dark, 'z', 214, z0, z0 + 58);
    s.cylinder(holder, m.black, 'z', 224, z0 + 58, z0 + 70);
    s.box(holder, m.dark, [-72, 72], [-92, 92], [z0 + 70, hinge]);
    s.cylinder(holder, m.dark, 'x', 184, -72, 72, [0, hinge]);
    for (const side of [-1, 1]) {
        s.cylinder(holder, m.dark, 'x', 96, side * 72, side * 84, [0, hinge]);          // side covers
        s.cylinder(holder, m.black, 'x', 56, side * 84, side * 88, [0, hinge]);
        s.knob(holder, m, 'x', side * 72, side * 112, [40, z0 + 96], m.green);          // azimuth
    }
    s.cylinder(holder, m.steel, 'y', 14, -92, -140, [0, hinge - 70]);                   // latitude T-handle
    s.cylinder(holder, m.steel, 'z', 12, hinge - 140, hinge - 10, [0, -140]);
}

function buildEq8Head(s, j, m, mount) {
    const D = mount.decAlongRa, junction = -(D - 140);
    // RA body (latitude joint).
    s.cylinder(j.latitude, m.dark, 'y', 172, 150, junction);
    s.cylinder(j.latitude, m.black, 'y', 150, 150, 176);
    // Head (RA joint): a large block, an electronics box east, a long handle west.
    s.rbox(j.ra, m.dark, [-96, 96], [-124, 140], [-150, 140], 26);
    s.rbox(j.ra, m.black, [92, 152], [-100, 70], [-40, 138], 12, 'y');
    s.box(j.ra, m.black, [152, 156], [-60, 30], [20, 110]);
    s.label(j.ra, 'Sky-Watcher\n~EQ8-R PRO', { at: [156.5, -15, 65], normal: 'x', up: 'z', width: 88, height: 84 });
    for (const z of [-70, 70]) s.box(j.ra, m.black, [-124, -96], [-10, 10], [z - 10, z + 10]);
    s.box(j.ra, m.black, [-140, -124], [-14, 14], [-90, 90]);
    s.cylinder(j.ra, m.dark, 'z', 120, -150, -190);                                     // nose
    buildWeights(s, j.ra, m, { from: -190, to: -720, shaft: 32, stop: 50, weight: 190, thick: 80, at: [-470, -580], material: m.dark });
    s.cylinder(j.ra, m.dark, 'z', 226, 140, 160);                                       // Dec flange
    // Puck (Dec joint): a body and a huge saddle with three green knobs on its west side.
    s.cylinder(j.dec, m.dark, 'z', 184, 160, 200);
    s.rbox(j.dec, m.dark, [-92, 92], [-165, 165], [200, 296], 18, 'y');
    s.rbox(j.dec, m.black, [-82, 82], [-160, 160], [296, mount.saddleTop], 6, 'y');
    s.knobs(j.dec, m, 'x', -92, -132, [[-100, 300], [0, 300], [100, 300]], m.green);
}

/**
 * ZWO AM5: a compact strain-wave mount, red and black, without counterweights. Red side plates with black levers
 * on a red and black base disc, a short black RA barrel into a red cube (the motor housing), and a short black Dec
 * barrel up to a large red saddle with black knobs.
 */
function buildAm5Base(s, holder, m, mount) {
    const z0 = mount.top, hinge = z0 + mount.hinge;
    s.cylinder(holder, m.black, 'z', 168, z0, z0 + 10);
    s.cylinder(holder, m.red, 'z', 156, z0 + 10, z0 + 24);
    for (const side of [-1, 1]) {
        const x = side < 0 ? [-58, -46] : [46, 58];
        s.box(holder, m.red, x, [-62, 62], [z0 + 24, hinge]);
        s.cylinder(holder, m.red, 'x', 124, side * 46, side * 58, [0, hinge]);
        s.cylinder(holder, m.steel, 'x', 16, side * 58, side * 64, [0, hinge]);
    }
    s.box(holder, m.black, [58, 66], [-8, 8], [hinge - 46, hinge + 6]);                 // latitude lever
    s.box(holder, m.black, [58, 66], [-34, 30], [hinge - 26, hinge - 14]);
    s.knob(holder, m, 'x', 58, 84, [30, z0 + 44]);
    s.box(holder, m.red, [-26, 26], [-84, -62], [z0 + 24, z0 + 66]);                   // azimuth block
    for (const side of [-1, 1]) s.knob(holder, m, 'x', side * 26, side * 56, [-74, z0 + 44]);
    s.cylinder(holder, m.steel, 'y', 10, -84, -110, [0, z0 + 58]);
}

function buildAm5Head(s, j, m, mount) {
    const D = mount.decAlongRa;
    s.cylinder(j.latitude, m.black, 'y', 104, 42, -(D - 72));                          // RA barrel
    // Head (RA joint): the red cube, a black face on its west side, a cap where a counterweight bar would screw in.
    s.rbox(j.ra, m.red, [-72, 72], [-72, 72], [-72, 72], 8);
    s.label(j.ra, 'AM5', { at: [72.5, 0, 0], normal: 'x', up: 'z', width: 110, height: 52, weight: 800 });
    s.rbox(j.ra, m.black, [-76, -70], [-70, 70], [-70, 70], 6, 'x');
    s.cylinder(j.ra, m.black, 'z', 36, -72, -80);
    // Puck (Dec joint): the Dec barrel and the red saddle with two black knobs.
    s.cylinder(j.dec, m.black, 'z', 104, 72, 132);
    s.saddle(j.dec, m.red, { w: 64, len: 96, base: 132, mid: 174, top: mount.saddleTop, jaw: 16, inset: 8 });
    s.knobs(j.dec, m, 'x', 56, 92, [[-42, 180], [42, 180]]);
}

/**
 * iOptron CEM70: a black centre-balanced mount. Its axes cross nearly over the pier, so the RA housing sits low and
 * short and a tall Dec housing rises from it; a large round RA gear housing, an electronic polar finder's disc, a
 * black saddle, and one big weight on a long shaft.
 */
function buildCem70Base(s, holder, m, mount) {
    const z0 = mount.top, hinge = z0 + mount.hinge;
    s.cylinder(holder, m.dark, 'z', 204, z0, z0 + 40);
    s.box(holder, m.dark, [-96, 96], [-46, 46], [z0 + 40, z0 + 70]);                   // azimuth clamp
    s.knob(holder, m, 'y', -46, -80, [60, z0 + 55]);
    s.box(holder, m.dark, [-62, 62], [-74, 74], [z0 + 70, hinge]);
    s.cylinder(holder, m.dark, 'x', 148, -62, 62, [0, hinge]);
    s.cylinder(holder, m.grey, 'x', 70, 62, 68, [-20, hinge - 52]);                     // polar finder disc
    s.cylinder(holder, m.steel, 'x', 20, -62, -70, [0, hinge]);
}

function buildCem70Head(s, j, m, mount) {
    const D = mount.decAlongRa;
    s.cylinder(j.latitude, m.dark, 'y', 150, 84, -(D - 30));                          // RA housing
    s.cylinder(j.latitude, m.dark, 'y', 196, 40, 92);                                  // RA gear housing
    s.cylinder(j.latitude, m.black, 'y', 120, 92, 98);
    // Head (RA joint): the tall Dec housing, its motor box east, and the Dec gear housing near the top.
    s.cylinder(j.ra, m.dark, 'z', 132, -120, 196);
    s.rbox(j.ra, m.dark, [56, 108], [-60, 60], [-40, 120], 14, 'y');
    s.label(j.ra, 'iOptron\nCEM70', { at: [108.5, 0, 40], normal: 'x', up: 'z', width: 96, height: 56 });
    s.cylinder(j.ra, m.dark, 'z', 196, 196, 232);
    // The counterweight bar hangs from a lug at the pole end, off the Dec axis, so it clears the pier (the axes
    // cross nearly over it); an end cap on the RA axis there.
    s.cylinder(j.ra, m.dark, 'y', 150, -60, -104);
    s.box(j.ra, m.dark, [-38, 38], [-190, -60], [-150, -40]);
    s.cylinder(j.ra, m.dark, 'z', 76, -150, -170, [0, CEM70_BAR_Y]);
    buildWeights(s, j.ra, m, { from: -170, to: -720, shaft: 28, stop: 40, weight: 196, thick: 110, at: [-480], material: m.dark, y: CEM70_BAR_Y });
    // Puck (Dec joint): the saddle body and a black dual saddle with knobs east.
    s.cylinder(j.dec, m.dark, 'z', 150, 232, 262);
    s.saddle(j.dec, m.black, { w: 76, len: 150, base: 262, mid: 288, top: mount.saddleTop, jaw: 18, inset: 8 });
    s.knobs(j.dec, m, 'x', 68, 104, [[-60, 294], [60, 294]]);
}

/**
 * Sky-Watcher CQ350 Pro: black with bright green knobs. A short, wide RA drum rides in a curved latitude cradle (an
 * arc under it) rather than on a tower; the flat Dec housing sits on top of the drum, its ports on the east side,
 * with the saddle above; the counterweight bar hangs from an arm at the drum's north face, off the Dec axis, as on a
 * centre-balanced mount; big green azimuth knobs on the base.
 */
function buildCq350Base(s, holder, m, mount) {
    const z0 = mount.top, hinge = z0 + mount.hinge;
    s.cylinder(holder, m.dark, 'z', 200, z0, z0 + 36);
    s.cylinder(holder, m.black, 'z', 186, z0 + 36, z0 + 46);
    for (const side of [-1, 1]) s.knob(holder, m, 'x', side * 92, side * 132, [0, z0 + 22], m.green);
    // The cradle: a post up to an arc under the RA drum, in the north-south plane about the hinge.
    s.box(holder, m.dark, [-40, 40], [-40, 40], [z0 + 46, hinge - 118]);
    const r = 122;
    for (let a = 200; a <= 340; a += 10) {
        const t = a * Math.PI / 180;
        s.cylinder(holder, m.dark, 'x', 42, -56, 56, [r * Math.cos(t), hinge + r * Math.sin(t)], { tessellation: 16 });
    }
    s.knob(holder, m, 'x', 56, 96, [0, hinge - 118], m.green);                             // latitude lock
}

function buildCq350Head(s, j, m, mount) {
    const D = mount.decAlongRa, bar = CQ350_BAR_Y;
    // RA drum (latitude joint): short and wide, in the cradle, a round cap on its north face, a green ring south.
    s.cylinder(j.latitude, m.dark, 'y', 210, 56, -46);
    s.cylinder(j.latitude, m.black, 'y', 116, -46, -58);
    s.cylinder(j.latitude, m.green, 'y', 214, 56, 64);
    s.cylinder(j.latitude, m.dark, 'y', 196, 64, 80);
    // Head (RA joint; the axes cross over the drum): the flat Dec housing on top of the drum, its ports east, and
    // an arm down past the drum's north face carrying the counterweight bar, off the Dec axis so it clears the pier.
    s.rbox(j.ra, m.dark, [-82, 82], [bar - 22, 100], [104, 170], 18, 'y');
    s.label(j.ra, 'Sky-Watcher\n~CQ350 PRO', { at: [-82.5, -62, 137], normal: '-x', up: 'z', width: 130, height: 50 });
    s.box(j.ra, m.black, [82, 88], [-60, 70], [116, 160]);
    for (const [y, z] of [[-40, 146], [-16, 146], [8, 146], [40, 130]]) s.cylinder(j.ra, m.steel, 'x', 12, 88, 91, [y, z], { tessellation: 16 });
    s.knob(j.ra, m, 'x', -82, -112, [40, 136], m.green);                                 // Dec clutch
    s.box(j.ra, m.dark, [-44, 44], [bar - 22, bar + 22], [-64, 104]);                    // the arm
    buildWeights(s, j.ra, m, { from: -64, to: -560, shaft: 28, stop: 44, weight: 160, thick: 64, at: [-380, -452], material: m.dark, y: bar });
    // Puck (Dec joint): the Dec plate and the saddle with green knobs on its west side.
    s.cylinder(j.dec, m.dark, 'z', 172, 170, 190);
    s.cylinder(j.dec, m.green, 'z', 176, 170, 176);
    s.rbox(j.dec, m.dark, [-66, 66], [-142, 142], [190, 226], 10, 'y');
    s.rbox(j.dec, m.black, [-60, 60], [-138, 138], [226, mount.saddleTop], 6, 'y');
    s.knobs(j.dec, m, 'x', -66, -104, [[-90, 230], [0, 230], [90, 230]], m.green);
}

/**
 * Sky-Watcher EQM-35 Pro: a small white mount. A white latitude tower with a black dial; a white RA housing with a
 * green ring and its black motor box; a tall white Dec housing with a ribbed conical nose, a round black clutch knob
 * and the black Dec motor box; a green anodised saddle on a short neck, and two white weights.
 */
function buildEqm35Base(s, holder, m, mount) {
    const z0 = mount.top, hinge = z0 + mount.hinge;
    s.cylinder(holder, m.white, 'z', 112, z0, z0 + 28);
    s.box(holder, m.white, [-30, 30], [-38, 38], [z0 + 28, hinge]);
    s.cylinder(holder, m.white, 'x', 80, -30, 30, [0, hinge]);
    s.cylinder(holder, m.black, 'x', 58, 30, 36, [0, hinge]);                             // dial
    s.label(holder, 'EQM-35', { at: [36.5, 0, hinge], normal: 'x', up: 'z', width: 46, height: 16 });
    s.cylinder(holder, m.black, 'x', 58, -30, -34, [0, hinge]);
    for (const side of [-1, 1]) {                                                         // latitude T-bolts
        s.cylinder(holder, m.steel, 'y', 9, side * 38, side * 96, [0, z0 + 54]);
        s.cylinder(holder, m.steel, 'x', 7, -22, 22, [side * 96, z0 + 54]);
    }
    for (const side of [-1, 1]) s.knob(holder, m, 'x', side * 30, side * 62, [-24, z0 + 16]);   // azimuth
}

function buildEqm35Head(s, j, m, mount) {
    const D = mount.decAlongRa, junction = -(D - 50);
    // RA housing (latitude joint): white, a green ring near the head, the polar scope cap south, the RA motor east.
    s.cylinder(j.latitude, m.white, 'y', 86, 92, junction);
    s.cylinder(j.latitude, m.green, 'y', 90, junction + 22, junction + 12);
    s.cylinder(j.latitude, m.white, 'y', 86, 92, 104, [0, 0], { top: 64 });
    s.cylinder(j.latitude, m.black, 'y', 44, 104, 116);
    s.rbox(j.latitude, m.black, [42, 86], [-50, 20], [-30, 30], 8, 'y');
    // Head (RA joint): the tall white Dec housing with a ribbed conical nose down to the shaft, a round black Dec
    // clutch knob, the Dec motor box with its ports on the east side near the top, and two white weights.
    s.cylinder(j.ra, m.white, 'y', 92, 50, 34);
    s.cylinder(j.ra, m.white, 'z', 92, -150, 92);
    s.cylinder(j.ra, m.white, 'z', 92, -150, -188, [0, 0], { top: 40 });
    for (let z = -158; z > -184; z -= 8) {
        const d = 92 - (52 * (-150 - z)) / 38;
        s.cylinder(j.ra, m.white, 'z', d + 4, z, z - 3, [0, 0], { tessellation: 32 });
    }
    s.knob(j.ra, m, 'x', 22, 50, [0, -176]);                                             // shaft lock
    s.cylinder(j.ra, m.black, 'x', 40, -44, -60, [-20, -40], { tessellation: 24 });     // Dec clutch knob
    s.rbox(j.ra, m.black, [44, 100], [-40, 46], [10, 92], 8);                            // Dec motor box
    for (const [y, z] of [[-24, 70], [-4, 70], [16, 70], [-14, 34]]) s.cylinder(j.ra, m.steel, 'x', 10, 100, 103, [y, z], { tessellation: 16 });
    buildWeights(s, j.ra, m, { from: -188, to: -420, shaft: 16, stop: 26, weight: 112, thick: 40, at: [-300, -346], material: m.white });
    // Puck (Dec joint): a green ring, a short white neck, and the green anodised saddle with a black knob.
    s.cylinder(j.dec, m.green, 'z', 96, 92, 100);
    s.cylinder(j.dec, m.white, 'z', 74, 100, 114);
    s.saddle(j.dec, m.green, { w: 34, len: 62, base: 114, mid: 128, top: mount.saddleTop, jaw: 10, inset: 4, r: 4 });
    s.knobs(j.dec, m, 'x', 30, 56, [[-26, 132], [26, 132]]);
}

/**
 * Sky-Watcher Wave 150i Pro: a black strain-wave mount with green trim, without counterweights. A black base with
 * green knobs and side plates (the round Wi-Fi badge on its east side), a short RA barrel, a big black drum with a
 * green ring (the RA head, carrying the Dec drive), and the Dec axis out of the drum's side to a black saddle with
 * green knobs and a small port box.
 */
function buildWave150Base(s, holder, m, mount) {
    const z0 = mount.top, hinge = z0 + mount.hinge;
    s.rbox(holder, m.dark, [-62, 62], [-72, 72], [z0, z0 + 40], 12);
    for (const side of [-1, 1]) {
        const x = side < 0 ? [-62, -50] : [50, 62];
        s.box(holder, m.dark, x, [-56, 56], [z0 + 40, hinge]);
        s.cylinder(holder, m.dark, 'x', 112, side * 50, side * 62, [0, hinge]);
        s.knob(holder, m, 'x', side * 62, side * 92, [-40, z0 + 20], m.green);          // azimuth
    }
    s.knob(holder, m, 'x', 62, 94, [30, hinge - 30], m.green);                           // latitude lock
    s.cylinder(holder, m.black, 'x', 52, 62, 66, [-24, z0 + 74], { tessellation: 32 });  // the Wi-Fi badge
    s.cylinder(holder, m.grey, 'x', 18, 66, 67, [-24, z0 + 74], { tessellation: 16 });
    s.knob(holder, m, 'y', -72, -100, [0, z0 + 20], m.green);
}

function buildWave150Head(s, j, m, mount) {
    const D = mount.decAlongRa;
    s.cylinder(j.latitude, m.dark, 'y', 116, 46, -(D - 100));                           // RA barrel
    // Head (RA joint): the big drum along the polar axis, a green ring near its base.
    s.cylinder(j.ra, m.dark, 'y', 172, 100, -84);
    s.cylinder(j.ra, m.dark, 'y', 172, -84, -96, [0, 0], { top: 150 });
    s.cylinder(j.ra, m.green, 'y', 176, 70, 80);
    s.label(j.ra, 'Sky-Watcher\n~WAVE 150i', { at: [87, -6, 0], normal: 'x', up: 'z', width: 116, height: 44 });
    s.cylinder(j.ra, m.black, 'z', 40, -86, -92);                                       // counterweight thread cap
    // Puck (Dec joint): out of the drum's side: the Dec plate, a port box and the saddle with green knobs.
    s.cylinder(j.dec, m.dark, 'z', 108, 84, 104);
    s.rbox(j.dec, m.black, [-46, 46], [-70, -20], [104, 126], 6, 'y');
    s.saddle(j.dec, m.dark, { w: 52, len: 92, base: 104, mid: 128, top: mount.saddleTop, jaw: 14 });
    s.knobs(j.dec, m, 'x', 46, 76, [[-40, 132], [40, 132]], m.green);
}

/**
 * A fork mount (generic, after the classic Schmidt-Cassegrain forks): a drive base, a turntable and two arms in dark
 * grey, the OTA hanging between them on the Dec axis. On a wedge for a polar mount; for Alt-Az the drive base stands
 * on a round pedestal, its axis straight up. No counterweights.
 */
function buildForkBase(s, holder, m, mount) {
    const z0 = mount.top, hinge = z0 + mount.hinge;
    if (mount.mode === 0) {                                                               // Alt-Az: a pedestal
        s.cylinder(holder, m.dark, 'z', 240, z0, z0 + 24);
        s.cylinder(holder, m.dark, 'z', 200, z0 + 24, hinge - 10);
        return;
    }
    // A wedge: a base plate, two side cheeks either side of the drive base, the latitude bolt and a lock knob.
    s.rbox(holder, m.dark, [-176, 176], [-150, 150], [z0, z0 + 22], 20);
    for (const side of [-1, 1]) {
        const x = side < 0 ? [-176, -158] : [158, 176];
        s.box(holder, m.dark, x, [-120, 120], [z0 + 22, hinge + 30]);
        s.cylinder(holder, m.steel, 'x', 30, side * 176, side * 186, [0, hinge]);
        s.knob(holder, m, 'x', side * 186, side * 214, [-70, hinge - 60]);
    }
    s.cylinder(holder, m.steel, 'z', 16, z0 + 22, hinge - 80, [0, 110]);                 // latitude bolt
}

function buildForkHead(s, j, m, mount) {
    const D = mount.decAlongRa, g = (mount.otaHalfWidth ?? 120) + 22, arm = 46;
    // Drive base (latitude joint; the hinge at its back): a wide disc and its control panel.
    s.cylinder(j.latitude, m.black, 'y', 300, 12, -64);
    s.rbox(j.latitude, m.black, [-60, 60], [-60, -10], [138, 170], 8, 'y');
    for (const x of [-30, 0, 30]) s.cylinder(j.latitude, m.steel, 'z', 10, 170, 173, [x, -35], { tessellation: 12 });
    // Turntable and fork (RA joint; origin where the Dec axis crosses the polar axis).
    s.cylinder(j.ra, m.dark, 'y', 280, D - 64, D - 84);
    s.rbox(j.ra, m.dark, [-60, 60], [D - 120, D - 84], [-(g + arm), g + arm], 12, 'x');
    for (const side of [-1, 1]) {
        const z = side < 0 ? [-(g + arm), -g] : [g, g + arm];
        s.rbox(j.ra, m.dark, [-48, 48], [-40, D - 110], z, 18, 'z');                     // the arm
        s.cylinder(j.ra, m.dark, 'z', 110, side * g, side * (g + arm), [0, 0]);           // its bearing housing
        s.cylinder(j.ra, m.black, 'z', 70, side * (g + arm), side * (g + arm + 4), [0, 0]);
    }
    s.label(j.ra, 'FORK', { at: [0, D - 102, g + arm + 0.5], normal: 'z', up: '-y', width: 60, height: 22 });
    // Dec joint: the trunnions from the arms to the OTA's side plates.
    for (const side of [-1, 1]) {
        s.cylinder(j.dec, m.steel, 'z', 50, side * (g - 26), side * g, [0, 0]);
        s.rbox(j.dec, m.black, [-36, 36], [-90, 90], side < 0 ? [-(g - 20), -(g - 30)] : [g - 30, g - 20], 8, 'z');
    }
}

/**
 * iOptron CEM40: a centre-balanced mount in red and black, the CEM70's smaller sibling. A black base with shaped
 * side plates, a brass latitude screw and a bubble level; a red RA housing with a black octagonal end cap; a red
 * Dec housing between black end blocks; a black saddle, and one black weight on a steel shaft.
 */
function buildCem40Base(s, holder, m, mount) {
    const z0 = mount.top, hinge = z0 + mount.hinge;
    s.cylinder(holder, m.dark, 'z', 156, z0, z0 + 24);
    for (const side of [-1, 1]) {
        const x = side < 0 ? [-58, -46] : [46, 58];
        s.box(holder, m.dark, x, [-66, 54], [z0 + 24, hinge]);
        s.cylinder(holder, m.dark, 'x', 110, side * 46, side * 58, [0, hinge]);
        s.cylinder(holder, m.steel, 'x', 18, side * 58, side * 62, [0, hinge]);
    }
    s.cylinder(holder, m.brass, 'z', 10, z0 + 30, hinge - 60, [0, -50]);              // latitude screw
    s.cylinder(holder, m.black, 'x', 26, -12, 12, [-50, z0 + 70], { tessellation: 5 });
    s.cylinder(holder, m.green, 'x', 16, 58, 62, [-40, z0 + 14], { tessellation: 16 });  // bubble level
    s.knob(holder, m, 'y', -66, -92, [30, z0 + 14]);                                     // azimuth
}

function buildCem40Head(s, j, m, mount) {
    const D = mount.decAlongRa;
    // RA housing (latitude joint): red, a black octagonal cap on its south end.
    s.cylinder(j.latitude, m.red, 'y', 118, 92, -(D - 20));
    s.cylinder(j.latitude, m.dark, 'y', 142, 92, 128, [0, 0], { tessellation: 8 });
    // Head (RA joint): the red Dec housing between black end blocks, the Dec drive on top, and the counterweight.
    s.rbox(j.ra, m.dark, [-60, 60], [-62, 62], [-84, -40], 10);
    s.rbox(j.ra, m.red, [-56, 56], [-58, 58], [-40, 78], 8);
    s.label(j.ra, 'iOptron\nCEM40', { at: [-56.5, 0, 20], normal: '-x', up: 'z', width: 90, height: 52 });
    s.rbox(j.ra, m.dark, [-60, 60], [-62, 62], [78, 108], 10);
    s.cylinder(j.ra, m.dark, 'z', 150, 108, 136);
    s.rbox(j.ra, m.dark, [56, 92], [-40, 30], [20, 96], 6, 'y');                         // ports box
    // The counterweight bar hangs from a lug at the pole end, off the Dec axis, so it clears the pier (the axes
    // cross nearly over it); an end cap on the RA axis there.
    s.cylinder(j.ra, m.dark, 'y', 124, -62, -100);
    s.box(j.ra, m.dark, [-32, 32], [-168, -62], [-112, -30]);
    s.cylinder(j.ra, m.dark, 'z', 62, -112, -130, [0, CEM40_BAR_Y]);
    buildWeights(s, j.ra, m, { from: -130, to: -540, shaft: 22, stop: 34, weight: 150, thick: 84, at: [-380], material: m.dark, y: CEM40_BAR_Y });
    // Puck (Dec joint): the Dec plate and a black saddle with a big knob east.
    s.cylinder(j.dec, m.dark, 'z', 128, 136, 156);
    s.saddle(j.dec, m.black, { w: 62, len: 130, base: 156, mid: 196, top: mount.saddleTop, jaw: 14 });
    s.cylinder(j.dec, m.black, 'x', 46, 62, 82, [60, 180], { tessellation: 24 });
}

/**
 * OTAs built from simple shapes (ASSEMBLY_BUILT_OTAS), in their own frame on the saddle (millimetres): the tube
 * along Y with its aperture toward -Y (the pole at the home position), its dovetail standing on the saddle at z = 0.
 * Drawn at the size an .obj OTA has at the default scale (ASSEMBLY_OTA.scale), then scaled with it.
 */

/** A small finder scope on a foot, along the tube's top at height `z`, from y0 to y1. */
function buildFinder(s, ota, m, z, y0, y1) {
    s.box(ota, m.black, [-10, 10], [y0 + 30, y1 - 30], [z, z + 24]);
    s.cylinder(ota, m.black, 'y', 44, y0, y1, [0, z + 46]);
    s.cylinder(ota, m.glass, 'y', 36, y0 - 1, y0, [0, z + 46], { tessellation: 24 });
}

/**
 * Newtonian: a long open tube on two tube rings and a dovetail; the open aperture dark at the front, the focuser on
 * the side near the front, a finder on top, and the mirror cell with collimation knobs at the back.
 */
function buildNewtonian(s, ota, m) {
    const d = 250, half = 520, axis = 20 + 18 + d / 2;          // the tube's axis above the dovetail's foot
    s.box(ota, m.steel, [-40, 40], [-270, 270], [0, 20]);       // dovetail
    for (const y of [-210, 210]) {                              // tube rings and their feet
        s.box(ota, m.black, [-18, 18], [y - 14, y + 14], [20, 40]);
        s.cylinder(ota, m.black, 'y', d + 26, y - 16, y + 16, [0, axis], { tessellation: 48 });
    }
    // The tube stops just inside the front ring and mirror cell, so their faces don't fight over the same plane.
    s.cylinder(ota, m.ota, 'y', d, -half + 4, half - 4, [0, axis], { tessellation: 48 });
    s.cylinder(ota, m.black, 'y', d + 8, -half, -half + 16, [0, axis], { tessellation: 48 });   // front ring
    s.cylinder(ota, m.glass, 'y', d - 14, -half - 1, -half, [0, axis], { tessellation: 48 });   // the open end, dark
    s.cylinder(ota, m.black, 'y', d + 6, half - 22, half, [0, axis], { tessellation: 48 });     // mirror cell
    for (const a of [90, 210, 330]) {                           // collimation knobs
        const t = a * Math.PI / 180;
        s.cylinder(ota, m.black, 'y', 22, half, half + 16, [d * 0.32 * Math.cos(t), axis + d * 0.32 * Math.sin(t)], { tessellation: 12 });
    }
    // Focuser out of the east side near the front, its draw tube and a knob.
    s.box(ota, m.black, [d / 2 - 6, d / 2 + 18], [-half + 70, -half + 150], [axis - 40, axis + 40]);
    s.cylinder(ota, m.black, 'x', 64, d / 2 + 18, d / 2 + 70, [-half + 110, axis]);
    s.cylinder(ota, m.steel, 'x', 48, d / 2 + 70, d / 2 + 110, [-half + 110, axis]);
    s.cylinder(ota, m.black, 'z', 20, axis - 50, axis + 50, [d / 2 + 44, -half + 110], { tessellation: 12 });
    buildFinder(s, ota, m, axis + d / 2, -half + 60, -half + 260);
}

/**
 * Maksutov-Cassegrain: a short, fat tube; the glass meniscus at the front with its silvered secondary spot, a
 * mounting rail, and at the back the visual back, a star diagonal, an eyepiece and the focus knob.
 */
function buildMaksutov(s, ota, m) {
    const d = 210, front = -240, back = 230, axis = 18 + 14 + d / 2;
    s.box(ota, m.steel, [-36, 36], [-170, 170], [0, 18]);       // dovetail
    for (const y of [-120, 120]) s.box(ota, m.black, [-24, 24], [y - 22, y + 22], [18, 40]);    // mounting blocks
    s.cylinder(ota, m.ota, 'y', d, front + 4, back, [0, axis], { tessellation: 48 });   // just inside the front cell
    s.cylinder(ota, m.black, 'y', d + 12, front, front + 34, [0, axis], { tessellation: 48 });  // front cell
    s.cylinder(ota, m.glass, 'y', d - 24, front - 2, front, [0, axis], { tessellation: 48 });   // meniscus
    s.cylinder(ota, m.steel, 'y', 60, front - 3, front - 2, [0, axis], { tessellation: 32 });  // secondary spot
    s.cylinder(ota, m.black, 'y', d - 8, back, back + 30, [0, axis], { tessellation: 48 });    // rear cell
    s.cylinder(ota, m.black, 'y', 56, back + 30, back + 74, [0, axis]);                        // visual back
    s.box(ota, m.black, [-30, 30], [back + 74, back + 134], [axis - 30, axis + 30]);           // star diagonal
    s.cylinder(ota, m.black, 'z', 46, axis + 30, axis + 74, [0, back + 104]);
    s.cylinder(ota, m.steel, 'z', 40, axis + 74, axis + 130, [0, back + 104]);                 // eyepiece
    s.cylinder(ota, m.black, 'x', 38, d / 2 - 30, d / 2 + 6, [back + 14, axis - 40], { tessellation: 16 });  // focus knob
    buildFinder(s, ota, m, axis + d / 2, front + 40, front + 200);
}

function disposeImported(result) {
    if (!result) return;
    for (const mesh of result.meshes || []) mesh.dispose();
    for (const node of result.transformNodes || []) node.dispose();
}

export function disposeAssembly(viewer) {
    if (!viewer.assembly) return;
    viewer.assembly.holder.dispose(false, false);
    viewer.assembly = null;
    viewer.assemblyAt = null;
}

/** Loads the selected support, mount and OTA; the old model remains until its OBJ tube is ready. */
export async function loadAssembly(viewer, url, mount, load) {
    const B = window.BABYLON;
    const scene = viewer.scene;
    const parts = mount?.assembly;
    if (!parts || !Object.hasOwn(ASSEMBLY_SUPPORTS, parts.support))
        return `Error: unsupported assembly support '${parts?.support}'`;
    if (!Object.hasOwn(ASSEMBLY_MOUNTS, parts.mount))
        return `Error: unsupported assembly mount '${parts.mount}'`;
    const builtOta = ASSEMBLY_BUILT_OTAS[parts.ota];
    if (!builtOta && !ASSEMBLY_OTAS.includes(parts.ota))
        return `Error: unsupported assembly OTA '${parts.ota}'`;
    const support = ASSEMBLY_SUPPORTS[parts.support];
    const build = ASSEMBLY_MOUNTS[parts.mount];
    const otaName = builtOta ? null : parts.ota;
    const folder = url ? url.substring(0, url.lastIndexOf('/') + 1) : '';
    if (otaName && !folder) return 'Error: assembly OTA model URL is missing';
    if (B.OBJFileLoader) B.OBJFileLoader.SKIP_MATERIALS = true;

    let ota = null;
    try {
        if (otaName)
            ota = await B.SceneLoader.ImportMeshAsync('', folder, `${otaName}.obj`, scene, null, '.obj');
    } catch (error) {
        return load !== viewer.loadCount || scene.isDisposed
            ? 'Superseded'
            : `Error: assembly OTA '${otaName}' failed to load: ${error?.message ?? error}`;
    }
    if (load !== viewer.loadCount || scene.isDisposed) {
        disposeImported(ota);
        return 'Superseded';
    }

    const tube = ota?.meshes.filter(mesh => mesh.getTotalVertices() > 0) || [];
    if (otaName && tube.length === 0) {
        disposeImported(ota);
        return `Error: assembly OTA '${otaName}' has no meshes`;
    }

    const wasAssembly = !!viewer.assembly;
    disposeAssembly(viewer);
    if (!wasAssembly) viewer.modelMeshes.forEach(mesh => mesh.dispose());
    viewer.modelMeshes = [];
    viewer.modelMaterials.forEach(material => material.dispose(false, true));
    viewer.modelMaterials = [];
    viewer.pierMeshes.forEach(mesh => mesh.dispose());
    viewer.pierMeshes = [];

    const mat = (name, hex, specular = 0.15, power = 24) => {
        const material = new B.StandardMaterial(name, scene);
        material.diffuseColor = B.Color3.FromHexString(hex);
        material.specularColor = new B.Color3(specular, specular, specular);
        material.specularPower = power;
        return material;
    };
    const materials = {
        tripod: mat('AssemblyTripod', '#34474f', 0.1),
        white: mat('AssemblyMountWhite', '#ecece7', 0.3, 40),
        black: mat('AssemblyMountBlack', '#1b1f21', 0.25),
        steel: mat('AssemblyMountSteel', '#b3b9bd', 1, 90),
        green: mat('AssemblyMountGreen', '#2fbf55', 0.3),
        grey: mat('AssemblyMountGrey', '#5f676b', 0.3),
        dark: mat('AssemblyMountDark', '#2a2e31', 0.3),
        red: mat('AssemblyMountRed', '#cf2a22', 0.5, 60),
        brass: mat('AssemblyMountBrass', '#b8913f', 0.8, 70),
        glass: mat('AssemblyMountGlass', '#13232d', 0.9),
        ota: mat('AssemblyOTA', viewer.primaryColor, 0.3)
    };
    viewer.otaMaterial = materials.ota;
    viewer.modelMaterials = Object.values(materials);

    const holder = new B.TransformNode('Assembly', scene);
    holder.parent = viewer.root;
    holder.scaling = new B.Vector3(ASSEMBLY_SCALE, ASSEMBLY_SCALE, ASSEMBLY_SCALE);
    const mode = mount?.mode ?? 2;
    const latitude = Number.isFinite(mount?.latitude) ? mount.latitude : 0;
    const top = support.pier ? ASSEMBLY_COLUMN_TOP : ASSEMBLY_COLUMN_TOP * support.tripod;
    const at = { ...build, top, mode };
    viewer.assemblyAt = at;
    const latitudeJoint = new B.TransformNode('AssemblyLatitudeJoint', scene);
    latitudeJoint.parent = holder;
    latitudeJoint.position = new B.Vector3(0, 0, top + build.hinge);
    const ra = new B.TransformNode('AssemblyRaJoint', scene);
    ra.parent = latitudeJoint;
    ra.position = new B.Vector3(0, -build.decAlongRa, 0);
    const dec = new B.TransformNode('AssemblyDecJoint', scene);
    dec.parent = ra;
    for (const node of [latitudeJoint, ra, dec]) node.rotationQuaternion = B.Quaternion.Identity();
    viewer.assembly = { holder, latitude: latitudeJoint, ra, dec, mode, south: latitude < 0 };

    const keep = mesh => {
        mesh.isPickable = false;
        viewer.modelMeshes.push(mesh);
    };
    const shapes = new AssemblyShapes(B, scene, keep);
    const size = Number.isFinite(parts.otaSize) && parts.otaSize > 0 ? parts.otaSize / 100 : 1;
    shapes.otaSize = size;
    if (support.pier) {
        buildPier(shapes, holder, materials, top, support.pier);
    } else {
        const tripod = new B.TransformNode('AssemblyTripod', scene);
        tripod.parent = holder;
        tripod.scaling = new B.Vector3(support.tripod, support.tripod, support.tripod);
        buildTripod(shapes, tripod, materials, support.thick ?? 1);
    }
    viewer.modelMaterials.push(...shapes.labelMaterials);
    shapes.labelMaterials = viewer.modelMaterials;
    build.base(shapes, holder, materials, at);

    const o = ASSEMBLY_OTA;
    const scale = (build.otaScale ?? o.scale) * size;
    const otaNode = new B.TransformNode('AssemblyOta', scene);
    otaNode.parent = dec;
    if (builtOta) {
        const node = new B.TransformNode('AssemblyBuiltOta', scene);
        node.parent = otaNode;
        node.position = new B.Vector3(0, 0, build.saddleTop);
        node.scaling = new B.Vector3(scale / o.scale, scale / o.scale, scale / o.scale);
        builtOta(shapes, node, materials);
    }
    if (tube.length > 0) {
        const bottom = tube[0].getBoundingInfo().boundingBox.minimum.z;
        tube[0].parent = otaNode;
        tube[0].scaling = new B.Vector3(scale, scale, scale);
        tube[0].position = new B.Vector3(0, -o.decAxisY * scale, build.saddleTop - bottom * scale);
        tube[0].material = materials.ota;
        keep(tube[0]);
    }
    tube.slice(1).forEach(mesh => mesh.dispose());

    const extent = () => {
        dec.computeWorldMatrix(true);
        const { min, max } = otaNode.getHierarchyBoundingVectors(true);
        const toDec = dec.getWorldMatrix().clone().invert();
        const lo = new B.Vector3(Infinity, Infinity, Infinity), hi = lo.scale(-1);
        for (const x of [min.x, max.x]) for (const y of [min.y, max.y]) for (const z of [min.z, max.z]) {
            const corner = B.Vector3.TransformCoordinates(new B.Vector3(x, y, z), toDec);
            lo.minimizeInPlace(corner);
            hi.maximizeInPlace(corner);
        }
        return { lo, hi };
    };
    if (build.fork) {
        const { lo, hi } = extent();
        otaNode.position = lo.add(hi).scale(-0.5);
        at.otaHalfWidth = Math.max(hi.x - lo.x, hi.z - lo.z) / 2;
    }
    build.head(shapes, { latitude: latitudeJoint, ra, dec }, materials, at);
    for (const mesh of ota?.meshes || []) {
        if (mesh !== tube[0]) mesh.dispose();
    }
    for (const node of ota?.transformNodes || []) node.dispose();
    viewer.applyPose();
    return 'Model loaded successfully';
}

export function applyAssemblyPose(viewer) {
    const B = window.BABYLON;
    const { x, y, z } = viewer.pose;
    const assembly = viewer.assembly;
    let ra = y, dec = x;
    if (assembly.mode === ALT_AZ) dec = x + 90;
    else if (assembly.mode === POLAR && assembly.south) { ra = -y; dec = -x; }
    assembly.latitude.rotationQuaternion = B.Quaternion.FromRotationMatrix(B.Matrix.RotationX(rad(z)));
    assembly.ra.rotationQuaternion = B.Quaternion.FromRotationMatrix(B.Matrix.RotationY(rad(ra)));
    assembly.dec.rotationQuaternion = B.Quaternion.FromRotationMatrix(B.Matrix.RotationZ(rad(dec)));
}
"""Generate the editable Blender rig and the desktop renderer's mesh data."""

from argparse import ArgumentParser
from math import cos, pi, radians, sin, sqrt
from pathlib import Path
from collections import defaultdict
import struct
import wave
import zlib

import bpy
import bmesh
from mathutils import Vector
from mathutils.bvhtree import BVHTree
import numpy as np


POSE_RATE = 12
DURATION = {"Idle": 3, "Laugh": 4.3, "Feed": 2.1, "Sleep": 3, "Walk": 1}
ROOT = Path(__file__).resolve().parent
GROUND = -.35 + (-.984 + .35) * .66
HEIGHT = .882 - GROUND
ALBEDO_SIZE = 1024
SWATCH_WIDTH = 64


def select(obj):
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj


def smooth(a, b, x):
    t = max(0, min(1, (x - a) / (b - a)))
    return t * t * (3 - 2 * t)


def classic_proportions(co):
    x, y, z = co
    belly = (1 - (min(1, abs(z + .065) / .58)) ** 2) ** 2
    crown = smooth(.50, .79, z)
    x *= 1 + .22 * belly + .10 * crown
    y *= 1 + .24 * belly + .12 * crown
    y += .046 * belly
    if z < -.35:
        z = -.35 + (z + .35) * .66
    return Vector((x, y, z))


def paint(name, color):
    material = bpy.data.materials.new(name)
    material.use_nodes = True
    material.diffuse_color = (*color, 1)
    material.node_tree.nodes.get("Principled BSDF").inputs["Base Color"].default_value = (*color, 1)
    return material


def face_patches(pet):
    surface = BVHTree.FromPolygons(
        [v.co for v in pet.data.vertices],
        [tuple(f.vertices) for f in pet.data.polygons if f.material_index in (0, 1)],
    )

    def surface_depth(x, z):
        hit, *_ = surface.ray_cast((x, .85, z), (0, -1, 0))
        return .128 if hit is None else hit.y

    def on_face(x, z, offset):
        return (x, surface_depth(x, z) + offset, z)

    def absolute(x, z, y):
        return (x, y, z)

    def disc(name, materials, rest, expression, band=lambda radius: 0, rings=12,
             rest_coordinates=on_face, expression_coordinates=on_face):
        sectors = 40
        uv = [(0, 0)] + [
            (r / rings * cos(i * 2 * pi / sectors), r / rings * sin(i * 2 * pi / sectors))
            for r in range(1, rings + 1) for i in range(sectors)
        ]
        faces = [(0, 1 + i, 1 + (i + 1) % sectors) for i in range(sectors)]
        for ring in range(rings - 1):
            inner = 1 + ring * sectors
            outer = inner + sectors
            for i in range(sectors):
                j = (i + 1) % sectors
                faces.append((inner + i, outer + i, outer + j, inner + j))
        geometry = bpy.data.meshes.new(name)
        geometry.from_pydata([rest_coordinates(*rest(*p)) for p in uv], [], [tuple(reversed(f)) for f in faces])
        target = geometry.attributes.new("Expression target", "FLOAT_VECTOR", "POINT")
        for datum, p in zip(target.data, uv):
            datum.vector = expression_coordinates(*expression(*p))
        for material, color in materials:
            geometry.materials.append(paint(material, color))
        for polygon in geometry.polygons:
            polygon.use_smooth = True
            polygon.material_index = band((polygon.index // sectors + .6) / rings)
        obj = bpy.data.objects.new(name, geometry)
        bpy.context.collection.objects.link(obj)
        return obj

    def grin(u, v, depth):
        return (.074 * u, .677 + .0035 * v + .010 * u * u, depth)

    def open_jaw(u, v, depth):
        z = .642 + (.063 if v >= 0 else .074) * v + .012 * u * u
        return (.116 * u, z, depth + .012 * (u * u + v * v))

    patches = [
        disc("Mouth", [("Mouth", (.020, .004, .006)), ("Lip rim", (.49, .23, .054))],
             lambda u, v: grin(u, v, .013), lambda u, v: open_jaw(u, v, .030),
             band=lambda radius: int(radius > .91)),
        disc("Teeth", [("Teeth", (.98, .94, .84))],
             lambda u, v: (.066 * u, .678 + .004 * v, -.020),
             lambda u, v: open_jaw(u * .79, .61 + .19 * v - .06 * u * u, .042)),
        disc("Tongue", [("Tongue", (.46, .12, .13))],
             lambda u, v: (.040 * u, .672 + .002 * v, -.028),
             lambda u, v: open_jaw(u * .48, -.62 + .235 * v, .042)),
    ]
    for side, sign in (("L", 1), ("R", -1)):
        center = sign * .091
        eye_z = .776
        neutral_y = surface_depth(center, eye_z)
        slope = max(-.55, min(.55, (surface_depth(center + .014, eye_z) - surface_depth(center - .014, eye_z)) / .028))
        eye_colors = [(f"Eye.{part}.{side}", color) for part, color in (
            ("Pupil", (.015, .023, .006)), ("Iris", (.27, .40, .071)), ("Edge", (.18, .10, .035))
        )]

        def eye(u, v):
            x = center + .026 * u
            z = eye_z + .028 * v
            depth = neutral_y + slope * (x - center) + .013 + .017 * sqrt(max(0, 1 - u * u - v * v))
            return (x, z, depth)

        def tucked(u, v):
            return (center + .013 * u, eye_z + .003 * v, -.060)

        def glint(u, v):
            offset = -.24 + u * .17
            height = .29 + v * .18
            x, z, depth = eye(offset, height)
            return (x, z, depth + .003)

        patches.append(disc("Eye." + side, eye_colors, eye, tucked,
                            band=lambda radius: 0 if radius < .63 else 1 if radius < .91 else 2,
                            rest_coordinates=absolute, expression_coordinates=absolute))
        patches.append(disc("Eye.Glint." + side, [("Eye.Glint." + side, (.97, .94, .88))],
                            glint, tucked, rings=5, rest_coordinates=absolute, expression_coordinates=absolute))
        patches.append(disc("Eyelid." + side, [("Eyelid." + side, (.048, .023, .009))],
                            tucked,
                            lambda u, v: (center + .030 * u, .771 + .0056 * v + .018 * (1 - u * u), .023),
                            rings=6, rest_coordinates=absolute))
    select(pet)
    for obj in patches:
        obj.select_set(True)
    bpy.ops.object.join()


def prepare(source):
    bpy.ops.wm.open_mainfile(filepath=str(source))
    pet = next(o for o in bpy.context.scene.objects if o.type == "MESH")
    for obj in list(bpy.context.scene.objects):
        if obj != pet:
            bpy.data.objects.remove(obj, do_unlink=True)
    pet.location = (0, 0, 0)
    pet.name = "Nailong • WeDai mesh"
    select(pet)
    welded = bmesh.new()
    welded.from_mesh(pet.data)
    bmesh.ops.delete(welded, geom=[f for f in welded.faces if f.material_index in (2, 3)], context="FACES")
    bmesh.ops.delete(welded, geom=[v for v in welded.verts if not v.link_faces], context="VERTS")
    for face in welded.faces:
        face.material_index = 0
    bmesh.ops.remove_doubles(welded, verts=list(welded.verts), dist=.00085)
    soft_detail = [v for v in welded.verts if (abs(v.co.x) < .022 and -.35 < v.co.z < .87)
                   or (v.co.y > .07 and .737 < v.co.z < .816 and .05 < abs(v.co.x) < .138)]
    for _ in range(5):
        bmesh.ops.smooth_vert(welded, verts=soft_detail, factor=.40, use_axis_x=False, use_axis_y=True, use_axis_z=True)
    bmesh.ops.recalc_face_normals(welded, faces=list(welded.faces))
    welded.to_mesh(pet.data)
    welded.free()
    detail = pet.vertex_groups.new(name="Face detail")
    for v in pet.data.vertices:
        x, y, z = v.co
        if abs(x) < 0.15 and y > .13 and .61 < z < .72:
            detail.add([v.index], 1, "REPLACE")
    modifier = pet.modifiers.new("Desktop mesh", "DECIMATE")
    modifier.ratio = .105
    modifier.vertex_group = detail.name
    modifier.invert_vertex_group = True
    modifier.vertex_group_factor = 12
    bpy.ops.object.modifier_apply(modifier=modifier.name)
    pet.vertex_groups.remove(pet.vertex_groups["Face detail"])

    face_patches(pet)

    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.uv.smart_project(angle_limit=radians(68), island_margin=.008)
    bpy.ops.object.mode_set(mode="OBJECT")
    return pet


def recolor_skin(pet, arms):
    name = "Nailong skin tones"
    layer = pet.data.color_attributes.new(name=name, type="FLOAT_COLOR", domain="POINT")
    yellow = Vector((.87, .58, .088))
    cream = Vector((.94, .81, .61))
    brown = Vector((.120, .075, .037))
    for v in pet.data.vertices:
        x, y, z = v.co
        tone = (x / .306) ** 2 + ((z + .032) / .306) ** 2
        belly = (1 - smooth(.75, 1.13, tone)) * smooth(.110, .247, y)
        fingers = 1 - smooth(-.25, -.12, z) if v.index in arms else 0
        toes = 1 - smooth(-.955, -.855, z)
        layer.data[v.index].color = (*yellow.lerp(cream, belly).lerp(brown, max(fingers, toes)), 1)
    material = pet.data.materials[0]
    nodes = material.node_tree.nodes
    bsdf = next(n for n in nodes if n.type == "BSDF_PRINCIPLED")
    vertex_color = nodes.new("ShaderNodeVertexColor")
    vertex_color.layer_name = name
    material.node_tree.links.new(vertex_color.outputs["Color"], bsdf.inputs["Base Color"])


def bake(pet, destination):
    scene = bpy.context.scene
    scene.render.engine = "CYCLES"
    scene.cycles.device = "CPU"
    scene.cycles.samples = 1
    image = bpy.data.images.new("Nailong body albedo", width=ALBEDO_SIZE, height=ALBEDO_SIZE, alpha=False)
    transitions = []
    select(pet)
    for material in pet.data.materials:
        nodes = material.node_tree.nodes
        links = material.node_tree.links
        output = next(n for n in nodes if n.type == "OUTPUT_MATERIAL")
        bsdf = next(n for n in nodes if n.type == "BSDF_PRINCIPLED")
        color = bsdf.inputs["Base Color"]
        emitter = nodes.new("ShaderNodeEmission")
        if color.is_linked:
            links.new(color.links[0].from_socket, emitter.inputs["Color"])
        else:
            emitter.inputs["Color"].default_value = color.default_value
        links.new(emitter.outputs[0], output.inputs["Surface"])
        target = nodes.new("ShaderNodeTexImage")
        target.image = image
        nodes.active = target
        transitions.append((nodes, links, bsdf, output, emitter, target))
    bpy.ops.object.bake(type="EMIT", margin=8)
    for nodes, links, bsdf, output, emitter, target in transitions:
        nodes.remove(emitter)
        nodes.remove(target)
        links.new(bsdf.outputs[0], output.inputs["Surface"])
    # Keep facial colors in solid swatches so sub-pixel face surfaces never sample UV seams.
    rgba = np.empty(ALBEDO_SIZE * ALBEDO_SIZE * 4, dtype=np.float32)
    image.pixels.foreach_get(rgba)
    width = ALBEDO_SIZE + SWATCH_WIDTH
    atlas_pixels = np.ones((ALBEDO_SIZE, width, 4), dtype=np.float32)
    atlas_pixels[:, :ALBEDO_SIZE] = rgba.reshape(ALBEDO_SIZE, ALBEDO_SIZE, 4)
    swatches = {}
    materials = list(pet.data.materials)[4:]
    for i, material in enumerate(materials):
        start = i * ALBEDO_SIZE // len(materials)
        end = (i + 1) * ALBEDO_SIZE // len(materials)
        rgb = np.asarray(material.diffuse_color[:3], dtype=np.float32)
        atlas_pixels[start:end, ALBEDO_SIZE:, :3] = np.where(rgb <= .0031308, rgb * 12.92, 1.055 * rgb ** (1 / 2.4) - .055)
        swatches[4 + i] = ((ALBEDO_SIZE + SWATCH_WIDTH / 2) / width, (start + end) / (2 * ALBEDO_SIZE))
    atlas = bpy.data.images.new("Nailong albedo", width=width, height=ALBEDO_SIZE, alpha=False)
    atlas.pixels.foreach_set(atlas_pixels.ravel())
    atlas.filepath_raw = str(destination)
    atlas.file_format = "PNG"
    atlas.save()
    atlas.pack()
    bpy.data.images.remove(image)
    return swatches


def expressions(pet):
    memberships = [set() for _ in pet.data.vertices]
    for face in pet.data.polygons:
        name = pet.data.materials[face.material_index].name
        for index in face.vertices:
            memberships[index].add((face.material_index, name))
    pet.shape_key_add(name="Basis")
    mouth = pet.shape_key_add(name="Laugh jaw")
    eyes = pet.shape_key_add(name="Squint")
    targets = pet.data.attributes["Expression target"]
    for vertex in pet.data.vertices:
        i = vertex.index
        mats = memberships[i]
        if any(name in ("Lip rim", "Mouth", "Teeth", "Tongue") for _, name in mats):
            mouth.data[i].co = targets.data[i].vector
        elif any(name.startswith(("Eye.", "Eyelid.")) for _, name in mats):
            eyes.data[i].co = targets.data[i].vector
    pet.data.attributes.remove(targets)
    return mouth, eyes


def arm_regions(pet):
    """Trace the connected arm below its shoulder, keeping fingertips on the same skin chain."""
    neighbors = defaultdict(list)
    for edge in pet.data.edges:
        a, b = edge.vertices
        neighbors[a].append(b)
        neighbors[b].append(a)
    points = [v.co for v in pet.data.vertices]
    arms = set()
    for sign in (1, -1):
        candidates = {i for i, p in enumerate(points) if -.32 < p.z < .252 and sign * p.x > .24}
        tip = Vector((sign * .40, .07, -.24))
        first = min(candidates, key=lambda i: (tip - points[i]).length_squared)
        visited, queue = {first}, [first]
        while queue:
            for i in neighbors[queue.pop()]:
                if i in candidates and i not in visited:
                    visited.add(i)
                    queue.append(i)
        arms.update(visited)
    return arms


def attach(pet, arms):
    data = bpy.data.armatures.new("Nailong skeleton")
    rig = bpy.data.objects.new("Nailong rig", data)
    bpy.context.collection.objects.link(rig)
    rig.show_in_front = True
    select(rig)
    bpy.ops.object.mode_set(mode="EDIT")

    def bone(name, head, tail, parent=None):
        result = data.edit_bones.new(name)
        result.head, result.tail = classic_proportions(head), classic_proportions(tail)
        if parent:
            result.parent = data.edit_bones[parent]
        return result

    bone("hips", (0, -.035, -.34), (0, -.02, -.08))
    bone("chest", (0, -.02, -.08), (0, -.014, .45), "hips")
    bone("head", (0, -.014, .45), (0, .02, .84), "chest")
    for name, sign in (("L", 1), ("R", -1)):
        shoulder = (sign * .290, -.019, .332)
        elbow = (sign * .428, -.022, .028)
        wrist = (sign * .401, .052, -.207)
        hip = (sign * .169, -.062, -.32)
        knee = (sign * .211, -.081, -.59)
        ankle = (sign * .260, -.108, -.845)
        bone("upper_arm." + name, shoulder, elbow, "chest")
        bone("forearm." + name, elbow, wrist, "upper_arm." + name)
        bone("hand." + name, wrist, (sign * .394, .082, -.275), "forearm." + name)
        bone("thigh." + name, hip, knee, "hips")
        bone("shin." + name, knee, ankle, "thigh." + name)
        bone("foot." + name, ankle, (sign * .272, .114, -.946), "shin." + name)
    bpy.ops.object.mode_set(mode="OBJECT")
    groups = {bone.name: pet.vertex_groups.new(name=bone.name) for bone in data.bones}
    for v in pet.data.vertices:
        x, y, z = v.co
        side = "L" if x >= 0 else "R"
        identified = v.index in arms
        shoulder = smooth(.278, .408, abs(x)) * (1 - smooth(.31, .47, z))
        arm = (1 - smooth(.147, .302, z)) + smooth(.147, .302, z) * shoulder if identified else shoulder * smooth(.218, .275, z)
        leg = (1 - smooth(-.47, -.25, z)) * smooth(.008, .103, abs(x)) if not identified else 0
        head = smooth(.30, .58, z)
        hips = 1 - smooth(-.33, .045, z)
        elbow = 1 - smooth(-.045, .098, z)
        wrist = 1 - smooth(-.248, -.172, z)
        knee = 1 - smooth(-.66, -.52, z)
        ankle = 1 - smooth(-.907, -.805, z)
        core = 1 - arm - leg
        weights = {
            "hips": core * (1 - head) * hips,
            "chest": core * (1 - head) * (1 - hips),
            "head": core * head,
            "upper_arm." + side: arm * (1 - elbow),
            "forearm." + side: arm * elbow * (1 - wrist),
            "hand." + side: arm * elbow * wrist,
            "thigh." + side: leg * (1 - knee),
            "shin." + side: leg * knee * (1 - ankle),
            "foot." + side: leg * knee * ankle,
        }
        for name, weight in weights.items():
            if weight > .0001:
                groups[name].add([v.index], weight, "REPLACE")
    modifier = pet.modifiers.new("Nailong armature", "ARMATURE")
    modifier.object = rig
    modifier.use_deform_preserve_volume = True
    pet.parent = rig
    for shape in pet.data.shape_keys.key_blocks:
        for point in shape.data:
            point.co = classic_proportions(point.co)
    for v in pet.data.vertices:
        v.co = pet.data.shape_keys.reference_key.data[v.index].co
    return rig


def rotate(rig, name, x=0, y=0, z=0):
    bone = rig.pose.bones[name]
    bone.rotation_mode = "XYZ"
    bone.rotation_euler = (radians(x), radians(y), radians(z))


def aim(rig, name, vector, weight):
    bone = rig.pose.bones[name]
    basis = bone.bone.matrix_local.to_quaternion()
    rest = bone.bone.tail_local - bone.bone.head_local
    target = rest.normalized().lerp(vector.normalized(), weight)
    turn = rest.rotation_difference(target)
    bone.rotation_mode = "QUATERNION"
    bone.rotation_quaternion = basis.inverted() @ turn @ basis
    return turn


def hug(rig, side, sign, amount, bounce, feed):
    raise_hand = side == "L" and feed > 0
    upper_bone = rig.data.bones["upper_arm." + side]
    lower_bone = rig.data.bones["forearm." + side]
    shoulder = upper_bone.head_local
    wrist = Vector((sign * .124, .526, .052 + .030 * bounce + (.42 * feed if raise_hand else 0)))
    delta = wrist - shoulder
    axis = delta.normalized()
    a, b = upper_bone.length, lower_bone.length
    reach = min(delta.length, .975 * (a + b))
    along = (reach * reach + a * a - b * b) / (2 * reach)
    pole = Vector((sign * .90, .09, -.53))
    pole -= pole.dot(axis) * axis
    upper = axis * along + pole.normalized() * sqrt(max(0, a * a - along * along))
    fore = axis * reach - upper
    hand = Vector((-sign * .082, .011, -.016 + (.12 * feed if raise_hand else 0)))
    upper_turn = aim(rig, "upper_arm." + side, upper, amount)
    fore_turn = aim(rig, "forearm." + side, upper_turn.inverted() @ fore, amount)
    aim(rig, "hand." + side, fore_turn.inverted() @ (upper_turn.inverted() @ hand), amount)


def pose(rig, mouth, eyes, clip, t, laugh_level):
    swing = sin(t * 2 * pi)
    breath = sin(t * 2 * pi / 3)
    laugh = (smooth(.07, .50, t) * (1 - smooth(3.88, 4.28, t))) if clip == "Laugh" else 0
    pulse = sin(t * 2 * pi * 3.1) * (.45 + .55 * laugh_level)
    rock = sin(t * 2 * pi * .82 - .6)
    feed = (smooth(0, .36, t) * (1 - smooth(1.2, 2.1, t))) if clip == "Feed" else 0
    sleep = 1 if clip == "Sleep" else 0
    walk = 1 if clip == "Walk" else 0
    rotate(rig, "hips", z=3 * swing * walk)
    lean = -14 - 26 * rock - 5 * pulse
    rotate(rig, "chest", x=lean * laugh - 9 * sleep - 5 * feed + .9 * breath,
           z=3.5 * swing * laugh + 3 * sleep)
    rotate(rig, "head", x=(-.58 * lean - 4 * rock + 2 * pulse) * laugh - 13 * sleep + 5 * feed,
           z=-2 * swing * laugh + 6 * sleep)
    hips = rig.pose.bones["hips"]
    hips.location = (0, .012 * walk * abs(swing), 0)
    for side, sign in (("L", 1), ("R", -1)):
        hug(rig, side, sign, max(laugh, feed, .19 * sleep, .22 if clip == "Idle" else 0), pulse, feed)
        rotate(rig, "thigh." + side, x=sign * swing * walk * 21)
        rotate(rig, "shin." + side, x=-max(0, sign * swing) * walk * 24)
        rotate(rig, "foot." + side, x=sign * swing * walk * -9)
    mouth.value = max(laugh * (.37 + .63 * laugh_level), feed * (.16 + .13 * max(0, pulse)))
    blink = 1 - smooth(.039, .115, abs(t - 1.34))
    eyes.value = max(laugh, sleep, blink if clip == "Idle" else 0)


def laugh_envelope(path):
    with wave.open(str(path), "rb") as audio:
        if audio.getnchannels() != 1 or audio.getsampwidth() != 2:
            raise ValueError("Laugh recording must be mono 16-bit PCM.")
        rate = audio.getframerate()
        samples = np.frombuffer(audio.readframes(audio.getnframes()), dtype="<i2").astype(np.float32) / 32768
    if abs(len(samples) / rate - DURATION["Laugh"]) > .01:
        raise ValueError("Laugh recording duration must match the animation.")
    levels = []
    for frame in range(round(DURATION["Laugh"] * POSE_RATE)):
        center = int(frame * rate / POSE_RATE)
        span = int(.048 * rate)
        window = samples[max(0, center - span):center + span]
        levels.append(smooth(.011, .15, float(np.sqrt(np.mean(window ** 2)))))
    return levels


def keyframe(pet, rig, mouth, eyes, laugh_levels):
    scene = bpy.context.scene
    ranges = {}
    frame = 1
    for clip, seconds in DURATION.items():
        count = round(seconds * POSE_RATE)
        ranges[clip] = (frame, count)
        scene.timeline_markers.new(clip, frame=frame)
        for sample in range(count):
            t = sample / POSE_RATE
            if clip in {"Laugh", "Feed"} and sample == count - 1:
                t = seconds
            level = laugh_levels[sample] if clip == "Laugh" else 0
            pose(rig, mouth, eyes, clip, t, level)
            for bone in rig.pose.bones:
                prop = "rotation_quaternion" if bone.rotation_mode == "QUATERNION" else "rotation_euler"
                bone.keyframe_insert(data_path=prop, frame=frame + sample, group=bone.name)
            rig.pose.bones["hips"].keyframe_insert(data_path="location", frame=frame + sample, group="hips")
            mouth.keyframe_insert(data_path="value", frame=frame + sample)
            eyes.keyframe_insert(data_path="value", frame=frame + sample)
        frame += count
    scene.render.fps = POSE_RATE
    scene.frame_end = frame - 1
    return ranges


def mesh_archive(pet, ranges, swatches, destination):
    mesh = pet.data
    mesh.calc_loop_triangles()
    uv = mesh.uv_layers.active.uv
    unique = {}
    indices = []
    points = []
    for tri in mesh.loop_triangles:
        for loop in tri.loops:
            vertex = mesh.loops[loop].vertex_index
            u, v = uv[loop].vector
            u, v = swatches.get(tri.material_index, (u * ALBEDO_SIZE / (ALBEDO_SIZE + SWATCH_WIDTH), v))
            key = (vertex, u, v)
            if key not in unique:
                unique[key] = len(points)
                points.append(key)
            indices.append(unique[key])
    assert len(points) < 65536
    payload = bytearray(b"NPM1")
    payload += struct.pack("<H", len(points))
    payload += np.asarray([(u, 1 - v) for _, u, v in points], dtype="<f4").tobytes()
    payload += struct.pack("<I", len(indices))
    payload += np.asarray(indices, dtype="<u2").tobytes()
    scene = bpy.context.scene
    scale = 2.43 / HEIGHT
    v_indices = np.asarray([vertex for vertex, _, _ in points])
    co = np.zeros(len(mesh.vertices) * 3, dtype=np.float32)
    normals = np.zeros_like(co)
    for _, (first, count) in ranges.items():
        payload += struct.pack("<H", count)
        for frame in range(first, first + count):
            scene.frame_set(frame)
            evaluated = pet.evaluated_get(bpy.context.evaluated_depsgraph_get()).data
            evaluated.vertices.foreach_get("co", co)
            evaluated.vertices.foreach_get("normal", normals)
            vertices = co.reshape(-1, 3)[v_indices][:, [0, 2, 1]].copy()
            vertices[:, 0] *= -1
            vertices[:, 1] -= GROUND
            samples = normals.reshape(-1, 3)[v_indices][:, [0, 2, 1]].copy()
            samples[:, 0] *= -1
            payload += np.rint(vertices * (scale * 8192)).astype("<i2").tobytes()
            payload += np.rint(np.clip(samples, -1, 1) * 127).astype("i1").tobytes()
    encoder = zlib.compressobj(level=8, wbits=-15)
    destination.write_bytes(encoder.compress(payload) + encoder.flush())
    print(f"Desktop mesh: {len(points)} vertices, {len(indices) // 3} triangles")


def save_scene(pet, rig, ranges, destination):
    scene = bpy.context.scene
    scene.frame_set(ranges["Laugh"][0])
    select(rig)
    for screen in bpy.data.screens:
        for area in screen.areas:
            if area.type == "VIEW_3D":
                space = area.spaces.active
                space.region_3d.view_distance = 3.4
                space.region_3d.view_location = (0, 0, -.04)
                space.region_3d.view_rotation = Vector((-.22, -1, -.14)).to_track_quat("-Z", "Y")
                space.overlay.show_floor = False
                space.shading.type = "MATERIAL"
    bpy.context.preferences.filepaths.save_version = 0
    bpy.ops.wm.save_as_mainfile(filepath=str(destination), compress=True)


def main():
    parser = ArgumentParser()
    parser.add_argument("upstream", type=Path)
    args = parser.parse_args()
    media = ROOT.parent / "media"
    pet = prepare(args.upstream.resolve())
    arms = arm_regions(pet)
    recolor_skin(pet, arms)
    swatches = bake(pet, media / "model-albedo.png")
    mouth, eyes = expressions(pet)
    rig = attach(pet, arms)
    ranges = keyframe(pet, rig, mouth, eyes, laugh_envelope(media / "laugh.wav"))
    mesh_archive(pet, ranges, swatches, media / "model.bin")
    save_scene(pet, rig, ranges, ROOT / "nailong.blend")


if __name__ == "__main__":
    main()

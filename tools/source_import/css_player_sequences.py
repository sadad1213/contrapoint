"""Bake CSS v44 sequence autolayers, including weighted hand/weapon poses.

SourceIO imports raw animation descriptors. Player sequences additionally apply
absolute hand poses and additive body/aim/attack layers. Native v44 autolayers
use two int16 indices (24 bytes), unlike SourceIO's current int32 reader.
"""
import numpy as np
from mathutils import Quaternion, Vector
from SourceIO.library.models.mdl.load_animations import AnimationData
from SourceIO.library.models.mdl.structs.local_animation import ANIM_DTYPE


def bake_sequences(mdl, buffer, wanted, skip_layers=()):
    descriptors = mdl.anim_descs
    raw_cache = {}
    sequence_cache = {}

    def raw(index):
        if index not in raw_cache:
            raw_cache[index] = descriptors[index].read_animations(buffer, mdl.bones)
        return raw_cache[index]

    def metadata(index):
        if index not in sequence_cache:
            seq = mdl.sequences[index]
            with buffer.read_from_offset(seq._entry_offset + 148):
                count, offset = buffer.read_fmt('2i')
            layers = []
            with buffer.read_from_offset(seq._entry_offset + offset):
                for _ in range(count):
                    layers.append(buffer.read_fmt('hhI4f'))
            weights = seq.get_bone_weights(buffer, len(mdl.bones))
            sequence_cache[index] = (seq, layers, weights)
        return sequence_cache[index]

    def evaluate(index, cycle, depth=0, base=None):
        assert depth < 12, 'Recursive CSS sequence'
        seq, layers, weights = metadata(index)
        # Neutral pose for the 3x3 aim blend, single descriptor for other clips.
        desc_index = seq.anim_desc_indices[len(seq.anim_desc_indices) // 2]
        desc = descriptors[desc_index]
        frames = raw(desc_index)
        delta = bool(seq.flags & 4)
        frame = np.clip(cycle, 0, 1) * (desc.frame_count - 1)
        first = int(frame)
        fraction = float(frame - first)
        poses = []
        for bone in mdl.bones:
            values = frames.get(bone.name)
            if values is None:
                pos = Vector((0, 0, 0) if delta else bone.position)
                q = (0, 0, 0, 1) if delta else bone.quat
                rot = Quaternion((q[3], q[0], q[1], q[2]))
            else:
                a = values[min(first, len(values) - 1)]
                b = values[min(first + 1, len(values) - 1)]
                pos = Vector(a['pos']).lerp(Vector(b['pos']), fraction)
                qa, qb = a['rot'], b['rot']
                rot = Quaternion((qa[3], qa[0], qa[1], qa[2])).slerp(Quaternion((qb[3], qb[0], qb[1], qb[2])), fraction)
            poses.append([pos, rot.normalized()])
        if base is not None:
            assert not delta, 'Delta sequences must be accumulated onto an upper pose'
            # Source accumulates the weighted sequence onto the lower-body pose
            # before applying its non-local autolayers. In particular, knife and
            # pistol upper sequences have zero spine weights: their aim/body
            # offsets must start from the crouching/standing spine, not bind pose.
            for i, (pos, rot) in enumerate(poses):
                amount = float(np.clip(weights[i], 0, 1))
                poses[i] = [base[i][0].lerp(pos, amount),
                            base[i][1].slerp(rot, amount).normalized()]
        for layer_id, pose_id, flags, start, peak, tail, end in layers:
            if mdl.sequences[layer_id].name in skip_layers:
                continue
            weight = 1.0
            layer_cycle = cycle
            if end > start:
                if cycle < start or cycle > end:
                    continue
                if cycle < peak and peak > start:
                    weight = (cycle - start) / (peak - start)
                elif cycle > tail and end > tail:
                    weight = (end - cycle) / (end - tail)
                layer_cycle = (cycle - start) / (end - start)
            target, layer_delta = evaluate(layer_id, layer_cycle, depth + 1)
            layer_weights = metadata(layer_id)[2]
            for i, (p, q) in enumerate(target):
                amount = float(np.clip(weight * layer_weights[i], 0, 1))
                if amount == 0:
                    continue
                if layer_delta:
                    poses[i][0] += p * amount
                    # Match Source's STUDIO_POST (QuaternionMA) versus pre-addition.
                    add = Quaternion().slerp(q, amount)
                    post = bool(metadata(layer_id)[0].flags & 16)
                    poses[i][1] = ((poses[i][1] @ add) if post else (add @ poses[i][1])).normalized()
                else:
                    poses[i][0] = poses[i][0].lerp(p, amount)
                    poses[i][1] = poses[i][1].slerp(q, amount).normalized()
        return poses, delta

    def upper_pose(index, cycle):
        name = mdl.sequences[index].name
        lower_name = 'Crouch_Idle_Lower' if name.startswith('Crouch_') else 'Idle_lower'
        lower_index = next((i for i, s in enumerate(mdl.sequences) if s.name == lower_name), None)
        # The separate AK include already supplies its own complete upper pose.
        if lower_index is None:
            return evaluate(index, cycle)
        base, _ = evaluate(lower_index, 0)
        return evaluate(index, cycle, base=base)

    results = []
    # Directional gaits are blend samples of move_run/move_walk sequences,
    # rather than separately named sequences. Retain their raw samples.
    for index, desc in enumerate(descriptors):
        if not desc.name.startswith('a_') or not wanted(desc.name):
            continue
        frames = dict(raw(index))
        results.append(AnimationData(desc.name, desc.fps, desc.frame_count,
                                     [b.name for b in mdl.bones], frames, True, False))
    for index, seq in enumerate(mdl.sequences):
        # Export usable authored sequences, rather than their incomplete raw clips.
        if not wanted('@' + seq.name):
            continue
        desc = descriptors[seq.anim_desc_indices[len(seq.anim_desc_indices) // 2]]
        count = max(1, desc.frame_count)
        frames = {bone.name: np.zeros(count, ANIM_DTYPE) for bone in mdl.bones}
        for frame in range(count):
            cycle = frame / max(1, count - 1)
            if '_Upper_' in seq.name or '_Reload_' in seq.name:
                poses, delta = upper_pose(index, cycle)
            else:
                poses, delta = evaluate(index, cycle)
            if delta:
                stance, weapon = seq.name.split('_Shoot_')
                base_name = stance + '_Upper_' + ('AK' if weapon.upper() == 'RIFLE' else weapon.upper())
                base_index = next((i for i, s in enumerate(mdl.sequences) if s.name.lower() == base_name.lower()), None)
                if base_index is None:
                    continue
                base, _ = upper_pose(base_index, cycle)
                for i, (pos, rot) in enumerate(poses):
                    rotation = (base[i][1] @ rot) if seq.flags & 16 else (rot @ base[i][1])
                    poses[i] = [base[i][0] + pos, rotation.normalized()]
            for bone, (pos, rot) in zip(mdl.bones, poses):
                frames[bone.name][frame]['pos'] = pos
                frames[bone.name][frame]['rot'] = (rot.x, rot.y, rot.z, rot.w)
        results.append(AnimationData('@' + seq.name, desc.fps, count,
                                     list(frames), frames, bool(seq.flags & 1), False))
    return results

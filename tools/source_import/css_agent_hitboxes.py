"""Extract the original bone-local hitboxes without importing unrelated audio."""
import json
import struct
from pathlib import Path
from css_agents import AGENTS, model_name


def import_hitboxes(project, agents=AGENTS):
    output = project / 'assets/physics/css_damage'
    output.mkdir(parents=True, exist_ok=True)
    for agent in agents:
        path = project / f'assets/players/{agent}/source/css/models/player/{model_name(agent)}.mdl'
        data = path.read_bytes()
        assert data[:4] == b'IDST' and struct.unpack_from('<i', data, 4)[0] == 44
        count, start = struct.unpack_from('<ii', data, 156)
        bones = []
        for i in range(count):
            bone = start + i * 216
            name = bone + struct.unpack_from('<i', data, bone)[0]
            bones.append(data[name:data.index(b'\0', name)].decode('ascii'))
        count, start = struct.unpack_from('<ii', data, 172)
        assert count == 1
        _, count, offset = struct.unpack_from('<iii', data, start)
        boxes = []
        for i in range(count):
            bone, group, *bounds = struct.unpack_from('<ii6f', data, start + offset + i * 68)
            boxes.append({'bone': bones[bone], 'group': group, 'min': bounds[:3], 'max': bounds[3:]})
        (output / f'{agent}_hitboxes.json').write_text(json.dumps(boxes, indent=2) + '\n')
        print('CSS_HITBOXES_IMPORTED', agent, len(boxes))


if __name__ == '__main__':
    import sys
    import_hitboxes(Path(__file__).resolve().parents[2], sys.argv[1:] or AGENTS)

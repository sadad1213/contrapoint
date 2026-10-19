"""Extract selected vanilla CSS weapons and convert view/world models.

Run with system Python; conversion runs in isolated background Blender processes.
"""
import argparse
from concurrent.futures import ThreadPoolExecutor
import hashlib
import json
from pathlib import Path
import shutil
import subprocess

STEMS = {
    'ak47': 'rif_ak47', 'deagle': 'pist_deagle', 'usp': 'pist_usp', 'm4a1': 'rif_m4a1',
    'awp': 'snip_awp', 'scout': 'snip_scout', 'aug': 'rif_aug', 'elite': 'pist_elite',
    'famas': 'rif_famas', 'fiveseven': 'pist_fiveseven', 'g3sg1': 'snip_g3sg1',
    'galil': 'rif_galil', 'glock': 'pist_glock18', 'm249': 'mach_m249para',
    'm3': 'shot_m3super90', 'mac10': 'smg_mac10', 'mp5navy': 'smg_mp5',
    'p228': 'pist_p228', 'p90': 'smg_p90', 'sg550': 'snip_sg550',
    'sg552': 'rif_sg552', 'tmp': 'smg_tmp', 'ump45': 'smg_ump45', 'xm1014': 'shot_xm1014',
}
SOUND_DIRS = {'glock': 'glock', 'mp5navy': 'mp5navy'}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    for option in ("game-root", "blender", "sourceio", "content-root", "project"):
        parser.add_argument("--" + option, type=Path, required=True)
    parser.add_argument("--weapons", nargs="+", choices=list(STEMS), required=True)
    parser.add_argument('--skip-existing', action='store_true')
    args = parser.parse_args()
    stems = STEMS
    archive = args.game_root / "cstrike/cstrike_pak_dir.vpk"
    vpk = args.game_root / "bin/vpk.exe"
    entries = subprocess.check_output([str(vpk), "l", str(archive)], text=True).splitlines()
    selected = [entry for entry in entries if any(
        entry.startswith(f"models/weapons/{prefix}_{stems[name]}.") and
        entry.endswith((".mdl", ".vvd", ".dx90.vtx", ".phy"))
        for name in args.weapons for prefix in ("v", "w")) or any(
        name == 'elite' and entry.startswith('models/weapons/w_pist_elite_dropped.')
        and entry.endswith(('.mdl', '.vvd', '.dx90.vtx', '.phy')) for name in args.weapons) or any(
        entry.startswith(f"materials/models/weapons/v_models/{stems[name]}/") or
        entry.startswith(f"materials/models/weapons/w_models/w_{stems[name]}/") or
        (name == 'm249' and entry.startswith('materials/models/weapons/w_models/w_mach_m249/')) or
        entry.startswith(f"sound/weapons/{SOUND_DIRS.get(name, name)}/") for name in args.weapons) or
        entry.startswith("materials/models/weapons/v_models/hands/") or (
        any(name in args.weapons for name in ('scout', 'g3sg1', 'sg550')) and
        entry.startswith("materials/models/weapons/v_models/snip_awp/v_awp_scope"))]
    args.content_root.mkdir(parents=True, exist_ok=True)
    for entry in selected:
        (args.content_root / entry).parent.mkdir(parents=True, exist_ok=True)
    subprocess.run([str(vpk), "x", str(archive), *selected], cwd=args.content_root, check=True)

    def convert(job):
        name, world = job
        output = args.project / "assets/weapons" / name
        if world:
            output /= "world"
        if world == 'dropped':
            output /= 'dropped'
        if args.skip_existing and (output / (name + ('_world' if world else '') + '.glb')).exists() and (output / 'import_report.json').exists():
            return
        output.mkdir(parents=True, exist_ok=True)
        log = output / "source/conversion.log"
        log.parent.mkdir(exist_ok=True)
        (log.parent / ".gdignore").touch()
        command = [str(args.blender), "--background", "--factory-startup", "--python-exit-code", "1",
                   "--python", str(args.project / "tools/source_import/import_source1_model.py"), "--",
                   "--sourceio", str(args.sourceio), "--content-root", str(args.content_root),
                   "--model", str(args.content_root / f"models/weapons/{'w' if world else 'v'}_{stems[name]}{'_dropped' if world == 'dropped' else ''}.mdl"),
                   "--output", str(output), "--name", name + ("_world" if world else ""), "--overwrite"]
        if world:
            command.append("--no-animations")
        with log.open("w", encoding="utf-8") as stream:
            subprocess.run(command, stdout=stream, stderr=subprocess.STDOUT, check=True)
        print("CONVERTED", name, "world" if world else "view", flush=True)

    with ThreadPoolExecutor(max_workers=2) as pool:
        jobs = [(name, world) for name in args.weapons for world in (False, True)]
        if 'elite' in args.weapons:
            jobs.append(('elite', 'dropped'))
        list(pool.map(convert, jobs))
    for name in args.weapons:
        output = args.project / "assets/weapons" / name
        originals = output / "source/css"
        records = []
        for entry in selected:
            shared_scope = name in ('scout', 'g3sg1', 'sg550') and entry.startswith("materials/models/weapons/v_models/snip_awp/v_awp_scope")
            if name not in entry and stems[name] not in entry and "/hands/" not in entry and not shared_scope:
                continue
            source = args.content_root / entry
            target = originals / entry
            target.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(source, target)
            records.append({"path": entry, "sha256": hashlib.sha256(source.read_bytes()).hexdigest()})
            if entry.startswith(f"sound/weapons/{SOUND_DIRS.get(name, name)}/"):
                (output / "sounds").mkdir(exist_ok=True)
                shutil.copy2(source, output / "sounds" / source.name)
        (output / "source/provenance.json").write_text(json.dumps(
            {"archive": str(archive), "files": records}, indent=2), encoding="utf-8")


if __name__ == "__main__":
    main()

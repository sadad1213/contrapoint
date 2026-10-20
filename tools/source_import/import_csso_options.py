"""Import CSSO's own Options layout and labels; no CSS/CSGO substitution."""
from pathlib import Path
import hashlib
import json
import re
import shutil

ROOT = Path(__file__).resolve().parents[2]
SOURCE = Path(r'C:/Program Files (x86)/Steam/steamapps/common/Counter-Strike Source/csso/resource')


def keyvalues(text):
    tokens = iter(re.findall(r'"((?:\\.|[^"\\])*)"|([{}])', re.sub(r'//[^\n]*', '', text)))
    def block():
        result = {}
        for quoted, brace in tokens:
            if brace == '}':
                return result
            key = quoted
            value, brace = next(tokens)
            result[key] = block() if brace == '{' else value
        return result
    return block()


def main():
    output = ROOT / 'assets/ui/csso/options'
    references = ROOT / 'tools/source_import/references/options'
    output.mkdir(parents=True, exist_ok=True)
    references.mkdir(parents=True, exist_ok=True)
    layouts, records = {}, []
    for path in sorted(SOURCE.glob('csoptions*.res')):
        shutil.copy2(path, references / path.name)
        layouts[path.stem] = next(iter(keyvalues(path.read_text()).values()))
        records.append({'source': str(path), 'sha256': hashlib.sha256(path.read_bytes()).hexdigest()})
    strings = {}
    for path in [SOURCE.parent.parent / 'platform/resource/vgui_english.txt',
                 SOURCE.parent.parent / 'hl2/resource/gameui_english.txt', SOURCE / 'gameui_english.txt']:
        if not path.exists():
            continue
        data = path.read_bytes()
        parsed = keyvalues(data.decode('utf-16' if data.startswith((b'\xff\xfe', b'\xfe\xff')) else 'utf-8-sig'))
        lang = next(iter(parsed.values()))
        strings.update(lang.get('Tokens', {}))
        records.append({'source': str(path), 'sha256': hashlib.sha256(data).hexdigest()})
    strings = {k: v for k, v in strings.items() if isinstance(v, str)
               and (k.startswith('GameUI_Options') or k == 'GameUI_DeveloperConsoleCheck')}
    (output / 'layout.json').write_text(json.dumps(layouts, indent=2), encoding='utf-8')
    (output / 'english.json').write_text(json.dumps(strings, indent=2), encoding='utf-8')
    (references / 'provenance.json').write_text(json.dumps(records, indent=2), encoding='utf-8')
    print('CSSO_OPTIONS_IMPORTED', len(layouts), 'layouts')


if __name__ == '__main__':
    main()

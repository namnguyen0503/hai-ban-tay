"""Snapshot/compare outputs from two independent final Blender builds."""
import argparse
import hashlib
import json
from pathlib import Path

ART = Path(__file__).resolve().parents[2] / 'Staging/art_env'
NAMES = ('NhaRong', 'SaigonStreet', 'Marseille')
COMMON = ('layout.json', 'stats.json', 'preview_overview.png',
          'preview_spawn.png', 'preview_beat.png', 'preview_top.png',
          'preview_top_raw.png', 'map_projection.json')


def hashes():
    return {f'{name}/{file}': hashlib.sha256((ART/name/file).read_bytes()).hexdigest()
            for name in NAMES for file in (name+'.fbx',)+COMMON}


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--snapshot', action='store_true')
    args = parser.parse_args()
    current = hashes()
    baseline = ART/'determinism_before.json'
    if args.snapshot:
        baseline.write_text(json.dumps(current, indent=2)+'\n')
        print('Snapshot:', len(current), 'files')
        return
    previous = json.loads(baseline.read_text())
    assert previous.keys() == current.keys(), 'Determinism manifest changed'
    mismatches = [n for n in current if current[n] != previous[n]]
    result = {
        'status': 'FAIL' if mismatches else 'PASS',
        'independent_full_builds': 2,
        'files': [{'file': n, 'sha256_before': previous[n], 'sha256_after': sha}
                  for n, sha in current.items()],
        'mismatches': mismatches,
        'blender_version': '4.2.23', 'seed': 1911,
        'cycles': 'CPU, one render thread, 24 fixed samples, OMP_NUM_THREADS=1',
        'fbx_ids': 'SHA-256 of stable Blender exporter string keys',
        'fbx_timestamp': '1911-06-05 12:00:00',
        'png_metadata': 'wall-clock/render-time metadata removed; pixels unchanged',
        'blend_container_byte_identity': 'not asserted; geometry fingerprint in stats.json'
    }
    (ART/'determinism.json').write_text(json.dumps(result, indent=2)+'\n')
    print(result['status'], len(current), 'files; mismatches:', mismatches)
    assert not mismatches, 'Independent builds differ; stop and inspect determinism.json'


if __name__ == '__main__':
    main()

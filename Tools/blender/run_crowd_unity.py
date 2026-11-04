"""Run Unity in a separate process session and record its actual exit status."""
import hashlib
import os
import json
import subprocess
import sys
import time
from pathlib import Path

ROOT=Path(__file__).resolve().parents[2]
SCRATCH=ROOT/'Staging/characters/_scratch/crowd'
UNITY=Path(os.environ.get("UNITY", str(Path.home() / "Unity/Hub/Editor/6000.3.25f1/Editor/Unity")))


def main():
    started=time.time()
    args=[str(UNITY),'-batchmode','-nographics','-projectPath',str(SCRATCH/'unity_validation'),
        '-executeMethod','ValidateCrowd.Run','-logFile',str(SCRATCH/'unity_final.log')]
    with (SCRATCH/'unity_stdout.log').open('w') as output:
        process=subprocess.run(args,start_new_session=True,stdout=output,stderr=subprocess.STDOUT)
    report=SCRATCH/'unity_validation.json'
    fresh=report.is_file() and report.stat().st_mtime>=started
    evidence=json.loads(report.read_text()) if fresh else {}
    receipt={'exit_code':process.returncode,'elapsed_s':round(time.time()-started,3),
        'fresh_report':fresh,'report_passed':evidence.get('passed',False),
        'report_sha256':hashlib.sha256(report.read_bytes()).hexdigest() if fresh else None,
        'command':args,'separate_process_session':True}
    (SCRATCH/'unity_process.json').write_text(json.dumps(receipt,indent=2))
    print(json.dumps(receipt),flush=True)
    if process.returncode!=0 or not fresh or not evidence.get('passed'):sys.exit(1)


if __name__=='__main__':main()

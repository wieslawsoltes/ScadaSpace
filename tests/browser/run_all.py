"""Run every native Uno suite with bounded process concurrency and complete failure reporting."""
from __future__ import annotations
import argparse
from concurrent.futures import ThreadPoolExecutor, as_completed
import json
import os
from pathlib import Path
import signal
import subprocess
import sys
import time
from typing import Callable

SUITES = ('smoke','parity','pages','trees','history','workspace','events','hierarchy','styles','symbols','symbol-states')
OUT = Path('TestResults/browser-runs')

def execute_suite(name: str) -> dict:
    if name not in SUITES:
        raise ValueError('Unknown browser suite: '+name)
    OUT.mkdir(parents=True,exist_ok=True)
    started=time.monotonic()
    with (OUT/(name+'.log')).open('w') as log:
        process=subprocess.Popen([sys.executable,str(Path(__file__).with_name(name+'.py'))],
                                 stdout=log,stderr=subprocess.STDOUT,start_new_session=os.name=='posix')
        try:
            code=process.wait(timeout=900)
            return {'suite':name,'returnCode':code,'seconds':time.monotonic()-started}
        except subprocess.TimeoutExpired:
            if os.name=='posix': os.killpg(process.pid,signal.SIGKILL)
            else: process.kill()
            process.wait()
            return {'suite':name,'returnCode':124,'seconds':time.monotonic()-started,'error':'Suite exceeded 900 seconds'}

def run_suites(workers: int, runner: Callable[[str],dict]=execute_suite) -> list[dict]:
    if workers not in (1,2):
        raise ValueError('Use one or two browser workers.')
    rows={}
    with ThreadPoolExecutor(max_workers=workers) as pool:
        pending={pool.submit(runner,name):name for name in SUITES}
        for future in as_completed(pending):
            name=pending[future]
            try:
                result=future.result()
                if result.get('suite')!=name or not isinstance(result.get('returnCode'),int):
                    raise ValueError('Invalid browser runner result')
            except Exception as error:
                result={'suite':name,'returnCode':1,'error':str(error)}
            rows[name]=result
            print(('PASS' if result['returnCode']==0 else 'FAIL')+' BROWSER_SUITE '+name,flush=True)
    return [rows[name] for name in SUITES]

def main() -> int:
    parser=argparse.ArgumentParser();parser.add_argument('--workers',type=int,choices=(1,2),default=2)
    workers=parser.parse_args().workers
    started=time.monotonic();rows=run_suites(workers)
    summary={'workers':workers,'suiteCount':len(rows),'failures':sum(row['returnCode']!=0 for row in rows),
             'seconds':time.monotonic()-started,'results':rows}
    OUT.mkdir(parents=True,exist_ok=True);(OUT/'results.json').write_text(json.dumps(summary,indent=2))
    print(json.dumps(summary,indent=2),flush=True)
    for row in rows:
        if row['returnCode']!=0:
            path=OUT/(row['suite']+'.log')
            if path.exists(): print(path.read_text()[-16000:],flush=True)
    return 1 if summary['failures'] else 0

if __name__=='__main__':
    raise SystemExit(main())

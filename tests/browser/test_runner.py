"""Contract tests for failure-complete orchestration; no browser or managed code is invoked."""
import threading
import time
import unittest
from pathlib import Path
from run_all import SUITES, run_suites

class RunnerTests(unittest.TestCase):
    def test_registry_contains_each_application_suite_once(self):
        self.assertEqual(len(SUITES),11)
        self.assertEqual(len(set(SUITES)),len(SUITES))
        for name in SUITES:self.assertTrue(Path(__file__).with_name(name+'.py').is_file())
    def test_invalid_concurrency_is_rejected_before_work(self):
        for workers in (0,3,-1):
            with self.assertRaises(ValueError):run_suites(workers,lambda _:self.fail('must not execute'))
    def test_all_suites_finish_when_one_fails(self):
        rows=run_suites(2,lambda name:{'suite':name,'returnCode':1 if name=='styles' else 0})
        self.assertEqual([row['suite'] for row in rows],list(SUITES))
        self.assertEqual(sum(row['returnCode']!=0 for row in rows),1)
    def test_exceptions_and_malformed_results_fail_closed(self):
        def runner(name):
            if name=='smoke':raise RuntimeError('fixture failure')
            if name=='pages':return {'suite':name}
            return {'suite':name,'returnCode':0}
        rows=run_suites(2,runner)
        self.assertEqual(len(rows),len(SUITES));self.assertEqual(sum(row['returnCode']!=0 for row in rows),2)
    def test_concurrency_never_exceeds_two_workers(self):
        lock=threading.Lock();active=0;maximum=0
        def runner(name):
            nonlocal active,maximum
            with lock:active+=1;maximum=max(maximum,active)
            time.sleep(.01)
            with lock:active-=1
            return {'suite':name,'returnCode':0}
        rows=run_suites(2,runner)
        self.assertTrue(all(row['returnCode']==0 for row in rows));self.assertLessEqual(maximum,2)

if __name__=='__main__':unittest.main()

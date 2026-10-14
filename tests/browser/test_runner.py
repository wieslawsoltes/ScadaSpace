"""Contract tests for failure-complete orchestration; no browser or managed code is invoked."""
import threading
import time
import unittest
from pathlib import Path
from run_all import SUITES, run_suites
from native_readiness import CommandReadiness, ComponentReadiness

class RunnerTests(unittest.TestCase):
    def test_registry_contains_each_application_suite_once(self):
        self.assertEqual(len(SUITES),16)
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

class NativeReadinessTests(unittest.TestCase):
    @staticmethod
    def sample(tick=1):
        return {'tick':tick,'project':'Fixture','viewId':'view',
                'viewport':{'width':1024,'height':768},
                'bounds':{'x':25,'y':30,'width':26,'height':26}}
    def test_repeated_same_tick_cannot_prove_readiness(self):
        gate=CommandReadiness();sample=self.sample()
        for _ in range(20):self.assertFalse(gate.accept(sample))
        self.assertFalse(gate.accept(self.sample(2)))
        self.assertTrue(gate.accept(self.sample(3)))
    def test_toolbar_relayout_restarts_the_observation_window(self):
        gate=CommandReadiness();self.assertFalse(gate.accept(self.sample(1)))
        moved=self.sample(2);moved['bounds']['x']=52
        self.assertFalse(gate.accept(moved))
        moved['tick']=3;self.assertFalse(gate.accept(moved))
        moved['tick']=4;self.assertTrue(gate.accept(moved))
    def test_view_or_viewport_replacement_resets_readiness(self):
        for changed in ('view','viewport'):
            gate=CommandReadiness();gate.accept(self.sample(1));gate.accept(self.sample(2))
            sample=self.sample(3)
            if changed=='view':sample['viewId']='replacement'
            else:sample['viewport']['width']=1700
            self.assertFalse(gate.accept(sample))
            sample['tick']=4;self.assertFalse(gate.accept(sample))
            sample['tick']=5;self.assertTrue(gate.accept(sample))
    def test_missing_nonfinite_or_offscreen_geometry_is_not_usable(self):
        for invalid in (None,{}, {'x':0,'y':0,'width':0,'height':20},
                        {'x':float('nan'),'y':0,'width':20,'height':20},
                        {'x':1100,'y':0,'width':20,'height':20}):
            gate=CommandReadiness()
            for tick in range(1,5):
                sample=self.sample(tick);sample['bounds']=invalid
                self.assertFalse(gate.accept(sample))
    def test_geometry_snapshots_do_not_alias_mutable_input(self):
        gate=CommandReadiness();sample=self.sample();gate.accept(sample)
        sample['bounds']['x']=52;sample['tick']=2
        self.assertFalse(gate.accept(sample))
        sample['tick']=3;self.assertFalse(gate.accept(sample))
        sample['tick']=4;self.assertTrue(gate.accept(sample))
    def test_invalid_sample_budget_rejected(self):
        for count in (0,1,-1,1.5):
            with self.assertRaises(ValueError):CommandReadiness(count)

class ComponentReadinessTests(unittest.TestCase):
    @staticmethod
    def sample(tick=1):
        return {'tick':tick,'project':'Fixture','viewId':'view-instance','view':'Main','observedView':'Main',
                'viewport':{'width':1024,'height':768},'zoom':.5,'generation':7,'editing':True,'preview':False,
                'bounds':{'x':25,'y':30,'width':85,'height':115},
                'layout':{'id':'pump','width':170,'height':230,'bounds':{'x':25,'y':30,'width':85,'height':115}}}
    def test_stable_prefit_bounds_never_pass(self):
        gate=ComponentReadiness()
        for tick in range(1,8):
            sample=self.sample(tick);sample['zoom']=.75
            self.assertFalse(gate.accept(sample))
    def test_both_dimensions_must_match_current_zoom(self):
        for key in ('width','height'):
            gate=ComponentReadiness()
            for tick in range(1,5):
                sample=self.sample(tick);sample['bounds'][key]+=5
                self.assertFalse(gate.accept(sample))
    def test_two_native_observers_must_agree(self):
        for key in ('x','y','width','height'):
            gate=ComponentReadiness()
            for tick in range(1,5):
                sample=self.sample(tick);sample['layout']['bounds'][key]+=5
                self.assertFalse(gate.accept(sample))
    def test_size_revision_zoom_or_instance_changes_restart_stability(self):
        for changed in ('generation','viewId','componentId','zoom','size'):
            gate=ComponentReadiness();gate.accept(self.sample(1));gate.accept(self.sample(2))
            sample=self.sample(3)
            if changed=='generation':sample['generation']+=1
            elif changed=='viewId':sample['viewId']='replacement'
            elif changed=='componentId':sample['layout']['id']='replacement'
            elif changed=='size':sample['layout']['width']+=.1
            else:sample['zoom']+=.0001
            self.assertFalse(gate.accept(sample))
            sample['tick']=4;self.assertFalse(gate.accept(sample))
            sample['tick']=5;self.assertTrue(gate.accept(sample))
    def test_invalid_sample_clears_an_almost_ready_window(self):
        gate=ComponentReadiness();gate.accept(self.sample(1));gate.accept(self.sample(2))
        invalid=self.sample(3);invalid['layout']=None;self.assertFalse(gate.accept(invalid))
        self.assertFalse(gate.accept(self.sample(4)));self.assertFalse(gate.accept(self.sample(5)))
        self.assertTrue(gate.accept(self.sample(6)))
    def test_duplicate_publication_is_not_a_new_layout_observation(self):
        gate=ComponentReadiness()
        for _ in range(10):self.assertFalse(gate.accept(self.sample(1)))
        self.assertFalse(gate.accept(self.sample(2)));self.assertTrue(gate.accept(self.sample(3)))
    def test_wrong_view_or_preview_cannot_select_a_designer_component(self):
        for key,value in (('observedView','Other'),('editing',False),('preview',True),('viewId',None)):
            gate=ComponentReadiness()
            for tick in range(1,5):
                sample=self.sample(tick);sample[key]=value;self.assertFalse(gate.accept(sample))
    def test_invalid_scale_or_declared_size_is_rejected(self):
        for value in (None,True,0,-1,float('nan'),float('inf'),'0.5'):
            for key in ('zoom','width','height'):
                gate=ComponentReadiness()
                for tick in range(1,5):
                    sample=self.sample(tick)
                    (sample if key=='zoom' else sample['layout'])[key]=value
                    self.assertFalse(gate.accept(sample))

if __name__=='__main__':unittest.main()

import importlib.util
from pathlib import Path
import sys
import unittest
tools=Path(__file__).resolve().parents[1]
sys.path.insert(0,str(tools))
from evaluate_traversal import summarize,validate_schedule
from analyze_traversal_training import analyze


class TraversalEvaluationTests(unittest.TestCase):
    def row(self,**changes):
        r=dict(seed='1000',episode='0',condition='on',success='1',duration='10',snags='1',stucks='0',mean_speed='20',air_fraction='.5',transition_loss_total='4',transitions='2',goal_time='10',swing_fraction='.2',shots='2',attachments='1')
        r.update(changes);return r
    def test_rates_use_simulation_time_and_transition_counts(self):
        r=summarize([self.row(),self.row(duration='30',mean_speed='10',air_fraction='1',transition_loss_total='9',transitions='3',success='0',goal_time='')])
        self.assertEqual(r['snags_per_minute'],3);self.assertEqual(r['mean_speed'],12.5);self.assertEqual(r['mean_transition_loss'],2.6);self.assertEqual(r['goal_seconds'],10)
    def test_no_rows_are_never_reported_as_success(self):
        with self.assertRaises(ValueError):summarize([])
        self.assertFalse(analyze([])['converged'])
    def test_missing_or_duplicate_evaluation_episodes_fail(self):
        validate_schedule([self.row(),self.row(episode='1')],1000,1,2)
        with self.assertRaises(ValueError):validate_schedule([self.row(),self.row()],1000,1,2)
    def test_short_high_success_smoke_run_is_not_convergence(self):
        self.assertFalse(analyze([self.row() for _ in range(10)])['converged'])
    def test_stable_five_hundred_episode_success_is_convergence(self):
        self.assertTrue(analyze([self.row() for _ in range(500)])['converged'])


if __name__=='__main__':unittest.main()

"""Report actual training outcomes without changing rewards or claiming missing runs."""
import argparse
import json
import statistics
from pathlib import Path
from evaluate_traversal import read_rows,summarize


def analyze(rows):
    if not rows:return {'status':'not_run','converged':False,'reason':'No completed Unity training episodes.'}
    result=summarize(rows);window=100
    rates=[statistics.mean(int(r['success']) for r in rows[i:i+window]) for i in range(max(0,len(rows)-500),len(rows),window)]
    converged=len(rows)>=500 and len(rates)==5 and min(rates)>=.85 and max(rates)-min(rates)<=.05
    result.update(status='converged' if converged else 'not_converged',converged=converged,last_window_success_rates=rates)
    reasons=[]
    if sum(int(r['attachments']) for r in rows)==0:reasons.append('No successful web attachment: navigation results do not certify swing coverage. Inspect aim observations and fire action edges before changing rewards.')
    if not converged:
        if len(rows)<500:reasons.append('Fewer than 500 completed episodes: insufficient evidence of convergence.')
        if result['swing_fraction']<.05:reasons.append('Low swing coverage: navigation success alone does not validate web traversal.')
        if result['success_rate']<.85:reasons.append('Check timeout/bounds episodes, ray coverage, target altitude and exploration. Reward configuration was not changed.')
    result['analysis']=reasons;return result


def main():
    p=argparse.ArgumentParser(description=__doc__);p.add_argument('telemetry',type=Path);p.add_argument('--output',type=Path,required=True);args=p.parse_args()
    result=analyze(read_rows(args.telemetry));args.output.parent.mkdir(parents=True,exist_ok=True)
    args.output.write_text(json.dumps(result,indent=2),encoding='utf-8');print(json.dumps(result,indent=2))


if __name__=='__main__':main()
